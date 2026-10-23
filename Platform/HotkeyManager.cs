using System;
using System.Text;
using System.Windows.Input;

namespace GlassTranslate.Platform
{
    /// <summary>全局热键注册/解析（RegisterHotKey，与悬浮球窗口无关）。</summary>
    internal sealed class HotkeyManager
    {
        public const int MainId = 1;
        private readonly IntPtr _hwnd;
        private string? _current;

        public HotkeyManager(IntPtr hwnd) => _hwnd = hwnd;
        public string? Current => _current;

        /// <summary>解析 "Ctrl+Alt+T" 形式的组合键；失败返回 null。</summary>
        public static (uint mods, uint vk)? Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            uint mods = 0, vk = 0;
            foreach (var part in s.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim().ToLowerInvariant();
                if (p is "ctrl" or "control") mods |= Win32.MOD_CONTROL;
                else if (p == "alt") mods |= Win32.MOD_ALT;
                else if (p == "shift") mods |= Win32.MOD_SHIFT;
                else if (p == "win") mods |= Win32.MOD_WIN;
                else
                {
                    if (!TryVkFromName(p, out vk) || vk == 0) return null;
                }
            }
            if (vk == 0) return null;
            return (mods | Win32.MOD_NOREPEAT, vk);
        }

        private static bool TryVkFromName(string name, out uint vk)
        {
            vk = 0;
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), name, true);
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                return vk != 0;
            }
            catch { return false; }
        }

        public static string FormatFromVk(int vk, bool ctrl, bool alt, bool shift, bool win)
        {
            var key = KeyInterop.KeyFromVirtualKey(vk);
            var sb = new StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (alt) sb.Append("Alt+");
            if (shift) sb.Append("Shift+");
            if (win) sb.Append("Win+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        public bool Register(string combo)
        {
            Unregister();
            var parsed = Parse(combo);
            if (parsed == null) return false;
            if (!Win32.RegisterHotKey(_hwnd, MainId, parsed.Value.mods, parsed.Value.vk)) return false;
            _current = combo;
            return true;
        }

        public void Unregister()
        {
            Win32.UnregisterHotKey(_hwnd, MainId);
            _current = null;
        }
    }
}
