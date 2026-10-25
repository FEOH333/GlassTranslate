using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Threading;
using GlassTranslate.Core;

namespace GlassTranslate.UI
{
    /// <summary>
    /// 译文气泡的视图模型。
    /// “逐字输出”的实现：网络增量进入字符队列，UI 计时器每 16ms 释放 3~8 个字符——
    /// 无论网络块大小如何，屏幕上始终是平滑的打字机效果。
    /// </summary>
    public sealed class TooltipVm : INotifyPropertyChanged
    {
        private string _text;
        private double _x, _y;
        private bool _dying;
        private readonly Queue<char> _chars = new();
        private DispatcherTimer? _timer;

        public string Key { get; }
        public ScreenText Source { get; set; }
        /// <summary>帧同步时的贪心匹配标记（OverlayController 用）。</summary>
        public bool Matched;
        /// <summary>预估摆放矩形（该显示器的物理像素），供后续气泡避让。</summary>
        public (double X, double Y, double W, double H) Placed { get; set; }

        public TooltipVm(string key, ScreenText source, string? cachedTranslation)
        {
            Key = key;
            Source = source;
            _text = cachedTranslation ?? "…";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string Text { get => _text; private set { _text = value; Raise(nameof(Text)); } }
        public double X { get => _x; set { _x = value; Raise(nameof(X)); } }
        public double Y { get => _y; set { _y = value; Raise(nameof(Y)); } }
        public bool Dying { get => _dying; set { _dying = value; Raise(nameof(Dying)); } }

        public void AppendDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            foreach (var ch in delta) _chars.Enqueue(ch);
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _timer.Tick += (s, e) => Pump();
            }
            _timer.Start();
        }

        private void Pump()
        {
            if (_chars.Count == 0) { _timer?.Stop(); return; }
            int take = _chars.Count > 90 ? 8 : 3; // 长文本自动加速，保持总时长可感知
            var sb = new StringBuilder(take);
            while (take-- > 0 && _chars.Count > 0) sb.Append(_chars.Dequeue());
            var add = sb.ToString();
            Text = _text == "…" ? add : _text + add;
        }

        public void SetFailed()
        {
            _chars.Clear();
            if (_text == "…") Text = "⚠ 未能翻译";
        }
    }
}
