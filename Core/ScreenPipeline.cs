using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GlassTranslate.Platform;

namespace GlassTranslate.Core
{
    /// <summary>一条可翻译的屏幕文本（坐标为虚拟屏幕物理像素）。</summary>
    public sealed record ScreenText(string Text, int X, int Y, int Width, int Height, int MonitorIndex);

    /// <summary>
    /// 屏幕流水线：轮询 捕获 → 变化检测 → OCR → 过滤，输出“当前屏幕上的非中文行”快照。
    /// 关键省电点：画面没变（对采样像素做 SHA256）就完全跳过 OCR，静止画面下 CPU 占用≈0。
    /// </summary>
    public sealed class ScreenPipeline : IDisposable
    {
        public sealed class MonitorCtx
        {
            internal Win32.RECT Rect;
            public uint Dpi;
            public byte[]? LastHash;
            public List<ScreenText> LastLines = new();
        }

        private readonly OcrService _ocr;
        private readonly List<MonitorCtx> _monitors = new();
        private readonly object _gate = new();
        private readonly SemaphoreSlim _captureLock = new(1, 1); // 轮询与一次性捕获互斥
        private CancellationTokenSource? _cts;
        private Thread? _loop;

        /// <summary>实时模式下每次扫描完成触发（后台线程）。</summary>
        public event Action<List<ScreenText>>? LinesDetected;

        public ScreenPipeline(OcrService ocr)
        {
            _ocr = ocr;
            RefreshMonitors();
        }

        public void RefreshMonitors()
        {
            lock (_gate)
            {
                _monitors.Clear();
                Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
                {
                    var info = Win32.MONITORINFO.Create();
                    if (Win32.GetMonitorInfo(hmon, ref info))
                    {
                        uint dpi = 96;
                        try { Win32.GetDpiForMonitor(hmon, 0, out dpi, out _); } catch { }
                        _monitors.Add(new MonitorCtx { Rect = info.rcMonitor, Dpi = dpi });
                    }
                    return true;
                }, IntPtr.Zero);
                // 与 OverlayController 使用同一排序，保证 MonitorIndex 一致
                _monitors.Sort((a, b) => a.Rect.Left != b.Rect.Left
                    ? a.Rect.Left.CompareTo(b.Rect.Left)
                    : a.Rect.Top.CompareTo(b.Rect.Top));
            }
        }

        public void Start(int intervalMs)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = new Thread(() => RunLoop(intervalMs, token))
            {
                IsBackground = true,
                Name = "GlassTranslate.ScreenPipeline"
            };
            _loop.Start();
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _loop = null;
        }

        private async void RunLoop(int intervalMs, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var all = await CaptureOnceAsync();
                    LinesDetected?.Invoke(all);
                }
                catch { /* 单帧失败不影响下一轮 */ }
                try { await Task.Delay(intervalMs, ct); } catch { break; }
            }
        }

        /// <summary>扫描全部显示器一次（快捷键全屏翻译也走这里）。</summary>
        public async Task<List<ScreenText>> CaptureOnceAsync()
        {
            var merged = new List<ScreenText>();
            await _captureLock.WaitAsync();
            try
            {
                List<MonitorCtx> monitors;
                lock (_gate) monitors = _monitors.ToList();
                for (int idx = 0; idx < monitors.Count; idx++)
                {
                    var mon = monitors[idx];
                    var r = mon.Rect;
                    if (r.Width <= 0 || r.Height <= 0) continue;

                    var px = ScreenCapture.CaptureSmart(r.Left, r.Top, r.Width, r.Height,
                        _ocr.MaxDimension, out int sw, out int sh, out double scale);
                    var hash = QuickHash(px, sw, sh);

                    if (mon.LastHash != null && hash.AsSpan().SequenceEqual(mon.LastHash))
                    {
                        // 画面没变：复用上次结果（同样代表“这些文本还在屏幕上”）
                        merged.AddRange(mon.LastLines);
                        continue;
                    }
                    mon.LastHash = hash;

                    var ocrLines = await _ocr.RecognizeAsync(px, sw, sh);
                    var lines = new List<ScreenText>();
                    foreach (var l in ocrLines)
                    {
                        if (!TextFilter.ShouldTranslate(l.Text)) continue;
                        lines.Add(new ScreenText(
                            l.Text,
                            r.Left + (int)Math.Round(l.X * scale),
                            r.Top + (int)Math.Round(l.Y * scale),
                            (int)Math.Round(l.Width * scale),
                            (int)Math.Round(l.Height * scale),
                            idx));
                    }
                    mon.LastLines = lines;
                    merged.AddRange(lines);
                }
            }
            finally { _captureLock.Release(); }
            return merged;
        }

        /// <summary>稀疏采样哈希：画面可见变化才重新 OCR。</summary>
        private static byte[] QuickHash(byte[] px, int w, int h)
        {
            int stride = w * 4;
            var vals = new byte[(w / 11 + 1) * (h / 7 + 1) + 2];
            int n = 0;
            for (int y = 0; y < h; y += 7)
            {
                int row = y * stride;
                for (int x = 0; x < w && n < vals.Length - 2; x += 11)
                {
                    int o = row + x * 4;
                    vals[n++] = (byte)(px[o] ^ px[o + 1] ^ px[o + 2]);
                }
            }
            vals[^2] = (byte)(w & 0xFF);
            vals[^1] = (byte)(h & 0xFF);
            return SHA256.HashData(vals);
        }

        public void Dispose() => Stop();
    }
}
