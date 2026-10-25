using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GlassTranslate.Platform;

namespace GlassTranslate.UI
{
    /// <summary>
    /// 悬浮球窗口：空心玻璃圆球 ←→ 三按钮面板 的形态切换。
    /// 左键点按展开、拖拽移动（球态拖球 / 面板态拖面板空白处）；鼠标离开面板 5 秒自动缩回，
    /// 也可点面板右上角 ✕ 立即收起；右键菜单隐藏/退出。
    /// 注意：悬停动画在代码里做——WPF 的 Style 触发器不允许 Storyboard 引用命名元素。
    /// </summary>
    public partial class BallWindow : Window
    {
        private const double BallSize = 64, PanelW = 320, PanelH = 192;

        private readonly AppController _controller;
        private readonly DispatcherTimer _collapseTimer;
        private readonly DispatcherTimer _recordTimer;
        private bool _expanded;
        private bool _pressed, _dragged;        // 球态拖拽
        private bool _panelPressed, _panelDragged; // 面板态拖拽
        private System.Windows.Point _pressScreen;
        private bool _recording;
        private bool _hintShown;

        private static readonly int[] PollVks = BuildPollVks();

        private static int[] BuildPollVks() =>
            Enumerable.Range(0x30, 10)
            .Concat(Enumerable.Range(0x41, 26))
            .Concat(Enumerable.Range(0x70, 24))
            .Concat(new[] { 0x20, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0,
                            0xDB, 0xDC, 0xDD, 0xDE, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E })
            .ToArray();

        public BallWindow(AppController controller)
        {
            InitializeComponent();
            _controller = controller;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");

            _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _collapseTimer.Tick += (s, e) => { _collapseTimer.Stop(); Collapse(); };

            _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _recordTimer.Tick += (s, e) => PollRecordingKeys();

            SourceInitialized += (s, e) => WindowEffects.MakeToolWindowNoActivate(this);
            Closing += (s, e) => e.Cancel = true;

            // ---- 悬浮球交互 ----
            Ball.MouseLeftButtonDown += OnBallDown;
            Ball.MouseMove += OnBallMove;
            Ball.MouseLeftButtonUp += OnBallUp;
            Ball.MouseRightButtonUp += (s, e) => { BallMenu.PlacementTarget = Ball; BallMenu.IsOpen = true; };
            Ball.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    _controller.ClearTranslations();
                    e.Handled = true;
                }
            };
            MenuClear.Click += (s, e) => _controller.ClearTranslations();
            MenuHideBall.Click += (s, e) => _controller.ShowBall(false);
            MenuExit.Click += (s, e) => _controller.ExitApp();

            // ---- 面板交互 ----
            PanelRoot.MouseLeftButtonDown += OnPanelDown;
            MouseMove += OnPanelMove;
            MouseLeftButtonUp += OnPanelUp;
            BtnCollapse.Click += (s, e) => Collapse();
            HkBox.MouseLeftButtonDown += (s, e) => StartRecording();
            BtnApi.Click += (s, e) => { _controller.OpenSettings(); StartCollapseSoon(1200); };
            TgRealtime.Click += (s, e) => _controller.ToggleRealtime(TgRealtime.IsChecked == true);
            TgHotkey.Click += (s, e) => _controller.SetHotkeyMode(TgHotkey.IsChecked == true);

            // ---- 悬停动效 + 自动收起 ----
            MouseEnter += (s, e) => { _collapseTimer.Stop(); HoverIn(); };
            MouseLeave += (s, e) => { HoverOut(); if (_expanded) _collapseTimer.Start(); };

