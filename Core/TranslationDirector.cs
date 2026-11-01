using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GlassTranslate.Core.LocalMt;
using GlassTranslate.Core.OcrEngine;

namespace GlassTranslate.Core
{
    /// <summary>
    /// UI 侧回调接口（全部可能在后台线程触发，实现方自行调度到 UI 线程）。
    /// </summary>
    public interface ITranslationSink
    {
        /// <summary>实时模式：当前屏幕非中文行快照（增/移/消失都由它表达）。</summary>
        void OnRealtimeSync(List<ScreenText> current);
        /// <summary>快捷键模式：一次性扫描结果。</summary>
        void OnOneShotResult(List<ScreenText> lines);
        void OnOneShotClear();
        /// <summary>某句译文的增量字符（本地引擎整句到达，仍经打字机队列逐字渲染）。</summary>
        void OnTranslationDelta(string normKey, string delta);
        /// <summary>某句翻译完成并已写缓存。</summary>
        void OnTranslationDone(string normKey);
        /// <summary>某句翻译失败。</summary>
        void OnTranslationFailed(string normKey);
        void OnError(string message);
    }

    /// <summary>
    /// 翻译调度器（v2.0 本地引擎版）：连接屏幕流水线、本地翻译引擎与句库缓存。
    ///  - 缓存命中的句子零推理、瞬发显示；
    ///  - 未命中的句子按 300ms 窗口聚合、最多 16 行/批 → 本地推理逐句回调；
    ///  - 失败的批次重排一次，避免死循环。
    /// </summary>
    public sealed class TranslationDirector : IDisposable
    {
        private const int MaxBatch = 16;

        private sealed class PendingLine
        {
            public string Key = "";
            public string Source = "";
            public bool FromRealtime;
            public DateTime Queued;
            public int Attempts;
        }

        private readonly ITranslationSink _sink;
        private readonly TranslationCache _cache;
        private readonly LocalMtEngine _mt;
        private readonly ScreenPipeline _pipeline;
        private readonly SemaphoreSlim _inflight = new(1, 1);
        private readonly object _gate = new();
        private readonly Dictionary<string, PendingLine> _pending = new();
        private readonly HashSet<string> _realtimeKeys = new();
        private readonly HashSet<string> _oneShotKeys = new();
        private readonly CancellationTokenSource _shutdown = new();
        private Timer? _flushTimer;
        private bool _realtimeRunning;

        /// <summary>上一帧的规范化行快照（防 OCR 闪烁变体重复翻译）。</summary>
        private sealed class FrameLine
        {
            public string Key = "";
            public string Text = "";
            public int X, Y, W, H, Monitor;
        }
        private List<FrameLine> _lastFrameLines = new();

        public bool RealtimeRunning => _realtimeRunning;
        public bool OneShotActive { get; private set; }
        public string OcrEngineName => _pipeline.OcrEngineName;

        public event Action? OneShotStateChanged;

        public TranslationDirector(ITranslationSink sink, TranslationCache cache,
            LocalMtEngine mt, IOcrEngine ocr)
        {
            _sink = sink;
            _cache = cache;
            _mt = mt;
            _pipeline = new ScreenPipeline(ocr);
            _pipeline.LinesDetected += OnLinesDetected;
        }

        // ---------------- 实时模式 ----------------

        public void StartRealtime(int intervalMs)
        {
            if (_realtimeRunning) return;
            _realtimeRunning = true;
            _pipeline.Start(intervalMs);
            _flushTimer = new Timer(_ => _ = FlushLoopAsync(), null, 200, 300);
        }

        public void StopRealtime()
        {
            if (!_realtimeRunning) return;
            _realtimeRunning = false;
            _pipeline.Stop();
            _flushTimer?.Dispose();
            _flushTimer = null;
            lock (_gate)
            {
                _realtimeKeys.Clear();
                RemovePending(p => p.FromRealtime && !_oneShotKeys.Contains(p.Key));
            }
            _sink.OnRealtimeSync(new List<ScreenText>());
        }

