using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GlassTranslate.Core;
using GlassTranslate.Platform;

namespace GlassTranslate.UI
{
    /// <summary>
    /// 译文悬浮层控制器：管理每个显示器的 OverlayWindow 与气泡生命周期，
    /// 实现 ITranslationSink 接收调度器回调（全部转回 UI 线程）。
    ///
    /// v1.3：同一句子多处出现时每个位置一个气泡（按 normKey 分组 + 最近位置贪心匹配，
    /// 滚动时气泡跟随最近的同名行）；气泡尺寸用 FormattedText 实测（替代字符数粗估），
    /// 摆位避让更准；中键点球 / 右键菜单可一键清除全部译文。
    /// </summary>
    internal sealed class OverlayController : ITranslationSink
    {
        private const int MaxTooltips = 64;
        private const int DenseLineThreshold = 18;   // 同屏行数超过此值视为密集场景

        private readonly TranslationCache _cache;
        private readonly Dispatcher _ui = Application.Current.Dispatcher;
        private readonly Dictionary<string, List<TooltipVm>> _realtime = new();
        private readonly Dictionary<string, List<TooltipVm>> _oneShot = new();
        private readonly List<OverlayWindow?> _windows = new();
        private readonly List<(Win32.RECT Rect, uint Dpi)> _monitors = new();
        private List<ScreenText> _lastFrame = new();
        private DateTime _lastErrorAt = DateTime.MinValue;

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
            _ui.BeginInvoke(() => { _lastFrame = current; SyncRealtime(current); });

        public void OnOneShotResult(List<ScreenText> lines) =>
            _ui.BeginInvoke(() =>
            {
                _lastFrame = lines;
                ClearDict(_oneShot);
                foreach (var group in GroupByMonitor(lines))
                    ShowGroup(group.Lines, _oneShot);
            });

        public void OnOneShotClear() => _ui.BeginInvoke(() => ClearDict(_oneShot));

        public void OnTranslationDelta(string normKey, string delta) =>
            _ui.BeginInvoke(() =>
            {
                var list = _realtime.GetValueOrDefault(normKey);
                list ??= _oneShot.GetValueOrDefault(normKey);
                if (list == null) return;
                foreach (var vm in list.ToList())
                {
                    bool firstChunk = vm.Text == "…";
                    vm.AppendDelta(delta);
                    // 首个字符到达后译文长度已知，按实测尺寸重摆一次；放不下则退场
                    if (firstChunk && !Reposition(vm, vm.Source, _lastFrame))
                    {
                        list.Remove(vm);
                        RemoveVm(vm);
                    }
                }
            });

        public void OnTranslationDone(string normKey) { /* 缓存已写，气泡自然打完 */ }

        public void OnTranslationFailed(string normKey) =>
            _ui.BeginInvoke(() =>
            {
                var list = _realtime.GetValueOrDefault(normKey);
                list ??= _oneShot.GetValueOrDefault(normKey);
                if (list != null) foreach (var vm in list) vm.SetFailed();
            });

        public void OnError(string message)
        {
            if ((DateTime.UtcNow - _lastErrorAt).TotalSeconds < 4) return;
            _lastErrorAt = DateTime.UtcNow;
            Toast.Show(message);
        }

        /// <summary>一键清除全部译文（悬浮球中键 / 右键菜单）。</summary>
        public void ClearAll() => _ui.BeginInvoke(() =>
        {
            ClearDict(_oneShot);
            ClearDict(_realtime);
        });

        // ---------------- 实时同步 ----------------

        private void SyncRealtime(List<ScreenText> current)
        {
            var groups = current
                .GroupBy(l => TextFilter.NormalizeKey(l.Text))
                .ToDictionary(g => g.Key, g => g.ToList());

            // 移除整组消失的句子
            foreach (var gone in _realtime.Where(kv => !groups.ContainsKey(kv.Key)).ToList())
                ClearGroup(_realtime, gone.Key);

            int total = TotalCount();
            foreach (var kv in groups)
            {
                if (!_realtime.TryGetValue(kv.Key, out var vms)) vms = new();
                _realtime[kv.Key] = vms;

                // 同句多位置：最近距离贪心匹配，多余行建新泡，多余的泡移除
                MatchOccurrences(kv.Value, vms, ref total, _realtime, current);
            }
        }

