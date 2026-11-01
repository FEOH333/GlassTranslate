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
                            if (string.IsNullOrEmpty(text)) return;
                            _sink.OnTranslationDelta(target.Key, text);
                            _cache.Put(target.Key, text);
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
