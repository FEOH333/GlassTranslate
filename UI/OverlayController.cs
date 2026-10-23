using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using GlassTranslate.Core;
using GlassTranslate.Platform;

namespace GlassTranslate.UI
{
    /// <summary>
    /// 译文悬浮层控制器：管理每个显示器的 OverlayWindow 与气泡生命周期，
    /// 实现 ITranslationSink 接收调度器回调（全部转回 UI 线程）。
    ///
    /// 摆位策略（“语句旁的空白处，不遮挡其他内容”）：
    ///   下方 → 上方 → 右侧 → 最后兜底覆盖原文行本身；
    ///   候选位置与同屏其他 OCR 行、已放置气泡求交，全部冲突才降级。
    /// </summary>
    internal sealed class OverlayController : ITranslationSink
    {
        private const int MaxTooltips = 80;

        private readonly TranslationCache _cache;
        private readonly Dispatcher _ui = Application.Current.Dispatcher;
        private readonly Dictionary<string, TooltipVm> _realtime = new();
        private readonly Dictionary<string, TooltipVm> _oneShot = new();
        private readonly List<OverlayWindow?> _windows = new();
        private readonly List<(Win32.RECT Rect, uint Dpi)> _monitors = new();
        private DateTime _lastErrorAt = DateTime.MinValue;
        private DateTime _lastClearAt = DateTime.MinValue;

        public OverlayController(TranslationCache cache)
        {
            _cache = cache;
            RefreshMonitors();
        }

        private void RefreshMonitors()
        {
            _monitors.Clear();
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
            {
                var mi = Win32.MONITORINFO.Create();
                if (Win32.GetMonitorInfo(hmon, ref mi))
                {
                    uint dpi = 96;
                    try { Win32.GetDpiForMonitor(hmon, 0, out dpi, out _); } catch { }
                    _monitors.Add((mi.rcMonitor, dpi));
                }
                return true;
            }, IntPtr.Zero);
            _monitors.Sort((a, b) => a.Rect.Left != b.Rect.Left
                ? a.Rect.Left.CompareTo(b.Rect.Left)
                : a.Rect.Top.CompareTo(b.Rect.Top));
        }

        // ---------------- ITranslationSink ----------------

        public void OnRealtimeSync(List<ScreenText> current) =>
            _ui.BeginInvoke(() => SyncRealtime(current));

        public void OnOneShotResult(List<ScreenText> lines) =>
            _ui.BeginInvoke(() =>
            {
                ClearDict(_oneShot);
                foreach (var group in GroupByMonitor(lines))
                    ShowGroup(group, _oneShot);
            });

        public void OnOneShotClear() => _ui.BeginInvoke(() =>
        {
            // 快速连按时避免闪烁抑制
            _lastClearAt = DateTime.UtcNow;
            ClearDict(_oneShot);
        });

        public void OnTranslationDelta(string normKey, string delta) =>
            _ui.BeginInvoke(() =>
            {
                var vm = _realtime.GetValueOrDefault(normKey) ?? _oneShot.GetValueOrDefault(normKey);
                vm?.AppendDelta(delta);
            });

        public void OnTranslationDone(string normKey) { /* 缓存已写，气泡自然打完 */ }

        public void OnTranslationFailed(string normKey) =>
            _ui.BeginInvoke(() =>
            {
                var vm = _realtime.GetValueOrDefault(normKey) ?? _oneShot.GetValueOrDefault(normKey);
                vm?.SetFailed();
            });

        public void OnError(string message)
        {
            if ((DateTime.UtcNow - _lastErrorAt).TotalSeconds < 4) return;
            _lastErrorAt = DateTime.UtcNow;
            Toast.Show(message);
        }

        // ---------------- 实时同步 ----------------

        private void SyncRealtime(List<ScreenText> current)
        {
            var keys = new HashSet<string>();
            foreach (var l in current) keys.Add(TextFilter.NormalizeKey(l.Text));

            foreach (var gone in _realtime.Where(kv => !keys.Contains(kv.Key)).ToList())
                RemoveVm(_realtime, gone.Key, gone.Value);

            var grouped = GroupByMonitor(current);
            foreach (var group in grouped)
            {
                foreach (var l in group.Lines)
                {
                    var key = TextFilter.NormalizeKey(l.Text);
                    if (_oneShot.ContainsKey(key)) continue;
                    if (_realtime.TryGetValue(key, out var vm))
                    {
                        if (Math.Abs(vm.Source.X - l.X) + Math.Abs(vm.Source.Y - l.Y) > 12)
                            Reposition(vm, l, current);
                        vm.Source = l;
                    }
                    else if (_realtime.Count < MaxTooltips)
                    {
                        var cached = _cache.Get(key);
                        var nv = new TooltipVm(key, l, cached);
                        _realtime[key] = nv;
                        AddToWindow(nv, current);
                    }
                }
            }
        }

        private void ShowGroup((int Index, List<ScreenText> Lines) group, Dictionary<string, TooltipVm> dict)
        {
            foreach (var l in group.Lines)
            {
                var key = TextFilter.NormalizeKey(l.Text);
                if (dict.ContainsKey(key)) continue;
                if (dict.Count >= MaxTooltips) break;
                var cached = _cache.Get(key);
                var vm = new TooltipVm(key, l, cached);
                dict[key] = vm;
                AddToWindow(vm, group.Lines);
            }
        }

