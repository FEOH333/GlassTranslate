using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GlassTranslate.Core
{
    /// <summary>
    /// 判定一行 OCR 文本是否"非中文、值得翻译"，并给出归一化缓存键。
    /// 规则刻意保守：宁可漏翻，不可把数字/路径/代码/已中文/图标乱码送去花钱翻译。
    /// </summary>
    internal static partial class TextFilter
    {
        [GeneratedRegex(@"[\u4e00-\u9fff]")]
        private static partial Regex CjkRegex();

        [GeneratedRegex(@"[A-Za-z]")]
        private static partial Regex LatinRegex();

        [GeneratedRegex(@"^(https?:\/\/|www\.|ftp\:\/\/)")]
        private static partial Regex UrlRegex();

        [GeneratedRegex(@"^[\d\s\p{P}\p{S}]+$")]
        private static partial Regex OnlySymbolsRegex();

        [GeneratedRegex(@"[A-Za-z]:\\")]
        private static partial Regex WindowsPathRegex();

        [GeneratedRegex(@"^[\w.+-]+@[\w-]+\.[\w.]+$")]
        private static partial Regex EmailRegex();

        [GeneratedRegex(@"[^A-Za-z]+")]
        private static partial Regex NonLetterRegex();

        public static bool ShouldTranslate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            if (t.Length < 3 || t.Length > 480) return false;
            if (CjkRegex().IsMatch(t)) return false;                    // 已含中文（含我们自己的译文气泡）
            if (LatinRegex().Matches(t).Count < 3) return false;        // "OK" 之类的噪声不值得翻译
            if (OnlySymbolsRegex().IsMatch(t)) return false;
            if (UrlRegex().IsMatch(t)) return false;
            if (WindowsPathRegex().IsMatch(t)) return false;
            if (EmailRegex().IsMatch(t)) return false;
            // 无空格且符号占比过半 → 大概率是标识符/哈希/代码
            if (!t.Contains(' '))
            {
                int symbols = t.Count(char.IsPunctuation) + t.Count(char.IsSymbol);
                if (symbols > t.Length / 2) return false;
            }
            // 必须含至少一个"真词"：≥3 个字母且含元音。
            // 过滤图标/图形 OCR 出的无元音乱码（"MCPXR"、"fr?E"）、
            // 快捷键提示（"Ctrl + N"）和无需翻译的缩写（"API"、"CPU"、"PDF"）。
            foreach (var token in NonLetterRegex().Split(t))
            {
                if (token.Length < 3) continue;
                foreach (var c in token)
                {
                    var ch = char.ToLowerInvariant(c);
                    if (ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y') goto hasRealWord;
                }
            }
            return false;
        hasRealWord:
            return true;
        }

        /// <summary>缓存键：去多余空白 + 小写化。让 "Settings" 与 "SETTINGS" 共享一次翻译。</summary>
        public static string NormalizeKey(string text) =>
            Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();
    }
}
