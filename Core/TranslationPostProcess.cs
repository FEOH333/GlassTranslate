using System;
using System.Text.RegularExpressions;

namespace GlassTranslate.Core
{
    /// <summary>
    /// 本地翻译引擎输出的后处理：修正常见瑕疵、拦截可疑译文。
    ///  - 半角标点在中文语境转全角（","→"，" 等）
    ///  - 去标点前空格、折叠连续重复标点（"! !"→"！"、"！！"→"！"）
    ///  - 退化检测（单 token 死循环，如"公约公约公约…"）→ 判为失败
    ///  - 无任何汉字 → 判为失败（防止把英文原样当译文）
    /// </summary>
    public static partial class TranslationPostProcess
    {
        [GeneratedRegex(@"\s+([,!;:?!，；：。？])")]
        private static partial Regex SpaceBeforePunct();

        [GeneratedRegex(@"(?<=[\u4e00-\u9fff]),")]
        private static partial Regex CommaAfterCjk();

        [GeneratedRegex(@"(?<=[\u4e00-\u9fff])!")]
        private static partial Regex BangAfterCjk();

        [GeneratedRegex(@"(?<=[\u4e00-\u9fff]);")]
        private static partial Regex SemiAfterCjk();

        [GeneratedRegex(@"(?<=[\u4e00-\u9fff]):")]
        private static partial Regex ColonAfterCjk();

        [GeneratedRegex(@"(?<=[\u4e00-\u9fff])\?")]
        private static partial Regex QuestAfterCjk();

        [GeneratedRegex(@"[!\uFF01]+")]
        private static partial Regex BangRun();

        [GeneratedRegex(@"[?\uFF1F]+")]
        private static partial Regex QuestRun();

        [GeneratedRegex(@"[\u3002]+")]
        private static partial Regex DotRun();

        [GeneratedRegex(@"[,\uFF0C]+")]
        private static partial Regex CommaRun();

        /// <summary>清洗译文；可疑/无效返回 null（调用方按翻译失败处理）。</summary>
        public static string? Clean(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var t = s.Trim();

            // 必须含汉字
            bool hasCjk = false;
            foreach (var c in t)
            {
                if (c >= 0x4e00 && c <= 0x9fff) { hasCjk = true; break; }
            }
            if (!hasCjk) return null;

            // 退化检测：去重字符占比过低 = 生成死循环
            int distinct = 0;
            var seen = new HashSet<char>();
            foreach (var c in t)
            {
                if (seen.Add(c)) distinct++;
            }
            if (t.Length >= 12 && distinct < t.Length * 0.34) return null;

            t = SpaceBeforePunct().Replace(t, "$1");
            t = CommaAfterCjk().Replace(t, "，");
            t = BangAfterCjk().Replace(t, "！");
            t = SemiAfterCjk().Replace(t, "；");
            t = ColonAfterCjk().Replace(t, "：");
            t = QuestAfterCjk().Replace(t, "？");
            t = BangRun().Replace(t, "！");
            t = QuestRun().Replace(t, "？");
            t = DotRun().Replace(t, "。");
            t = CommaRun().Replace(t, "，");
            return t.Trim();
        }
    }
}