            RestorePosition();
        }

        // ---------------- 悬停动效（代码驱动，避免 Style 触发器限制） ----------------

        private void HoverIn()
        {
            if (_expanded) return;
            AnimateScale(1.15, 160);
            Sheen.BeginAnimation(OpacityProperty, new DoubleAnimation(0.95, M(180)));
        }

        private void HoverOut()
        {
            if (_expanded) return;
            AnimateScale(1.0, 260);
            Sheen.BeginAnimation(OpacityProperty, new DoubleAnimation(0.62, M(240)));
        }

        private void AnimateScale(double to, double ms)
        {
            var a = new DoubleAnimation(to, M(ms)) { EasingFunction = EaseOut };
            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ---------------- 位置与拖拽 ----------------

        private void RestorePosition()
        {
            var cfg = _controller.Config;
            if (!double.IsNaN(cfg.BallLeft) && !double.IsNaN(cfg.BallTop))
            {
                Left = cfg.BallLeft;
                Top = cfg.BallTop;
            }
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - 96;
                Top = wa.Top + 140;
            }
            ClampIntoScreen();
        }

        private void ClampIntoScreen()
        {
            var vsL = SystemParameters.VirtualScreenLeft;
            var vsT = SystemParameters.VirtualScreenTop;
            var vsR = vsL + SystemParameters.VirtualScreenWidth;
            var vsB = vsT + SystemParameters.VirtualScreenHeight;
            Left = Math.Clamp(Left, vsL, Math.Max(vsL, vsR - Width));
            Top = Math.Clamp(Top, vsT, Math.Max(vsT, vsB - Height));
        }

        private double DipScale()
        {
            var src = PresentationSource.FromVisual(this);
            return src?.CompositionTarget != null && src.CompositionTarget.TransformToDevice.M11 > 0
                ? src.CompositionTarget.TransformToDevice.M11 : 1;
        }

        private void SaveBallPosition()
        {
            _controller.Config.BallLeft = Left;
            _controller.Config.BallTop = Top;
            _controller.SaveConfig();
        }

        // ---- 球态拖拽 ----

        private void OnBallDown(object sender, MouseButtonEventArgs e)
        {
            _pressed = true;
            _dragged = false;
            _pressScreen = Ball.PointToScreen(e.GetPosition(Ball));
            Ball.CaptureMouse();
        }

        private void OnBallMove(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;
            var p = Ball.PointToScreen(e.GetPosition(Ball));
            double dist = Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y);
            if (!_dragged && dist > 5) _dragged = true;
            if (_dragged) { MoveByScreenDelta(p); ClampIntoScreen(); }
        }

        private void OnBallUp(object sender, MouseButtonEventArgs e)
        {
            Ball.ReleaseMouseCapture();
            if (_pressed && !_dragged && !_expanded) Expand();
            _pressed = false;
            if (_dragged) { ClampIntoScreen(); SaveBallPosition(); }
        }

        // ---- 面板态拖拽（点在按钮等控件上时不拖） ----

        private void OnPanelDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOverInteractiveControl(e.OriginalSource)) return;
            _panelPressed = true;
            _panelDragged = false;
            _pressScreen = PanelRoot.PointToScreen(e.GetPosition(PanelRoot));
            PanelRoot.CaptureMouse();
        }

        private void OnPanelMove(object sender, MouseEventArgs e)
        {
            if (!_panelPressed) return;
            var p = PanelRoot.PointToScreen(e.GetPosition(PanelRoot));
            if (!_panelDragged && Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 5)
                _panelDragged = true;
            if (_panelDragged) { MoveByScreenDelta(p); ClampIntoScreen(); }
        }

        private void OnPanelUp(object sender, MouseButtonEventArgs e)
        {
            if (!_panelPressed) return;
            PanelRoot.ReleaseMouseCapture();
            _panelPressed = false;
            if (_panelDragged) SaveBallPosition();
        }

        private bool MoveByScreenDelta(System.Windows.Point now)
        {
            double dx = now.X - _pressScreen.X, dy = now.Y - _pressScreen.Y;
            if (dx == 0 && dy == 0) return false;
            double scale = DipScale();
            if (scale > 0) { dx /= scale; dy /= scale; }
            Left += dx;
            Top += dy;
            _pressScreen = now;
            return true;
        }

        private static bool IsOverInteractiveControl(object originalSource)
        {
            for (DependencyObject? d = originalSource as DependencyObject; d != null;
                 d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            {
                if (d is ButtonBase or TextBox or ComboBox or PasswordBox) return true;
            }
            return false;
        }

        // ---------------- 展开 / 缩回 ----------------

        private static TimeSpan M(double ms) => TimeSpan.FromMilliseconds(ms);
        private static CubicEase EaseOut => new() { EasingMode = EasingMode.EaseOut };

        private void AnimateBox(double w, double h, double left, double top, double ms)
        {
            foreach (var (prop, to) in new[]
            {
                (WidthProperty, w), (HeightProperty, h),
                (LeftProperty, left), (TopProperty, top)
            })
            {
                BeginAnimation(prop, new DoubleAnimation(to, M(ms)) { EasingFunction = EaseOut });
            }
        }

        public void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            WindowEffects.ReassertTopmost(this);
            PanelHost.Visibility = Visibility.Visible;

            var cx = Left + Width / 2;
            var cy = Top + Height / 2;
            // “向两端展开”：窗口从中心向四周生长成面板
            AnimateBox(PanelW, PanelH, cx - PanelW / 2, cy - PanelH / 2, 340);

            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.55, M(160)) { EasingFunction = EaseOut });
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.55, M(160)) { EasingFunction = EaseOut });
            Ball.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, M(150)));

            PanelT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, M(300)) { EasingFunction = EaseOut });
            PanelRoot.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, M(240)) { BeginTime = M(110), EasingFunction = EaseOut });
            PanelRoot.IsHitTestVisible = true;
        }

        public void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            CancelRecording();
            _collapseTimer.Stop();

            PanelRoot.IsHitTestVisible = false;
            PanelRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, M(130)));
            PanelT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 8, M(160)));

            var cx = Left + Width / 2;
            var cy = Top + Height / 2;
            Delay(150, () =>
            {
                PanelHost.Visibility = Visibility.Collapsed;
                AnimateBox(BallSize, BallSize, cx - BallSize / 2, cy - BallSize / 2, 320);
                Ball.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, M(220)) { BeginTime = M(140) });
                BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.55, 1, M(260)) { EasingFunction = EaseOut });
                BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.55, 1, M(260)) { EasingFunction = EaseOut });
                ClampIntoScreen();
            });
        }

        /// <summary>隐藏前立刻收成球态（无动画，直接落位）。</summary>
        public void ForceCollapseInstant()
        {
            var cx = Left + Width / 2;
            var cy = Top + Height / 2;
            _expanded = false;
            CancelRecording();
            _collapseTimer.Stop();
            PanelHost.Visibility = Visibility.Collapsed;
            foreach (var prop in new[] { WidthProperty, HeightProperty, LeftProperty, TopProperty })
                BeginAnimation(prop, null);
            Width = BallSize; Height = BallSize;
            Left = cx - BallSize / 2; Top = cy - BallSize / 2;
            PanelRoot.BeginAnimation(OpacityProperty, null);
            PanelRoot.Opacity = 0;
            PanelRoot.IsHitTestVisible = false;
            Ball.BeginAnimation(OpacityProperty, null);
            Ball.Opacity = 1;
            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            BallScale.ScaleX = BallScale.ScaleY = 1;
            ClampIntoScreen();
            SaveBallPosition();
        }

        private void StartCollapseSoon(double ms) => Delay(ms, () => { if (_expanded && !IsMouseOver) Collapse(); });

        private void Delay(double ms, Action act)
        {
            var t = new DispatcherTimer { Interval = M(ms) };
            t.Tick += (s, e) => { t.Stop(); act(); };
            t.Start();
        }

        public void ReassertTopmost()
        {
            if (IsLoaded) WindowEffects.ReassertTopmost(this);
        }

        // ---------------- 快捷键录制（无焦点轮询） ----------------

        private void StartRecording()
        {
            if (_recording) { CancelRecording(); return; }
            _recording = true;
            _hintShown = false;
            HkText.Text = "按下组合键…";
            HkBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7D, 0xD3, 0xFC));
            var pulse = new DoubleAnimation(0.55, 1, M(450))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            HkBox.BeginAnimation(OpacityProperty, pulse);
            _recordTimer.Start();
        }

        private void CancelRecording()
        {
            if (!_recording) return;
            _recording = false;
            _recordTimer.Stop();
            HkBox.BeginAnimation(OpacityProperty, null);
            HkBox.Opacity = 1;
            HkBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            HkText.Text = _controller.Config.Hotkey;
        }

        private static bool IsVkDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;

        private void PollRecordingKeys()
        {
            if (!_recording) return;
            if (IsVkDown(0x1B)) { CancelRecording(); return; } // Esc

            bool ctrl = IsVkDown(0x11), alt = IsVkDown(0x12), shift = IsVkDown(0x10),
                 win = IsVkDown(0x5B) || IsVkDown(0x5C);

            int vk = 0;
            foreach (var k in PollVks)
            {
                if (IsVkDown(k)) { vk = k; break; }
            }
            if (vk == 0) return;

            bool isFKey = vk is >= 0x70 and <= 0x87;
            if (!(ctrl || alt || win) && !isFKey)
            {
                if (!_hintShown)
                {
                    _hintShown = true;
                    HkText.Text = "需含 Ctrl/Alt/Win";
                    Delay(1100, () => { if (_recording) HkText.Text = "按下组合键…"; _hintShown = false; });
                }
                return;
            }

            var combo = HotkeyManager.FormatFromVk(vk, ctrl, alt, shift, win);
            _recording = false;
            _recordTimer.Stop();
            HkBox.BeginAnimation(OpacityProperty, null);
            HkBox.Opacity = 1;
            HkBox.BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            HkText.Text = combo;

            _controller.ApplyHotkeyCombo(combo);
        }

        // ---------------- 状态同步 ----------------

        public void UpdateStates(bool realtimeOn, bool hotkeyModeOn, string hotkeyText)
        {
            TgRealtime.IsChecked = realtimeOn;
            TgHotkey.IsChecked = hotkeyModeOn;
            if (!_recording) HkText.Text = hotkeyText;
        }
    }
}
