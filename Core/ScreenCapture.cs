using System;
using System.Runtime.InteropServices;
using GlassTranslate.Platform;

namespace GlassTranslate.Core
{
    /// <summary>
    /// GDI 屏幕捕获（每显示器一次 BitBlt，BGRA 32 位）。
    /// 选 GDI 而非 Windows.Graphics.Capture：无需可见的黄边/系统 UI，延迟低、实现简单，
    /// 对每 1.5 秒一次的轮询足够。超大的屏幕按 2 的幂缩放（StretchBlt + HALFTONE）。
    /// </summary>
    internal static class ScreenCapture
    {
        public static byte[] CaptureSmart(int x, int y, int w, int h, int maxDim,
            out int sw, out int sh, out double scale, byte[]? reuse = null)
        {
            sw = w; sh = h;
            while (Math.Max(sw, sh) > Math.Max(64, maxDim))
            {
                sw = Math.Max(1, sw / 2);
                sh = Math.Max(1, sh / 2);
            }
            scale = w / (double)sw;

            var screenDc = Win32.GetDC(IntPtr.Zero);
            var memDc = Win32.CreateCompatibleDC(screenDc);
            var bmi = new Win32.BITMAPINFO
            {
                bmiHeader = new Win32.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                    biWidth = sw,
                    biHeight = -sh, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32.BI_RGB
                }
            };
            var dib = Win32.CreateDIBSection(screenDc, ref bmi, Win32.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            byte[] px = Array.Empty<byte>();
            if (dib != IntPtr.Zero && bits != IntPtr.Zero)
            {
                var old = Win32.SelectObject(memDc, dib);
                if (sw == w && sh == h)
                {
                    Win32.BitBlt(memDc, 0, 0, w, h, screenDc, x, y, Win32.SRCCOPY | Win32.CAPTUREBLT);
                }
                else
                {
                    Win32.SetStretchBltMode(memDc, 4); // HALFTONE
                    Win32.StretchBlt(memDc, 0, 0, sw, sh, screenDc, x, y, w, h, Win32.SRCCOPY | Win32.CAPTUREBLT);
                }
                px = reuse is { Length: > 0 } buf && buf.Length == sw * sh * 4
                    ? buf
                    : new byte[sw * sh * 4];
                Marshal.Copy(bits, px, 0, px.Length);
                Win32.SelectObject(memDc, old);
                Win32.DeleteObject(dib);
            }
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);

            // GDI 的 alpha 通道是 0，显式置 255，保证 OCR 视为实心像素
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return px;
        }
        /// <summary>
        /// 运动探针截图：宽约 240px 的低清帧，不含 CAPTUREBLT/层叠窗口
        /// （自己的译文气泡不会触发自循环），单屏约 2-4ms。
        /// </summary>
        public static byte[] CaptureTiny(int x, int y, int w, int h, byte[]? reuse, out int tw, out int th)
        {
            tw = Math.Min(Math.Max(64, w * 240 / Math.Max(1, w)), w);
            th = Math.Max(1, (int)((long)h * tw / w));

            var screenDc = Win32.GetDC(IntPtr.Zero);
            var memDc = Win32.CreateCompatibleDC(screenDc);
            var bmi = new Win32.BITMAPINFO
            {
                bmiHeader = new Win32.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                    biWidth = tw,
                    biHeight = -th,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32.BI_RGB
                }
            };
            var dib = Win32.CreateDIBSection(screenDc, ref bmi, Win32.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            byte[] px = Array.Empty<byte>();
            if (dib != IntPtr.Zero && bits != IntPtr.Zero)
            {
                var old = Win32.SelectObject(memDc, dib);
                Win32.SetStretchBltMode(memDc, 3); // COLORONCOLOR：快
                Win32.StretchBlt(memDc, 0, 0, tw, th, screenDc, x, y, w, h, Win32.SRCCOPY);
                px = reuse is { Length: > 0 } buf && buf.Length == tw * th * 4 ? buf : new byte[tw * th * 4];
                Marshal.Copy(bits, px, 0, px.Length);
                Win32.SelectObject(memDc, old);
                Win32.DeleteObject(dib);
            }
            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return px;
        }
    }
}
