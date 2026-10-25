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
    /// 屏幕流水线（v1.5 响应性架构）：
    ///
    ///  文出即出 —— 不再定时轮询。150ms 低成本运动探针（~320px 低分辨率截图 + 哈希，
    ///              不含层叠窗口，自己的译文气泡不会触发自循环）；画面一变立即触发全量扫描，
    ///              静止时探针间隔指数放缓到 800ms（CPU 仍趋近 0）。
    ///  文走即走 —— 变化区域内的旧行立即移除对应气泡。
    ///  快速跟随 —— 分格差异 OCR：屏幕划成 4×4 格，仅识别变化格的联合区域，
    ///              滚动时只重识别滚动带，未变区域的行与气泡原地保留不闪烁。
    /// </summary>
    public sealed class ScreenPipeline : IDisposable
    {
        public sealed class MonitorCtx
        {
            internal Win32.RECT Rect;
            internal Win32.RECT WorkRect;       // 不含任务栏的工作区（过滤任务栏噪声）
            public uint Dpi;
            public List<ScreenText> LastLines = new();
            internal byte[]? TinyBuffer;        // 运动探针低清帧
            internal byte[]? LastTinyHash;
            internal byte[]? Buffer;            // 全分辨率 OCR 帧（复用）
            internal byte[][]? CellHashes;      // 4×4 分格哈希
            internal bool FullScanNext = true;  // 首扫/大变化时整帧识别
            internal OcrService? Peer;
        }

        private const int ProbeBaseMs = 150;      // 运动探针基础间隔
        private const int ProbeMaxMs = 800;       // 静止时的上限
        private const int ScanMinGapMs = 320;     // 连续变化（滚动/视频）下的最小扫描间隔
        private const int Cols = 4, Rows = 4;

        private readonly OcrService _ocr;
        private readonly List<MonitorCtx> _monitors = new();
        private readonly List<Win32.RECT> _exclusions = new();
        private readonly object _gate = new();
        private readonly SemaphoreSlim _captureLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private Thread? _loop;
        private DateTime _lastScanAt = DateTime.MinValue;
        private volatile bool _pendingScan;

        /// <summary>最近一次扫描是否检测到画面变化。</summary>
        public bool LastScanChanged { get; private set; }

        /// <summary>每次扫描完成触发（后台线程）。</summary>
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
                        _monitors.Add(new MonitorCtx
                        {
                            Rect = info.rcMonitor,
                            WorkRect = info.rcWork,
                            Dpi = dpi
                        });
                    }
                    return true;
                }, IntPtr.Zero);
                _monitors.Sort((a, b) => a.Rect.Left != b.Rect.Left
                    ? a.Rect.Left.CompareTo(b.Rect.Left)
                    : a.Rect.Top.CompareTo(b.Rect.Top));
            }
        }

        /// <summary>更新"不翻译"窗口区域（悬浮球/面板/设置窗），防止翻译自己界面上的文字。</summary>
        internal void UpdateExclusions(IEnumerable<Win32.RECT> rects)
        {
            lock (_gate)
            {
                _exclusions.Clear();
                _exclusions.AddRange(rects);
                foreach (var m in _monitors) { m.LastTinyHash = null; m.FullScanNext = true; }
            }
        }

        public void Start(int _)
        {
            Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = new Thread(() => RunLoop(token))
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

        // ---------------- 运动探针主循环 ----------------

        private async void RunLoop(CancellationToken ct)
        {
            int stable = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool changed = ProbeChanged();
                    bool due = (DateTime.UtcNow - _lastScanAt).TotalMilliseconds >= ScanMinGapMs;
                    if (changed && due)
                    {
                        var all = await CaptureOnceAsync();
                        LinesDetected?.Invoke(all);
                        stable = 0;
                    }
                    else if (changed)
                    {
                        _pendingScan = true;   // 变化太频繁（滚动中），节流后补扫
                    }
                    else if (_pendingScan && due)
                    {
                        _pendingScan = false;
                        var all = await CaptureOnceAsync();
                        LinesDetected?.Invoke(all);
                        stable = 0;
                    }
                    else stable++;
                }
                catch { /* 单帧失败不影响下一轮 */ }

                int delay = stable == 0
                    ? ProbeBaseMs
                    : (int)Math.Min(ProbeBaseMs * Math.Pow(1.4, Math.Min(stable, 12)), ProbeMaxMs);
                try { await Task.Delay(delay, ct); } catch { break; }
            }
        }

        /// <summary>低清运动探针：不含层叠窗口（自己的气泡不会自触发），约 2-4ms/屏。</summary>
        private bool ProbeChanged()
        {
            List<MonitorCtx> monitors;
            lock (_gate) monitors = _monitors.ToList();
            bool changed = false;
            foreach (var mon in monitors)
            {
                var r = mon.Rect;
                if (r.Width <= 0 || r.Height <= 0) continue;
                try
                {
                    var px = ScreenCapture.CaptureTiny(r.Left, r.Top, r.Width, r.Height,
                        mon.TinyBuffer, out int tw, out int th);
                    mon.TinyBuffer = px;
                    var hash = QuickHash(px, tw, th);
                    if (mon.LastTinyHash == null || !hash.AsSpan().SequenceEqual(mon.LastTinyHash))
                    {
                        mon.LastTinyHash = hash;
                        changed = true;
                    }
                }
                catch { changed = true; }
            }
            return changed;
        }

        // ---------------- 全量扫描（含分格差异 OCR） ----------------

        /// <summary>扫描全部显示器一次（快捷键全屏翻译也走这里）。多屏并行。</summary>
        public async Task<List<ScreenText>> CaptureOnceAsync()
        {
            var merged = new List<ScreenText>();
            bool anyChanged = false;
            await _captureLock.WaitAsync();
            try
            {
                _lastScanAt = DateTime.UtcNow;
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

            // 分格哈希：先与旧值比较得出变化格范围，再替换
            var cells = CellHashes(px, sw, sh);
            bool fullScan = mon.FullScanNext || mon.CellHashes == null;
            int changedCells = 0, c0 = Cols, c1 = -1, r0 = Rows, r1 = -1;
            if (mon.CellHashes != null)
            {
                for (int row = 0; row < Rows; row++)
                    for (int col = 0; col < Cols; col++)
                    {
                        int idx = row * Cols + col;
                        if (cells[idx].AsSpan().SequenceEqual(mon.CellHashes[idx])) continue;
                        changedCells++;
                        c0 = Math.Min(c0, col); c1 = Math.Max(c1, col);
                        r0 = Math.Min(r0, row); r1 = Math.Max(r1, row);
                    }
            }
            if (changedCells >= Rows * Cols - 2) fullScan = true;   // 几乎全变（切页/换窗）→ 整帧
            mon.CellHashes = cells;
            mon.FullScanNext = false;

            // 识别区域：整帧 或 变化格联合区（外扩一格）
            int rx = 0, ry = 0, rw = sw, rh = sh;
            if (!fullScan && changedCells > 0)
            {
                c0 = Math.Max(0, c0 - 1); r0 = Math.Max(0, r0 - 1);
                c1 = Math.Min(Cols - 1, c1 + 1); r1 = Math.Min(Rows - 1, r1 + 1);
                rx = c0 * sw / Cols; ry = r0 * sh / Rows;
                rw = Math.Min(sw - rx, (c1 + 1) * sw / Cols - rx);
                rh = Math.Min(sh - ry, (r1 + 1) * sh / Rows - ry);
                if (rw < 40 || rh < 24) return (mon.LastLines, false);
            }
            else if (!fullScan && changedCells == 0)
            {
                return (mon.LastLines, false);
            }

            // 裁剪识别区
            byte[] crop;
            bool cropped = rx > 0 || ry > 0 || rw != sw || rh != sh;
            if (cropped)
            {
                crop = new byte[rw * rh * 4];
                for (int y = 0; y < rh; y++)
                    Array.Copy(px, ((ry + y) * sw + rx) * 4, crop, y * rw * 4, rw * 4);
            }
            else crop = px;

            // 深色主题反色：Windows OCR 对"浅字深底"的识别明显弱于"深字浅底"，
            // 暗色编辑器/网页先反色再识别（包围盒不受影响，缓冲下轮覆盖）
            InvertIfDark(crop);

            var ocrLines = await mon.Peer.RecognizeAsync(crop, cropped ? rw : sw, cropped ? rh : sh);

            // 密集画面判定：原始行很多、或代码/日志占比高（代码编辑器、终端、IDE）
            // → 严格过滤：只翻"句子样"文本，单词/片段一律跳过，杜绝满屏错乱气泡
            int rawCount = ocrLines.Count;
            int codeish = 0;
            foreach (var l in ocrLines) if (TextFilter.LooksLikeCode(l.Text)) codeish++;
            bool dense = rawCount > 55 || (rawCount >= 8 && codeish * 100 >= rawCount * 35);

            var s = mon.Dpi / 96.0;
            var newLines = new List<ScreenText>();
            foreach (var l in ocrLines)
            {
                if (newLines.Count >= 32) break;   // 单屏上限：再密集也不刷屏
                double lx = l.X * scale + (cropped ? rx * scale : 0),
                       ly = l.Y * scale + (cropped ? ry * scale : 0),
                       lw = l.Width * scale, lh = l.Height * scale;
                int absX = r.Left + (int)Math.Round(lx), absY = r.Top + (int)Math.Round(ly);
                if (ly + r.Top >= mon.WorkRect.Bottom - 2) continue;
                if (lw < 30 * s || lh < 12 * s) continue;
                if (InExclusion(absX, absY, lw, lh)) continue;
                if (!TextFilter.ShouldTranslate(l.Text, dense)) continue;
                newLines.Add(new ScreenText(l.Text, absX, absY,
                    (int)Math.Round(lw), (int)Math.Round(lh), _monitors.IndexOf(mon)));
            }

            // 合并：变化区外的旧行保留（气泡不闪烁），区内以新结果为准
            List<ScreenText> mergedLines;
            if (cropped)
            {
                double px0 = r.Left + rx * scale, py0 = r.Top + ry * scale,
                       px1 = r.Left + (rx + rw) * scale, py1 = r.Top + (ry + rh) * scale;
                mergedLines = mon.LastLines
                    .Where(o => o.X + o.Width < px0 - 4 || o.X > px1 + 4 ||
                                o.Y + o.Height < py0 - 4 || o.Y > py1 + 4)
                    .Concat(newLines)
                    .OrderBy(l => l.Y).ThenBy(l => l.X)
                    .ToList();
            }
            else mergedLines = newLines;

            bool contentChanged = mergedLines.Count != mon.LastLines.Count ||
                !mergedLines.Select(l => (l.Text, l.X, l.Y))
                    .SequenceEqual(mon.LastLines.Select(l => (l.Text, l.X, l.Y)));
            mon.LastLines = mergedLines;
            return (mergedLines, contentChanged);
        }

        /// <summary>深色帧反色：稀疏采样平均亮度 &lt; 90 时反转 RGB。</summary>
        private static void InvertIfDark(byte[] px)
        {
            // 先探测（采样步长按面积自适应，最多约 4000 个样本）
            int step = Math.Max(1, (int)Math.Sqrt(px.Length / 4 / 4000.0));
            long sum = 0; int n = 0;
            for (int i = 0; i + 2 < px.Length; i += step * 4)
            {
                sum += (px[i] + px[i + 1] + px[i + 2]) / 3;
                n++;
            }
            if (n == 0 || sum / n >= 90) return;
            for (int i = 0; i + 2 < px.Length; i += 4)
            {
                px[i] = (byte)(255 - px[i]);
                px[i + 1] = (byte)(255 - px[i + 1]);
                px[i + 2] = (byte)(255 - px[i + 2]);
            }
        }

        private bool InExclusion(double x, double y, double w, double h)
        {
            lock (_gate)
            {
                foreach (var ex in _exclusions)
                {
                    if (x < ex.Right + 8 && x + w > ex.Left - 8 &&
                        y < ex.Bottom + 8 && y + h > ex.Top - 8)
                        return true;
                }
            }
            return false;
        }

        private static byte[][] CellHashes(byte[] px, int w, int h)
        {
            var result = new byte[Rows * Cols][];
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    int x0 = col * w / Cols, x1 = (col + 1) * w / Cols;
                    int y0 = row * h / Rows, y1 = (row + 1) * h / Rows;
                    int stride = w * 4;
                    var vals = new byte[(Math.Max(1, (x1 - x0) / 13 + 1)) * (Math.Max(1, (y1 - y0) / 7 + 1)) + 2];
                    int n = 0;
                    for (int y = y0; y < y1 && n < vals.Length - 2; y += 7)
                    {
                        int baseIdx = y * stride;
                        for (int x = x0; x < x1 && n < vals.Length - 2; x += 13)
                        {
                            int o = baseIdx + x * 4;
                            vals[n++] = (byte)(px[o] ^ px[o + 1] ^ px[o + 2]);
                        }
                    }
                    vals[^2] = (byte)(x1 - x0);
                    vals[^1] = (byte)(y1 - y0);
                    result[row * Cols + col] = SHA256.HashData(vals);
                }
            }
            return result;
        }

        /// <summary>稀疏采样哈希（运动探针用）。</summary>
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
