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
    ///
    /// v1.4 优化：
    ///  - 自适应轮询：画面连续无变化时轮询间隔按 1.6 倍指数拉长（上限 4s），
    ///    一有变化立刻回到基础间隔——静止画面下 CPU 占用进一步趋近于 0；
    ///  - 多显示器并行：每屏独立线程 + 独立 OCR 引擎（WinRT 引擎不保证并发安全）；
    ///  - 像素缓冲复用：同尺寸帧不再每轮新分配 8MB+ 数组。
    /// </summary>
    public sealed class ScreenPipeline : IDisposable
    {
        public sealed class MonitorCtx
        {
            internal Win32.RECT Rect;
            internal Win32.RECT WorkRect;   // 不含任务栏的工作区（过滤任务栏噪声）
            public uint Dpi;
            public byte[]? LastHash;
            public List<ScreenText> LastLines = new();
            internal byte[]? Buffer;
            internal OcrService? Peer;
        }

        private readonly OcrService _ocr;
        private readonly List<MonitorCtx> _monitors = new();
        private readonly List<Win32.RECT> _exclusions = new();   // 工具自身窗口区域（悬浮球/设置窗）
        private readonly object _gate = new();
        private readonly SemaphoreSlim _captureLock = new(1, 1); // 轮询与一次性捕获互斥
        private CancellationTokenSource? _cts;
        private Thread? _loop;

        /// <summary>最近一次扫描是否检测到画面变化（自适应轮询依据）。</summary>
        public bool LastScanChanged { get; private set; }

        /// <summary>更新"不翻译"窗口区域（悬浮球/面板/设置窗），防止翻译自己界面上的文字。</summary>
        internal void UpdateExclusions(IEnumerable<Win32.RECT> rects)
        {
            lock (_gate)
            {
                _exclusions.Clear();
                _exclusions.AddRange(rects);
                // 排除区域变化强制下一轮重新判定（清掉哈希缓存）
                foreach (var m in _monitors) m.LastHash = null;
            }
        }

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
                        _monitors.Add(new MonitorCtx { Rect = info.rcMonitor, WorkRect = info.rcWork, Dpi = dpi });
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
            int stableRounds = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var all = await CaptureOnceAsync();
                    LinesDetected?.Invoke(all);
                }
                catch { /* 单帧失败不影响下一轮 */ }

                int delay = intervalMs;
                if (!LastScanChanged)
                {
                    stableRounds++;
                    delay = (int)Math.Min(intervalMs * Math.Pow(1.6, stableRounds), 4000.0);
                }
                else stableRounds = 0;

                try { await Task.Delay(delay, ct); } catch { break; }
            }
        }

        /// <summary>扫描全部显示器一次（快捷键全屏翻译也走这里）。多屏并行。</summary>
        public async Task<List<ScreenText>> CaptureOnceAsync()
        {
            var merged = new List<ScreenText>();
            bool anyChanged = false;
            await _captureLock.WaitAsync();
            try
            {
                List<MonitorCtx> monitors;
                lock (_gate) monitors = _monitors.ToList();
                if (monitors.Count == 1)
                {
                    var (lines, changed) = await ScanMonitor(monitors[0]);
                    merged.AddRange(lines);
                    anyChanged = changed;
                }
                else
                {
                    var results = await Task.WhenAll(monitors.Select(m => Task.Run(() => ScanMonitor(m))));
                    foreach (var (lines, changed) in results)
                    {
                        merged.AddRange(lines);
                        anyChanged |= changed;
                    }
                }
            }
            finally { _captureLock.Release(); }
            LastScanChanged = anyChanged;
            return merged;
        }

        private async Task<(List<ScreenText> Lines, bool Changed)> ScanMonitor(MonitorCtx mon)
        {
            var r = mon.Rect;
            if (r.Width <= 0 || r.Height <= 0) return (new List<ScreenText>(), false);
            mon.Peer ??= _ocr.CreatePeer() ?? _ocr;

            var px = ScreenCapture.CaptureSmart(r.Left, r.Top, r.Width, r.Height,
                mon.Peer.MaxDimension, out int sw, out int sh, out double scale, mon.Buffer);
            mon.Buffer = px;

            var hash = QuickHash(px, sw, sh);
            if (mon.LastHash != null && hash.AsSpan().SequenceEqual(mon.LastHash))
            {
                // 画面没变：复用上次结果（同样代表“这些文本还在屏幕上”）
                return (mon.LastLines, false);
            }
            mon.LastHash = hash;

            var ocrLines = await mon.Peer.RecognizeAsync(px, sw, sh);
            var lines = new List<ScreenText>();
            double s = mon.Dpi / 96.0;
            foreach (var l in ocrLines)
            {
                // 位置级噪声过滤（物理像素，DPI 感知）：
                //  - 整行落在任务栏带内（工作区之外）→ 任务栏时间/图标/托盘噪声
                //  - 过小包围盒 → 图标碎片/残影
                double lx = l.X * scale, ly = l.Y * scale,
                       lw = l.Width * scale, lh = l.Height * scale;
                int absX = r.Left + (int)Math.Round(lx), absY = r.Top + (int)Math.Round(ly);
                if (ly + r.Top >= mon.WorkRect.Bottom - 2) continue;
                if (lw < 30 * s || lh < 12 * s) continue;

                // 与工具自身窗口相交 → 自己界面上的文字不翻
                bool excluded = false;
                lock (_gate)
                {
                    foreach (var ex in _exclusions)
                    {
                        if (absX < ex.Right + 8 && absX + lw > ex.Left - 8 &&
                            absY < ex.Bottom + 8 && absY + lh > ex.Top - 8)
                        {
                            excluded = true;
                            break;
                        }
                    }
                }
                if (excluded) continue;

                if (!TextFilter.ShouldTranslate(l.Text)) continue;
                lines.Add(new ScreenText(
                    l.Text,
                    absX, absY,
                    (int)Math.Round(lw),
                    (int)Math.Round(lh),
                    _monitors.IndexOf(mon)));
            }
            mon.LastLines = lines;
            return (lines, true);
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
