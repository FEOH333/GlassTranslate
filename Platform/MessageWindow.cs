using System;
using System.Windows.Interop;

namespace GlassTranslate.Platform
{
    /// <summary>
    /// 隐藏的 message-only 窗口：承接全局热键与托盘图标回调，
    /// 与任何可见窗口解耦（悬浮球隐藏后热键/托盘依然工作）。
    /// </summary>
    internal sealed class MessageWindow : IDisposable
    {
        public const int TrayCallbackMsg = Win32.WM_APP + 1;
        public const uint TrayIconId = 1;

        private readonly HwndSource _source;
        public IntPtr Handle => _source.Handle;

        /// <summary>热键按下（参数 = 热键 id）。</summary>
        public event Action<int>? HotkeyPressed;
        /// <summary>托盘图标鼠标消息（参数 = WM_LBUTTONUP / WM_RBUTTONUP / WM_LBUTTONDBLCLK …）。</summary>
        public event Action<uint>? TrayMessage;

        public MessageWindow()
        {
            var parameters = new HwndSourceParameters("GlassTranslateMessageWindow", 0, 0)
            {
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                HotkeyPressed?.Invoke(wParam.ToInt32());
                handled = true;
            }
            else if (msg == TrayCallbackMsg)
            {
                TrayMessage?.Invoke((uint)lParam.ToInt64());
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }
}
