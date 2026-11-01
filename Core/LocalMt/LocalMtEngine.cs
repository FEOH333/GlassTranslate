using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GlassTranslate.Core.LocalMt
{
    /// <summary>
    /// Opus-MT en→zh 本地翻译引擎（ONNX int8，CPU，完全离线，零网络）。
    ///
    /// 管线：自研 Unigram SentencePiece 分词 → encoder 一次前向 →
    /// decoder 贪心自回归（beam=1）→ 分词还原为中文。
    /// 启动时用一句样本自动校准解码约定（起始 token / 编码器句尾 EOS），
    /// 对冲不同导出变体的差异。单句通常几十毫秒；逐句整句回调，
    /// 由 UI 侧打字机队列渲染成逐字效果。
    /// </summary>
    public sealed class LocalMtEngine : IDisposable
    {
        private const int MaxDecodeSteps = 128;
        private const int MaxSrcTokens = 200;

        private readonly InferenceSession _encoder;
        private readonly InferenceSession _decoder;
        private readonly SpmUnigram _src;
        private readonly SpmUnigram _tgt;
        private readonly int _eosId;
        private readonly int _decoderStartId;
        private readonly bool _appendSrcEos;
        private readonly object _gate = new();

        public string EngineName => "Opus-MT en→zh (int8 · 本地)";
        public static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models", "mt");
        public static string? LastCreateError { get; private set; }

        private LocalMtEngine(InferenceSession enc, InferenceSession dec,
            SpmUnigram src, SpmUnigram tgt, int eos, int start, bool appendEos)
        {
            _encoder = enc;
            _decoder = dec;
            _src = src;
            _tgt = tgt;
            _eosId = eos;
            _decoderStartId = start;
            _appendSrcEos = appendEos;
        }

        public static bool ModelsPresent()
        {
            var dir = ModelsDir;
            return File.Exists(Path.Combine(dir, "encoder.onnx"))
                && File.Exists(Path.Combine(dir, "decoder.onnx"))
                && File.Exists(Path.Combine(dir, "tokenizer.json"));
        }

        public static LocalMtEngine? Create()
        {
            try
            {
                if (!ModelsPresent()) { LastCreateError = "model files missing"; return null; }
                var dir = ModelsDir;
                var enc = new InferenceSession(Path.Combine(dir, "encoder.onnx"));
                var dec = new InferenceSession(Path.Combine(dir, "decoder.onnx"));
                // Marian opus-mt：源/目标共用同一个 Unigram 分词器（tokenizer.json 与 ONNX 同源导出）
                var tok = SpmUnigram.LoadTokenizerJson(Path.Combine(dir, "tokenizer.json"));
                if (tok.Count < 100) { LastCreateError = "tokenizer vocab too small"; return null; }

                var eos = tok.FindId("</s>") ?? 0;

                // ---- 自动校准：起始 token × 是否补句尾 EOS，取能产出合理中文的组合 ----
                var starts = new[] { eos, tok.FindId("<pad>") ?? -1, tok.FindId("<unk>") ?? -1 };
                int bestStart = starts[0];
                bool bestAppend = true;
                double bestScore = double.NegativeInfinity;
                foreach (var st in starts.Distinct())
                {
                    if (st < 0) continue;
                    foreach (var append in new[] { true, false })
                    {
                        var probe = new LocalMtEngine(enc, dec, tok, tok, eos, st, append);
                        var out1 = probe.TranslateCore("the cat sat");
                        double sc = ScoreOutput(out1);
                        if (sc > bestScore) { bestScore = sc; bestStart = st; bestAppend = append; }
                    }
                }
                if (double.IsNegativeInfinity(bestScore))
                {
                    LastCreateError = "calibration failed (no CJK output)";
                    return null;
                }
                return new LocalMtEngine(enc, dec, tok, tok, eos, bestStart, bestAppend);
            }
            catch (Exception ex)
            {
                LastCreateError = ex.Message;
                return null;
            }
        }

        /// <summary>校准打分：含 CJK、无退化重复的输出得分高。</summary>
        private static double ScoreOutput(string? s)
        {
            if (string.IsNullOrEmpty(s)) return double.NegativeInfinity;
            int cjk = s.Count(c => c >= 0x4e00 && c <= 0x9fff);
            if (cjk == 0) return double.NegativeInfinity;
            // 退化检测：去重后字符占比过低 = 单 token 死循环
            int distinct = s.Distinct().Count();
            if (s.Length > 12 && distinct < s.Length * 0.3) return double.NegativeInfinity;
            return cjk - s.Length * 0.1 + distinct * 0.1;
        }

        /// <summary>单句翻译；失败返回 null。线程安全（内部串行）。</summary>
        public string? Translate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            lock (_gate)
            {
                try { return TranslateCore(text.Trim()); }
                catch { return null; }
            }
        }

        /// <summary>批量翻译，逐句回调（index 从 0 起）。句级失败回调 null 由调用方兜底。</summary>
        public Task TranslateBatchAsync(IReadOnlyList<string> lines,
            Action<int, string?> onLine, CancellationToken ct) =>
            Task.Run(() =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (ct.IsCancellationRequested) return;
                    onLine(i, Translate(lines[i]));
                }
            }, ct);

        private string? TranslateCore(string text)
        {
            var ids = _src.Encode(text);
            if (ids.Count == 0) return null;
            var input = ids.Take(MaxSrcTokens).ToList();
            if (_appendSrcEos) input.Add(_eosId);
            int n = input.Count;

            // ---- encoder：input_ids + attention_mask(全 1) → last_hidden_state ----
            var idsTensor = new DenseTensor<long>(new[] { 1, n });
            var maskTensor = new DenseTensor<long>(new[] { 1, n });
            for (int i = 0; i < n; i++)
            {
                idsTensor[0, i] = input[i];
                maskTensor[0, i] = 1;
            }
            var encInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", idsTensor)
            };
            if (_encoder.InputMetadata.ContainsKey("attention_mask"))
                encInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor));

            Tensor<float> hidden;
            using (var encOut = _encoder.Run(encInputs))
            {
                hidden = encOut.First().AsTensor<float>();
            }

            // ---- decoder 贪心自回归 ----
            var decIds = new List<int> { _decoderStartId };
            for (int step = 0; step < MaxDecodeSteps; step++)
            {
                int cur = decIds.Count;
                var decTensor = new DenseTensor<long>(new[] { 1, cur });
                for (int i = 0; i < cur; i++) decTensor[0, i] = decIds[i];

                var decInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", decTensor),
                    NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden)
                };
                if (_decoder.InputMetadata.ContainsKey("encoder_attention_mask"))
                    decInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskTensor));
                else if (_decoder.InputMetadata.ContainsKey("attention_mask"))
                    decInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor));

                int next;
                using (var decOut = _decoder.Run(decInputs))
                {
                    var logits = (DenseTensor<float>)decOut.First().AsTensor<float>(); // [1, cur, vocab]
                    var span = logits.Buffer.Span;
                    int vocab = logits.Dimensions[2];
                    int baseIdx = (cur - 1) * vocab;
                    int best = 0;
                    float bestV = float.MinValue;
                    for (int k = 0; k < vocab && k < span.Length - baseIdx; k++)
                    {
                        float v = span[baseIdx + k];
                        if (v > bestV) { bestV = v; best = k; }
                    }
                    next = best;
                }

                if (next == _eosId || next >= _tgt.Count) break;
                decIds.Add(next);
                if (decIds.Count > 220) break; // 防失控
            }

            var result = _tgt.Decode(decIds.Skip(1));
            return result.Length == 0 ? null : result;
        }

        public void Dispose()
        {
            _encoder.Dispose();
            _decoder.Dispose();
        }
    }
}
