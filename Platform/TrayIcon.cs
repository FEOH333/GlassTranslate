using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassTranslate.Platform;

namespace GlassTranslate.Platform
{
    /// <summary>
    /// 系统托盘图标（自绘圆环图标，与悬浮球同一视觉语言），零第三方依赖。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private Win32.NOTIFYICONDATA _nid;
        private readonly IntPtr _hwnd;
        private bool _added;

        /// <summary>右键弹出的菜单（由调用方填充）。</summary>
        public ContextMenu Menu { get; } = new();

        public TrayIcon(IntPtr hwnd, uint callbackMsg)
        {
            _hwnd = hwnd;
            _nid = new Win32.NOTIFYICONDATA
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = MessageWindow.TrayIconId,
                uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
                uCallbackMessage = callbackMsg,
                szTip = "GlassTranslate 实时翻译",
                hIcon = CreateRingIcon()
            };
            _added = Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _nid);
        }

        public void ShowContextMenu()
        {
            // 经典做法：先置前台窗口，否则菜单无法在点击别处时自动关闭
            Win32.SetForegroundWindow(_hwnd);
            Win32.GetCursorPos(out var p);
            Menu.Placement = PlacementMode.AbsolutePoint;
            Menu.HorizontalOffset = p.X;
            Menu.VerticalOffset = p.Y;
            Menu.IsOpen = true;
        }

        /// <summary>用 WPF 绘制 32x32 圆环图标，转成 HICON（不依赖 System.Drawing）。</summary>
        private static IntPtr CreateRingIcon()
        {
            const int size = 32;
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var ring = new LinearGradientBrush(
                    Color.FromRgb(0xBF, 0xE7, 0xFF), Color.FromRgb(0xA5, 0xB4, 0xFC), 30);
                var glow = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
                dc.DrawEllipse(null, new Pen(glow, 4.5),
                    new System.Windows.Point(16, 16), 11, 11);
                dc.DrawEllipse(null, new Pen(ring, 3.2),
                    new System.Windows.Point(16, 16), 11, 11);
            }
            rtb.Render(visual);
            rtb.Freeze();

            int stride = size * 4;
            var pixels = new byte[stride * size];
            rtb.CopyPixels(pixels, stride, 0);
            var andMask = new byte[ stride / 4 * size / 4 ]; // 1bpp：32x32 位掩码全 0（使用 per-pixel alpha）

            return CreateIconInstance(size, pixels, andMask);
        }

        private static IntPtr CreateIconInstance(int size, byte[] xorBits, byte[] andBits)
        {
            // CreateIcon(hInst, w, h, planes=1, bpp=32, AND bits, XOR bits)
            var handle = CreateIcon(IntPtr.Zero, size, size, (byte)1, (byte)32, andBits, xorBits);
            return handle == IntPtr.Zero ? IntPtr.Zero : handle;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        private static extern IntPtr CreateIcon(IntPtr hInstance, int nWidth, int nHeight, byte nPlanes, byte nBitsPixel,
            byte[] lpANDbits, byte[] lpXORbits);

        public void Dispose()
        {
            if (_added)
            {
                var n = _nid;
                Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref n);
                _added = false;
            }
            if (_nid.hIcon != IntPtr.Zero)
            {
                Win32.DestroyIcon(_nid.hIcon);
                _nid.hIcon = IntPtr.Zero;
            }
            Menu.IsOpen = false;
        }
    }
}