        /// <summary>把当前帧中某句子的所有出现位置与现有气泡对齐。</summary>
        private void MatchOccurrences(List<ScreenText> lines, List<TooltipVm> vms,
            ref int total, Dictionary<string, List<TooltipVm>> dict, List<ScreenText> frame)
        {
            var remaining = vms.ToList();
            foreach (var vm in remaining) vm.Matched = false;
            foreach (var line in lines)
            {
                TooltipVm? best = null;
                double bestDist = double.MaxValue;
                foreach (var vm in remaining.Where(v => !v.Matched && v.Source.MonitorIndex == line.MonitorIndex))
                {
                    double d = Math.Abs(vm.Source.X - line.X) + Math.Abs(vm.Source.Y - line.Y);
                    if (d < bestDist) { bestDist = d; best = vm; }
                }
                if (best != null && bestDist < 400)
                {
                    best.Matched = true;
                    bool moved = Math.Abs(best.Source.X - line.X) + Math.Abs(best.Source.Y - line.Y) > 12;
                    best.Source = line;
                    if (moved && !Reposition(best, line, frame))
                    {
                        // 位置放不下（密集场景）→ 该气泡退场
                        vms.Remove(best);
                        RemoveVm(best);
                        continue;
                    }
                }
                else if (total < MaxTooltips)
                {
                    var nv = new TooltipVm(TextFilter.NormalizeKey(line.Text), line,
                        _cache.Get(TextFilter.NormalizeKey(line.Text)));
                    AddToWindow(nv, frame);
                    if (!nv.PlacedOk) continue;   // 放不下：不登记
                    vms.Add(nv);
                    total++;
                }
            }
            foreach (var unmatched in remaining.Where(v => !v.Matched).ToList())
            {
                vms.Remove(unmatched);
                RemoveVm(unmatched);
            }
        }

        private int TotalCount() =>
            _realtime.Values.Sum(l => l.Count) + _oneShot.Values.Sum(l => l.Count);

        private void ShowGroup(List<ScreenText> lines, Dictionary<string, List<TooltipVm>> dict)
        {
            foreach (var line in lines)
            {
                var key = TextFilter.NormalizeKey(line.Text);
                if (!dict.TryGetValue(key, out var vms)) { vms = new(); dict[key] = vms; }
                if (vms.Count >= 6 || TotalCount() >= MaxTooltips) continue;
                var cached = _cache.Get(key);
                var vm = new TooltipVm(key, line, cached);
                AddToWindow(vm, lines);
                if (!vm.PlacedOk) continue;
                vms.Add(vm);
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
            var pos = Place(vm, vm.Source, frameLines, w);
            if (pos == null) return;   // 密集场景放不下：不放，避免堆叠错乱
            vm.X = w.ToDip(pos.Value.px);
            vm.Y = w.ToDip(pos.Value.py);
            w.Add(vm);
            vm.PlacedOk = true;
        }

        private bool Reposition(TooltipVm vm, ScreenText line, List<ScreenText> frameLines)
        {
            var w = GetWindow(line.MonitorIndex);
            var pos = Place(vm, line, frameLines, w);
            if (pos == null) return false;
            vm.MoveTo(w.ToDip(pos.Value.px), w.ToDip(pos.Value.py));
            return true;
        }

        private void RemoveVm(TooltipVm vm)
        {
            vm.Dying = true;
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(130) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                foreach (var w in _windows)
                    if (w != null && w.Count > 0) w.Remove(vm);
            };
            t.Start();
        }

