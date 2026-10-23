using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GlassTranslate.UI
{
    public static class Toast
    {
        private static ToastWindow? _w;
        public static void Show(string message)
        {
            var app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(() =>
            {
                _w ??= new ToastWindow();
                _w.ShowMessage(message);
            });
        }
    }

    /// <summary>右下角玻璃风小提示（不抢焦点、自动淡出）。</summary>
    internal sealed class ToastWindow : Window
    {
        private readonly Border _border;
        private readonly TextBlock _text;
        private readonly DispatcherTimer _timer;

        public ToastWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Width = 380;
            SizeToContent = SizeToContent.Height;

            _text = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
            System.Windows.Media.TextOptions.SetTextFormattingMode(_text, TextFormattingMode.Display);
            _border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(232, 22, 26, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 10, 16, 10),
                Child = _text,
                Opacity = 0
            };
            Content = _border;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
            _timer.Tick += (s, e) => { _timer.Stop(); HideFade(); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Platform.WindowEffects.MakeToolWindowNoActivate(this);
        }

        public void ShowMessage(string msg)
        {
            _text.Text = msg;
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 16;
            Top = wa.Bottom - 84;
            Show();
            _border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            _timer.Stop();
            _timer.Start();
        }

        private void HideFade()
        {
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
            a.Completed += (s, e) => Hide();
            _border.BeginAnimation(OpacityProperty, a);
        }
    }
}
