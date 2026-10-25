using System;
using System.Text.RegularExpressions;

namespace GlassTranslate.Core.Providers
{
    /// <summary>
    /// 解析模型流式输出的 "N|译文" 行协议：把原始文本块流转换为
    /// “第 N 行又多了几个字”的增量事件——逐字打字机效果的来源。
    ///
    /// 状态机：每行先确认行头（N|），行头确认后剩余内容按"已发出字符数"做增量；
    /// 行头未确认时只接受纯数字（行头可能被 chunk 劈开），否则整行判为非协议行丢弃。
    /// </summary>
    public sealed class StreamLineParser
    {
        private static readonly Regex Head = new(@"^\s*(\d{1,3})\s*[|:：]", RegexOptions.Compiled);

        private const int Skipped = int.MinValue;   // 非协议行（寒暄/代码围栏等）

        private readonly LineEventHandler _emit;
        private string _tail = "";      // 当前行尚未换行的原始内容
        private int _cur = -1;          // 当前行号；-1=行头未确认；Skipped=丢弃行
        private int _emitted;           // 当前行已发出的字符数（相对剥头后内容）

        public StreamLineParser(LineEventHandler emit) => _emit = emit;

        public void Feed(string chunk)
        {
            _tail += chunk;

            // 行首状态：尝试确认行头（整段一次，成功后同步裁剪 _tail，保证 _emitted 参照一致）
            if (_cur == -1)
            {
                var m = Head.Match(_tail);
                if (m.Success)
                {
                    _cur = int.Parse(m.Groups[1].Value);
                    _tail = _tail[m.Length..];
                    _emitted = 0;
                }
                else if (!IsPartialHeadCandidate(_tail))
                {
                    _cur = Skipped;
                }
            }

            int nl;
            while ((nl = _tail.IndexOf('\n')) >= 0)
            {
                var line = _tail[..nl];
                _tail = _tail[(nl + 1)..];

                // 下一行的行首处理（仅当上一行结束时回到 -1）
                if (_cur == -1)
                {
                    var m = Head.Match(line);
                    if (m.Success)
                    {
                        _cur = int.Parse(m.Groups[1].Value);
                        line = line[m.Length..];
                        _emitted = 0;
                    }
                    else _cur = Skipped;
                }

                if (_cur != Skipped) EmitComplete(line);

                // 行结束：回到行首状态；_tail 里可能已有下一行的开头
                _cur = -1;
                _emitted = 0;
                if (_tail.Length > 0)
                {
                    var m2 = Head.Match(_tail);
                    if (m2.Success)
                    {
                        _cur = int.Parse(m2.Groups[1].Value);
                        _tail = _tail[m2.Length..];
                        _emitted = 0;
                    }
                    else if (!IsPartialHeadCandidate(_tail))
                    {
                        _cur = Skipped;
                    }
                }
            }

            // 不完整尾行：只增量，不完结
            if (_tail.Length > 0 && _cur >= 0) EmitPartial(_tail);
        }

        public void Finish()
        {
            if (_tail.Length == 0) return;
            if (_cur == -1)
            {
                var m = Head.Match(_tail);
                if (m.Success)
                {
                    _cur = int.Parse(m.Groups[1].Value);
                    _tail = _tail[m.Length..];
                    _emitted = 0;
                }
                else _cur = Skipped;
            }
            if (_cur >= 0) EmitComplete(_tail);
            _tail = "";
            _cur = -1;
            _emitted = 0;
        }

        private void EmitPartial(string content)
        {
            // 暂存尾部高位代理项，等配对字符到达再输出（防止 emoji 被劈成两半）
            if (char.IsHighSurrogate(content[^1])) content = content[..^1];
            if (content.Length > _emitted)
            {
                _emit(_cur, content[_emitted..], false, null);
                _emitted = content.Length;
            }
        }

        private void EmitComplete(string content)
        {
            if (content.Length > _emitted)
            {
                _emit(_cur, content[_emitted..], false, null);
                _emitted = content.Length;
            }
            _emit(_cur, null, true, content.Trim());
        }

        /// <summary>全数字/空白：可能是被 chunk 劈开的行头（如 "12"），等待下一段。</summary>
        private static bool IsPartialHeadCandidate(string s)
        {
            if (s.Length == 0) return true;
            foreach (var c in s)
                if (!char.IsDigit(c) && c != ' ' && c != '\t') return false;
            return true;
        }
    }
}