        private void ClearGroup(Dictionary<string, List<TooltipVm>> dict, string key)
        {
            if (!dict.TryGetValue(key, out var vms)) return;
            foreach (var vm in vms) RemoveVm(vm);
            dict.Remove(key);
        }

        private void ClearDict(Dictionary<string, List<TooltipVm>> dict)
        {
            foreach (var key in dict.Keys.ToList()) ClearGroup(dict, key);
        }

        // ---------------- 摆位算法 ----------------

        /// <summary>
        /// 摆位：下方 → 上方 → 右侧；密集画面（同屏行数多）不再兜底覆盖原文，
        /// 全部冲突时返回 null（放不下就不放，宁缺毋滥）。
        /// </summary>
        private (int px, int py)? Place(TooltipVm vm, ScreenText line, List<ScreenText> frameLines, OverlayWindow w)
        {
            var mon = w.PixelRect;
            var dip = w.Dpi / 96.0;
            var (estW, estH) = MeasureBubble(vm, dip);

            var blocked = new List<(double X, double Y, double W, double H)>();
            int sameMonitorLines = 0;
            foreach (var l in frameLines)
            {
                if (l.MonitorIndex != w.MonitorIndex) continue;
                sameMonitorLines++;
                if (l.X == line.X && l.Y == line.Y && l.Text == line.Text) continue;
                blocked.Add((l.X, l.Y, l.Width, l.Height));
            }
            foreach (var d in new[] { _realtime, _oneShot })
                foreach (var list in d.Values)
                    foreach (var other in list)
                        if (other != vm && !other.Dying && other.Source.MonitorIndex == w.MonitorIndex)
                            blocked.Add(other.Placed);

            bool allowCover = sameMonitorLines <= DenseLineThreshold;
            double Gap = 6 * dip;
            var candidates = new (double X, double Y)[]
            {
                (line.X, line.Y + line.Height + Gap),          // 下方
                (line.X, line.Y - estH - Gap),                 // 上方
                (line.X + line.Width + Gap, line.Y)            // 右侧
            };

            foreach (var c in candidates)
            {
                var x = Math.Clamp(c.X, mon.Left + 6, mon.Right - estW - 6);
                var y = Math.Clamp(c.Y, mon.Top + 6, mon.Bottom - estH - 6);
                if (!IntersectsAny(x, y, estW, estH, blocked, 4 * dip))
                {
                    vm.Placed = (x, y, estW, estH);
                    return ((int)(x - mon.Left), (int)(y - mon.Top));
                }
            }
            if (!allowCover) return null;
            vm.Placed = (line.X, line.Y, estW, estH);
            return ((int)(line.X - mon.Left), (int)(line.Y - mon.Top));
        }

        /// <summary>
        /// 气泡尺寸：译文已开始流出时用 FormattedText 实测；
        /// 还是占位符时按原文长度粗估（首个字符到达后会重摆一次）。
        /// </summary>
        private static (double pw, double ph) MeasureBubble(TooltipVm vm, double dip)
        {
            double wDip, hDip;
            if (vm.Text is not "…" and { Length: > 0 })
            {
                var ft = new FormattedText(
                    vm.Text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI, Microsoft YaHei UI"),
                        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    13.5, Brushes.White, 1.0);
                ft.MaxTextWidth = 320;   // MaxWidth 340 - 左右 padding
                ft.LineHeight = 20;
                wDip = Math.Min(ft.Width, 320) + 22;
                hDip = ft.Height + 14;
            }
            else
            {
                int cjk = 0, other = 0;
                foreach (var c in vm.Source.Text)
                {
                    if (c >= 0x4e00 && c <= 0x9fff) cjk++; else other++;
                }
                double estChars = Math.Max(6, (cjk + other * 0.6) * 1.2);
                wDip = Math.Min(340, 52 + estChars * 13.0);
                hDip = 34 + (estChars > 22 ? 20 : 0);
            }
            return (wDip * dip, hDip * dip);
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
}
