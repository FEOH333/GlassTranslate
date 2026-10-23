using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassTranslate.Platform
{
    /// <summary>Windows DPAPI（当前用户范围）加解密，用于保存 API Key。</summary>
    internal static class DpApi
    {
        public static string Protect(string plain)
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var input = new Win32.DATA_BLOB
            {
                cbData = bytes.Length,
                pbData = Marshal.AllocHGlobal(bytes.Length)
            };
            try
            {
                Marshal.Copy(bytes, 0, input.pbData, bytes.Length);
                if (!Win32.CryptProtectData(ref input, "GlassTranslate", IntPtr.Zero, IntPtr.Zero, 0, 0, out var output))
                    return "";
                try
                {
                    var outBytes = new byte[output.cbData];
                    Marshal.Copy(output.pbData, outBytes, 0, output.cbData);
                    return Convert.ToBase64String(outBytes);
                }
                finally { Win32.LocalFree(output.pbData); }
            }
            catch { return ""; }
            finally { Marshal.FreeHGlobal(input.pbData); }
        }

        public static string Unprotect(string cipher)
        {
            var bytes = Convert.FromBase64String(cipher);
            var input = new Win32.DATA_BLOB
            {
                cbData = bytes.Length,
                pbData = Marshal.AllocHGlobal(bytes.Length)
            };
            try
            {
                Marshal.Copy(bytes, 0, input.pbData, bytes.Length);
                if (!Win32.CryptUnprotectData(ref input, null, IntPtr.Zero, IntPtr.Zero, 0, 0, out var output))
                    return "";
                try
                {
                    var outBytes = new byte[output.cbData];
                    Marshal.Copy(output.pbData, outBytes, 0, output.cbData);
                    return Encoding.UTF8.GetString(outBytes);
                }
                finally { Win32.LocalFree(output.pbData); }
            }
            catch { return ""; }
            finally { Marshal.FreeHGlobal(input.pbData); }
        }
    }
}
