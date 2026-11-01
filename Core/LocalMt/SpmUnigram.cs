using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GlassTranslate.Core.LocalMt
{
    /// <summary>
    /// 极简 Unigram SentencePiece 编解码器（自研，零依赖）：
    /// 直接解析 .spm 的 protobuf（pieces+scores），Viterbi 最大似然分词编码，
    /// 逐片还原解码。覆盖 Marian/Opus-MT 系模型所需全部能力。
    /// 跳过 precompiled charsmap 规范化——对英文源文本无实际影响。
    /// </summary>
    public sealed class SpmUnigram
    {
        public sealed class Piece
        {
            public string Text = "";
            public float Score;
            public int Type; // 1=NORMAL 2=UNKNOWN 3=CONTROL 4=USER_DEFINED 5=UNUSED 6=BYTE
        }

        private readonly Dictionary<string, int> _byText = new();
        public readonly List<Piece> Pieces = new();
        public int UnkId = 0;
        public int MaxPieceChars = 1;

        public int Count => Pieces.Count;

        public static SpmUnigram Load(string path) => Parse(File.ReadAllBytes(path));

        /// <summary>
        /// 从 HuggingFace tokenizer.json（Unigram）加载：vocab 数组下标即 id。
        /// 与 Xenova ONNX 导出配套使用，保证分词器与模型词表严格一致。
        /// </summary>
        public static SpmUnigram LoadTokenizerJson(string path)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var spm = new SpmUnigram();
            var model = root.GetProperty("model");
            foreach (var entry in model.GetProperty("vocab").EnumerateArray())
            {
                var piece = new Piece
                {
                    Text = entry[0].GetString() ?? "",
                    Score = (float)entry[1].GetDouble(),
                    Type = 1
                };
                if (piece.Text is "</s>" or "<s>" or "<pad>" or "<unk>") piece.Type = 3;
                spm.AddPiece(piece);
            }
            foreach (var piece in spm.Pieces)
                if (piece.Text.Length > spm.MaxPieceChars) spm.MaxPieceChars = piece.Text.Length;
            return spm;
        }

        private static SpmUnigram Parse(byte[] proto)
        {
            var spm = new SpmUnigram();
            int pos = 0;
            while (pos < proto.Length)
            {
                var (key, p) = ReadVarint(proto, pos);
                int field = (int)(key >> 3), wire = (int)(key & 7);
                if (field == 1 && wire == 2) // repeated SentencePiece
                {
                    var (len, p2) = ReadVarint(proto, p);
                    spm.AddPiece(ParsePiece(proto, p2, p2 + (int)len));
                    pos = p2 + (int)len;
                }
                else if (wire == 2) // trainer_spec / normalizer_spec：跳过
                {
                    var (len, p2) = ReadVarint(proto, p);
                    pos = p2 + (int)len;
                }
                else if (wire == 0) { var (_, p2) = ReadVarint(proto, p); pos = p2; }
                else if (wire == 5) pos = p + 4;
                else if (wire == 1) pos = p + 8;
                else throw new InvalidDataException($"spm proto: unexpected wire {wire}");
            }
            foreach (var piece in spm.Pieces)
                if (piece.Text.Length > spm.MaxPieceChars) spm.MaxPieceChars = piece.Text.Length;
            return spm;
        }

        private static Piece ParsePiece(byte[] b, int pos, int end)
        {
            var piece = new Piece();
            while (pos < end)
            {
                var (key, p) = ReadVarint(b, pos);
                int field = (int)(key >> 3), wire = (int)(key & 7);
                if (field == 1 && wire == 2)
                {
                    var (len, p2) = ReadVarint(b, p);
                    piece.Text = Encoding.UTF8.GetString(b, p2, (int)len);
                    pos = p2 + (int)len;
                }
                else if (field == 2 && wire == 5)
                {
                    piece.Score = BitConverter.ToSingle(b, p);
                    pos = p + 4;
                }
                else if (field == 3 && wire == 0)
                {
                    var (v, p2) = ReadVarint(b, p);
                    piece.Type = (int)v;
                    pos = p2;
                }
                else if (wire == 2) { var (len, p2) = ReadVarint(b, p); pos = p2 + (int)len; }
                else if (wire == 0) { var (_, p2) = ReadVarint(b, p); pos = p2; }
                else if (wire == 5) pos = p + 4;
                else if (wire == 1) pos = p + 8;
                else break;
            }
            return piece;
        }

        private void AddPiece(Piece p)
        {
            _byText[p.Text] = Pieces.Count;
            if (p.Type == 2) UnkId = Pieces.Count;
            Pieces.Add(p);
        }

        private static (long value, int next) ReadVarint(byte[] b, int pos)
        {
            long v = 0; int shift = 0;
            while (pos < b.Length)
            {
                byte x = b[pos++];
                v |= (long)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) break;
                shift += 7;
            }
            return (v, pos);
        }

        /// <summary>Viterbi 最大似然分词；返回 id 序列（不含任何 BOS/EOS）。</summary>
        public List<int> Encode(string text)
        {
            // SentencePiece 惯例：空格 → "▁"，词首加 "▁"
            var s = "▁" + text.Trim().Replace(" ", "▁");
            int n = s.Length;
            var best = new double[n + 1];
            var back = new int[n + 1];   // 指向起点
            var backId = new int[n + 1];
            for (int i = 1; i <= n; i++) best[i] = double.NegativeInfinity;
            var sb = new StringBuilder();
            for (int end = 1; end <= n; end++)
            {
                int startMin = Math.Max(0, end - Math.Min(MaxPieceChars, end));
                for (int start = end - 1; start >= startMin; start--)
                {
                    sb.Clear();
                    for (int k = start; k < end; k++) sb.Append(s[k]);
                    if (_byText.TryGetValue(sb.ToString(), out var id))
                    {
                        var score = best[start] + Pieces[id].Score;
                        if (score > best[end])
                        {
                            best[end] = score;
                            back[end] = start;
                            backId[end] = id;
                        }
                    }
                }
                if (double.IsNegativeInfinity(best[end]))
                {
                    // 无片段覆盖：退化为单字符 + <unk>
                    int start = end - 1;
                    if (!char.IsHighSurrogate(s[start]) || start == 0)
                    {
                        var score = best[start] - 10.0;
                        if (score > best[end])
                        {
                            best[end] = score;
                            back[end] = start;
                            backId[end] = UnkId;
                        }
                    }
                    else
                    {
                        // 高位代理：与前一 char 合并为一个码点走 unk
                        int start2 = end - 2;
                        if (start2 >= 0)
                        {
                            var score = best[start2] - 10.0;
                            if (score > best[end])
                            {
                                best[end] = score;
                                back[end] = start2;
                                backId[end] = UnkId;
                            }
                        }
                    }
                }
            }
            var ids = new List<int>();
            for (int pos = n; pos > 0; pos = back[pos])
                ids.Add(backId[pos]);
            ids.Reverse();
            return ids;
        }

        /// <summary>id 序列还原为文本（跳过控制/未知/BOS/EOS 片段）。</summary>
        public string Decode(IEnumerable<int> ids)
        {
            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                if (id < 0 || id >= Pieces.Count) continue;
                var p = Pieces[id];
                if (p.Type is 2 or 3 or 5) continue;      // unk/control/unused
                if (p.Text is "<s>" or "</s>" or "<pad>" or "<unk>") continue;
                sb.Append(p.Text);
            }
            return sb.ToString().Replace("▁", " ").Trim();
        }

        public int? FindId(string piece) => _byText.TryGetValue(piece, out var id) ? id : null;
    }
}
