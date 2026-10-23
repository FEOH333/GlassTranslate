using System;
using System.Windows;
using System.Windows.Interop;

namespace GlassTranslate.Platform
{
    /// <summary>无边框窗口的通用原生效果。</summary>
    internal static class WindowEffects
    {
        public static IntPtr Hwnd(Window w) => new WindowInteropHelper(w).Handle;

        /// <summary>不出现在 Alt+Tab、点击不抢焦点、置顶。</summary>
        public static void MakeToolWindowNoActivate(Window w)
        {
            int ex = Win32.GetWindowLong(Hwnd(w), Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(Hwnd(w), Win32.GWL_EXSTYLE,
                ex | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST);
        }

        /// <summary>完全点击穿透（翻译悬浮层专用）：鼠标事件直接落到下层窗口。</summary>
        public static void MakeClickThrough(Window w)
        {
            int ex = Win32.GetWindowLong(Hwnd(w), Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(Hwnd(w), Win32.GWL_EXSTYLE,
                ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_LAYERED |
                Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST);
        }

        /// <summary>以物理像素为单位移动/缩放窗口（跨 DPI 场景必须走这条路）。</summary>
        public static void SetWindowPixelRect(Window w, int x, int y, int cx, int cy)
        {
            Win32.SetWindowPos(Hwnd(w), IntPtr.Zero, x, y, cx, cy,
                Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
        }

        /// <summary>把窗口重新提到置顶序列最上方（但不激活）。</summary>
        public static void ReassertTopmost(Window w)
        {
            Win32.SetWindowPos(Hwnd(w), Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOACTIVATE | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE);
        }

        // 注：曾尝试 DWMWA_SYSTEMBACKDROP 亚克力背板，但在 WPF 逐像素透明窗口上
        // 会垫一层不透明灰底并附加系统边框，视觉效果反而变差，已弃用；
        // 玻璃质感统一由 WPF 渐变 + 发光实现。
    }
}