        private void OnLinesDetected(List<ScreenText> lines)
        {
            if (!_realtimeRunning) return;
            lines = Canonicalize(lines);
            lock (_gate)
            {
                var seen = new HashSet<string>();
                foreach (var l in lines)
                {
                    var k = TextFilter.NormalizeKey(l.Text);
                    if (!seen.Add(k)) continue;
                    if (_realtimeKeys.Add(k))
                    {
                        if (!_cache.Has(k) && !_pending.ContainsKey(k))
                            _pending[k] = new PendingLine
                            {
                                Key = k, Source = l.Text, FromRealtime = true, Queued = DateTime.UtcNow
                            };
                    }
                }
                var gone = _realtimeKeys.Where(k => !seen.Contains(k)).ToList();
                foreach (var g in gone) _realtimeKeys.Remove(g);
                RemovePending(p => p.FromRealtime && !_realtimeKeys.Contains(p.Key) && !_oneShotKeys.Contains(p.Key));
            }
            _sink.OnRealtimeSync(lines);
        }

        /// <summary>
        /// 归并 OCR 闪烁变体：OCR 对同一行常产出微小差异文本（多/少空格、l↔I、句尾标点），
        /// 精确键不同 → 会重复翻译并出现并排双泡。对"新面孔"行，若与上一帧某行
        /// 位置重叠(IoU≥0.5)且文本相似(≥0.72)，则映射回既有键——零额外翻译成本，
        /// 气泡也保持单一只随位置移动。
        /// </summary>
        private List<ScreenText> Canonicalize(List<ScreenText> lines)
        {
            var hasPrev = _lastFrameLines.Count > 0;
            var result = new List<ScreenText>(lines.Count);
            var newFrame = new List<FrameLine>(lines.Count);

            lock (_gate)
            {
                foreach (var l in lines)
                {
                    var k = TextFilter.NormalizeKey(l.Text);
                    var canonText = l.Text;
                    if (hasPrev &&
                        !_realtimeKeys.Contains(k) && !_oneShotKeys.Contains(k) &&
                        !_pending.ContainsKey(k) && !_cache.Has(k))
                    {
                        foreach (var fl in _lastFrameLines)
                        {
                            if (fl.Monitor != l.MonitorIndex) continue;
                            if (FuzzyText.RectIoU(fl.X, fl.Y, fl.W, fl.H,
                                l.X, l.Y, l.Width, l.Height) < 0.5) continue;
                            if (FuzzyText.Similarity(fl.Text, l.Text) >= 0.72)
                            {
                                canonText = fl.Text;   // 沿用上一帧的规范文本（其键已翻译/在册）
                                k = fl.Key;
                                break;
                            }
                        }
                    }
                    var lineOut = ReferenceEquals(canonText, l.Text) ? l : l with { Text = canonText };
                    result.Add(lineOut);
                    newFrame.Add(new FrameLine
                    {
                        Key = k, Text = canonText,
                        X = lineOut.X, Y = lineOut.Y, W = lineOut.Width, H = lineOut.Height,
                        Monitor = lineOut.MonitorIndex
                    });
                }
            }
            _lastFrameLines = newFrame;
            return result;
        }

        // ---------------- 快捷键一次性模式 ----------------

