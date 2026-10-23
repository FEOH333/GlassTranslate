using System;
using System.Text.RegularExpressions;

namespace GlassTranslate.Core.Providers
{
    /// <summary>
    /// 解析模型流式输出的 "N|译文" 行协议：把原始文本块流转换为
    /// “第 N 行又多了几个字”的增量事件——这正是逐字打字机效果的来源。
    /// 网络每次到达的块大小不一，解析器负责保证：不重、不漏、不串行。
    /// </summary>
    public sealed class StreamLineParser
    {
        private static readonly Regex Head = new(@"^\s*(\d{1,3})\s*[|:：]", RegexOptions.Compiled);

        private readonly LineEventHandler _emit;
        private string _lineBuf = "";
        private int _emitted;     // 当前行已发出的字符数
        private int _cur = -1;    // 当前行号；-1 = 尚未识别到行头
        private bool _skip;       // 当前行不是协议格式，整行吞掉

        public StreamLineParser(LineEventHandler emit) => _emit = emit;

        public void Feed(string chunk)
        {
            _lineBuf += chunk;
            int nl;
            while ((nl = _lineBuf.IndexOf('\n')) >= 0)
            {
                var line = _lineBuf[..nl];
                _lineBuf = _lineBuf[(nl + 1)..];
                Handle(line, true);
                ResetLineState();
            }
            if (_lineBuf.Length > 0)
                Handle(_lineBuf, false);
        }

        public void Finish()
        {
            if (_lineBuf.Length > 0)
            {
                Handle(_lineBuf, true);
                _lineBuf = "";
                ResetLineState();
            }
        }

        private void ResetLineState() { _cur = -1; _emitted = 0; _skip = false; }

        private void Handle(string raw, bool completeLine)
        {
            if (_cur == -1 && !_skip)
            {
                var m = Head.Match(raw);
                if (m.Success)
                {
                    _cur = int.Parse(m.Groups[1].Value);
                    raw = raw[m.Length..];
                    _emitted = 0;
                }
                else
                {
                    var t = raw.Trim();
                    if (t.Length == 0) return;
                    // 可能是还没到齐的行头（纯数字）
                    if (!completeLine && IsAllDigits(t)) return;
                    _skip = true; // 代码围栏、寒暄等非协议行：整行丢弃
                    return;
                }
            }
            if (_skip || _cur == -1) return;

            var content = raw;
            // 暂存尾部高位代理项，等配对字符到达再输出（防止 emoji 被劈成两半）
            if (!completeLine && content.Length > _emitted && char.IsHighSurrogate(content[^1]))
                content = content[..^1];

            if (content.Length > _emitted)
            {
                _emit(_cur, content[_emitted..], false, null);
                _emitted = content.Length;
            }
            if (completeLine)
            {
                var final = content.Trim();
                _emit(_cur, null, true, final.Length > 0 ? final : content);
            }
        }

        private static bool IsAllDigits(string s)
        {
            foreach (var c in s) if (!char.IsDigit(c)) return false;
            return true;
        }
    }
}