        private IEnumerable<(int Index, List<ScreenText> Lines)> GroupByMonitor(List<ScreenText> lines)
        {
            return lines
                .GroupBy(l => Math.Clamp(l.MonitorIndex, 0, Math.Max(0, _monitors.Count - 1)))
                .Select(g => (g.Key, g.ToList()));
        }

        // ---------------- 气泡增删/摆位 ----------------

        private OverlayWindow GetWindow(int monitorIndex)
        {
            if (_monitors.Count == 0) RefreshMonitors();
            monitorIndex = Math.Clamp(monitorIndex, 0, _monitors.Count - 1);
            for (int i = _windows.Count; i <= monitorIndex; i++) _windows.Add(null);
            if (_windows[monitorIndex] == null)
            {
                var (rect, _) = _monitors[monitorIndex];
                var w = new OverlayWindow(rect, monitorIndex);
                w.Show();
                _windows[monitorIndex] = w;
                OnOverlayCreated?.Invoke();
            }
            return _windows[monitorIndex]!;
        }

        /// <summary>新悬浮层盖到悬浮球之上时，让悬浮球重新置顶（AppController 订阅）。</summary>
        public event Action? OnOverlayCreated;

        private void AddToWindow(TooltipVm vm, List<ScreenText> frameLines)
        {
            var w = GetWindow(vm.Source.MonitorIndex);
            var (px, py) = Place(vm, vm.Source, frameLines, w);
            vm.X = w.ToDip(px) ;
            vm.Y = w.ToDip(py);
            w.Add(vm);
        }

        private void Reposition(TooltipVm vm, ScreenText line, List<ScreenText> frameLines)
        {
            var w = GetWindow(line.MonitorIndex);
            var (px, py) = Place(vm, line, frameLines, w);
            vm.X = w.ToDip(px);
            vm.Y = w.ToDip(py);
        }

        private void RemoveVm(Dictionary<string, TooltipVm> dict, string key, TooltipVm vm)
        {
            dict.Remove(key);
            vm.Dying = true;
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(240) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                foreach (var w in _windows)
                    if (w != null && w.Count > 0) w.Remove(vm);
            };
            t.Start();
        }

        private void ClearDict(Dictionary<string, TooltipVm> dict)
        {
            foreach (var kv in dict.ToList()) RemoveVm(dict, kv.Key, kv.Value);
        }

        /// <summary>核心摆位：返回相对该窗口左上角的物理像素坐标。</summary>
        private (int px, int py) Place(TooltipVm vm, ScreenText line, List<ScreenText> frameLines, OverlayWindow w)
        {
            var mon = w.PixelRect;
            var dip = w.Dpi / 96.0;
            // 预估气泡尺寸（物理像素）
            double estW = Math.Min(340, 52 + vm.TextLengthEstimate() * 13.0) * dip;
            double estH = (30 + (estW > 336 * dip ? vm.TextLengthEstimate() * 13.0 / (326 * dip) * 20 : 0)) * dip;

            var blocked = new List<(double X, double Y, double W, double H)>();
            foreach (var l in frameLines)
            {
                if (l.MonitorIndex != w.MonitorIndex) continue;
                if (l.X == line.X && l.Y == line.Y && l.Text == line.Text) continue; // 自己那行不算
                blocked.Add((l.X, l.Y, l.Width, l.Height));
            }
            foreach (var d in new[] { _realtime, _oneShot })
                foreach (var other in d.Values)
                    if (other != vm && other.Source.MonitorIndex == w.MonitorIndex)
                        blocked.Add(other.Placed);

            double Gap = 6 * dip;
            var candidates = new (double X, double Y)[]
            {
                (line.X, line.Y + line.Height + Gap),          // 下方
                (line.X, line.Y - estH - Gap),                 // 上方
                (line.X + line.Width + Gap, line.Y),           // 右侧
                (line.X, line.Y + 2)                           // 兜底：覆盖原文
            };

            foreach (var c in candidates)
            {
                var x = Math.Clamp(c.X, mon.Left + 6, mon.Right - estW - 6);
                var y = Math.Clamp(c.Y, mon.Top + 6, mon.Bottom - estH - 6);
                if (c == candidates[3] || !IntersectsAny(x, y, estW, estH, blocked, 4 * dip))
                {
                    vm.Placed = (x, y, estW, estH);
                    return ((int)(x - mon.Left), (int)(y - mon.Top));
                }
            }
            vm.Placed = (line.X, line.Y, estW, estH);
            return ((int)(line.X - mon.Left), (int)(line.Y - mon.Top));
        }

        private static bool IntersectsAny(double x, double y, double w, double h,
            List<(double X, double Y, double W, double H)> rects, double inflate)
        {
            foreach (var r in rects)
            {
                if (x < r.X + r.W + inflate && x + w + inflate > r.X &&
                    y < r.Y + r.H + inflate && y + h + inflate > r.Y)
                    return true;
            }
            return false;
        }
    }

    internal static class TooltipVmEstimateExt
    {
        /// <summary>粗略字数（中文按 1、其他按 0.55 个宽度计）。</summary>
        public static double TextLengthEstimate(this TooltipVm vm)
        {
            int cjk = 0, other = 0;
            foreach (var c in vm.Source.Text)
            {
                if (c >= 0x4e00 && c <= 0x9fff) cjk++; else other++;
            }
            // 译文一般不比原文长太多：用原文长度粗估
            return Math.Max(6, cjk + other * 0.6) * 1.2;
        }
    }
}
