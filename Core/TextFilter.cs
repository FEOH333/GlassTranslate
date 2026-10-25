using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GlassTranslate.Core
{
    /// <summary>
    /// 判定一行 OCR 文本是否"非中文、值得翻译"，并给出归一化缓存键。
    ///
    /// 两级过滤：
    ///  - 常规：剔除乱码/路径/URL/纯数字等噪声；
    ///  - 严格（strict，代码编辑器/终端等密集画面自动启用）：
    ///    只保留"句子样"文本（≥2 个真词），单词与片段一律不翻——
    ///    复杂画面宁可少翻，不可满屏气泡错乱。
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

        [GeneratedRegex(@"[A-Za-z_]\w*\(")]
        private static partial Regex FuncCallRegex();

        [GeneratedRegex(@"\w+_\w+")]
        private static partial Regex SnakeCaseRegex();

        [GeneratedRegex(@"^(error|warning|exception|fatal|traceback|panic|at\s+|stack\s|thread\s|caused\s+by|note:|hint:|usage:)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex LogPrefixRegex();

        [GeneratedRegex(@"^\[\d{1,2}:\d{2}")]
        private static partial Regex TimestampRegex();

        public static bool ShouldTranslate(string text) => ShouldTranslate(text, strict: false);

        public static bool ShouldTranslate(string text, bool strict)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            if (t.Length < 3 || t.Length > 480) return false;
            if (CjkRegex().IsMatch(t)) return false;                    // 已含中文（含我们自己的译文气泡）
            if (LatinRegex().Matches(t).Count < 3) return false;
            if (OnlySymbolsRegex().IsMatch(t)) return false;
            if (UrlRegex().IsMatch(t) || WindowsPathRegex().IsMatch(t) || EmailRegex().IsMatch(t)) return false;
            if (LooksLikeCode(t)) return false;

            int realWords = 0;
            foreach (var token in NonLetterRegex().Split(t))
            {
                if (token.Length < 3) continue;
                foreach (var c in token)
                {
                    var ch = char.ToLowerInvariant(c);
                    if (ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'y')
                    {
                        // 严格模式要求"真词"更长（≥4 字母），且至少两个
                        if (!strict) return true;
                        if (token.Length >= 4) realWords++;
                        break;
                    }
                }
                if (strict && realWords >= 2) return true;
            }
            return strict && realWords >= 2;
        }

        /// <summary>
        /// 代码/终端/日志行识别：代码编辑器、控制台输出一律不值得翻译。
        /// 标记取自然语言中几乎不出现的字符与模式。
        /// </summary>
        public static bool LooksLikeCode(string t)
        {
            if (t.IndexOfAny(new[] { ';', '{', '}', '`' }) >= 0) return true;
            if (t.Contains("=>") || t.Contains("::") || t.Contains("->")) return true;
            if (FuncCallRegex().IsMatch(t)) return true;      // foo(
            if (SnakeCaseRegex().IsMatch(t)) return true;     // foo_bar
            if (LogPrefixRegex().IsMatch(t)) return true;     // error/traceback/at ...
            if (TimestampRegex().IsMatch(t)) return true;     // [14:23:45]
            // 符号占比（不计空格）过高 → 代码/表格式内容
            int sym = 0, len = 0;
            foreach (var c in t)
            {
                if (char.IsWhiteSpace(c)) continue;
                len++;
                if (char.IsPunctuation(c) || char.IsSymbol(c)) sym++;
            }
            return len > 0 && sym * 100 > len * 35;
        }

        /// <summary>缓存键：去多余空白 + 小写化。让 "Settings" 与 "SETTINGS" 共享一次翻译。</summary>
        public static string NormalizeKey(string text) =>
            Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();
    }
}
