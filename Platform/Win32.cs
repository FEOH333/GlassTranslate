using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassTranslate.Platform
{
    /// <summary>
    /// 所有原生 API 声明集中在此，按 dll 分组。
    /// </summary>
    internal static class Win32
    {
        // ---------------- user32 ----------------

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_NOACTIVATE = 0x0010, SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004;

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000,
                          WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8;

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312, WM_APP = 0x8000;
        public const uint WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;

        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc lpfn, IntPtr data);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("Shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            public static MONITORINFO Create() => new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        }

        // ---------------- gdi32（屏幕捕获） ----------------

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sxDc, int syDc, uint rop);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dest, int xDest, int yDest, int wDest, int hDest, IntPtr src, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

        public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, DIB_RGB_COLORS = 0, BI_RGB = 0;

        // ---------------- dwm ----------------

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // ---------------- shell32（托盘） ----------------

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
        public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState, dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
        }

        // ---------------- crypt32（DPAPI，本地加密保存 API Key） ----------------

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CryptProtectData(ref DATA_BLOB input, string description, IntPtr entropy, IntPtr reserved, uint prompt, uint flags, out DATA_BLOB output);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CryptUnprotectData(ref DATA_BLOB input, StringBuilder? description, IntPtr entropy, IntPtr reserved, uint prompt, uint flags, out DATA_BLOB output);

        [StructLayout(LayoutKind.Sequential)]
        public struct DATA_BLOB { public int cbData; public IntPtr pbData; }

        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr hMem);

        // ---------------- kernel32（自检时挂接控制台） ----------------

        [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint pid);
        public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    }
}