        public void ToggleOneShot()
        {
            if (OneShotActive)
            {
                ClearOneShot();
                return;
            }
            OneShotActive = true;
            OneShotStateChanged?.Invoke();
            try
            {
                var lines = _pipeline.CaptureOnceAsync().GetAwaiter().GetResult();
                if (!OneShotActive) return; // 期间被清除
                var dedup = new Dictionary<string, ScreenText>();
                foreach (var l in lines)
                {
                    var k = TextFilter.NormalizeKey(l.Text);
                    if (!dedup.ContainsKey(k)) dedup[k] = l;
                }
                lock (_gate)
                {
                    _oneShotKeys.Clear();
                    foreach (var k in dedup.Keys) _oneShotKeys.Add(k);
                    foreach (var kv in dedup)
                    {
                        if (_cache.Has(kv.Key) || _pending.ContainsKey(kv.Key)) continue;
                        _pending[kv.Key] = new PendingLine
                        {
                            Key = kv.Key, Source = kv.Value.Text, FromRealtime = false, Queued = DateTime.UtcNow
                        };
                    }
                }
                _sink.OnOneShotResult(dedup.Values.ToList());
                _ = FlushLoopAsync();
            }
            catch (Exception ex)
            {
                OneShotActive = false;
                OneShotStateChanged?.Invoke();
                _sink.OnError("全屏翻译失败：" + ex.Message);
            }
        }

        public void ClearOneShot()
        {
            OneShotActive = false;
            OneShotStateChanged?.Invoke();
            lock (_gate)
            {
                _oneShotKeys.Clear();
                RemovePending(p => !p.FromRealtime);
            }
            _sink.OnOneShotClear();
        }

        // ---------------- 批处理与本地推理分发 ----------------

        private async Task FlushLoopAsync()
        {
            if (!await _inflight.WaitAsync(0)) return;
            try
            {
                while (true)
                {
                    List<PendingLine> batch;
                    lock (_gate)
                    {
                        var valid = _pending.Values
                            .Where(p => p.FromRealtime ? _realtimeKeys.Contains(p.Key) || _oneShotKeys.Contains(p.Key)
                                                       : _oneShotKeys.Contains(p.Key))
                            .OrderBy(p => p.Queued)
                            .ToList();
                        RemovePending(p => !valid.Contains(p));
                        batch = valid.Take(MaxBatch).ToList();
                        foreach (var b in batch) _pending.Remove(b.Key);
                    }
                    if (batch.Count == 0) break;

                    UsageStats.AddApiCall();
                    var completed = new HashSet<string>();
                    try
                    {
                        var sources = batch.Select(b => b.Source).ToList();
                        await _mt.TranslateBatchAsync(sources, (idx, text) =>
                        {
                            if (idx < 0 || idx >= batch.Count) return;
                            var target = batch[idx];
                            // 后处理：标点规整/退化检测；可疑或无效译文按失败处理
                            var cleaned = TranslationPostProcess.Clean(text);
                            if (string.IsNullOrEmpty(cleaned)) return;
                            _sink.OnTranslationDelta(target.Key, cleaned);
                            _cache.Put(target.Key, cleaned);
                            completed.Add(target.Key);
                            UsageStats.AddLine();
                            _sink.OnTranslationDone(target.Key);
                        }, _shutdown.Token);
                        foreach (var b in batch)
                            if (!completed.Contains(b.Key))
                                _sink.OnTranslationFailed(b.Key);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        _sink.OnError("本地翻译失败：" + ex.Message);
                        lock (_gate)
                        {
                            foreach (var b in batch)
                            {
                                if (b.Attempts < 1 && !_pending.ContainsKey(b.Key))
                                {
                                    b.Attempts++;
                                    b.Queued = DateTime.UtcNow.AddSeconds(2);
                                    _pending[b.Key] = b;
                                }
                                else _sink.OnTranslationFailed(b.Key);
                            }
                        }
                        continue;
                    }
                }
            }
            finally { _inflight.Release(); }
        }

        /// <summary>更新"不翻译"窗口区域（透传给屏幕流水线；气泡矩形也走这里）。</summary>
        internal void UpdatePipelineExclusions(IEnumerable<Platform.Win32.RECT> rects) =>
            _pipeline.UpdateExclusions(rects);

        private void RemovePending(Predicate<PendingLine> match)
        {
            var keys = _pending.Where(kv => match(kv.Value)).Select(kv => kv.Key).ToList();
            foreach (var k in keys) _pending.Remove(k);
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            StopRealtime();
            ClearOneShot();
            _pipeline.Dispose();
            _flushTimer?.Dispose();
        }
    }
}
