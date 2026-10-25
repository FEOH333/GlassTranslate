namespace GlassTranslate.Core.Providers
{
    /// <summary>
    /// 过滤部分模型在正文里内联输出的 &lt;think&gt;…&lt;/think&gt; 思考段
    /// （Qwen/GLM 某些网关即使关了 thinking 开关也会走正文输出思考）。
    /// 有状态：跨 chunk 维护"是否处于 think 段内"，并保护被 chunk 边界劈开的标签。
    /// </summary>
    public sealed class ThinkTagStripper
    {
        private const string Open = "<think>", Close = "</think>";
        private bool _inThink;
        private string _pending = "";

        public string Filter(string chunk)
        {
            _pending += chunk;
            var output = "";
            while (true)
            {
                if (_inThink)
                {
                    int end = _pending.IndexOf(Close, System.StringComparison.Ordinal);
                    if (end < 0)
                    {
                        // 保留尾部，防止 "</think>" 被劈开
                        if (_pending.Length > Close.Length)
                            _pending = _pending[^Close.Length..];
                        return output;
                    }
                    _pending = _pending[(end + Close.Length)..];
                    _inThink = false;
                }
                else
                {
                    int start = _pending.IndexOf(Open, System.StringComparison.Ordinal);
                    if (start < 0)
                    {
                        int hold = HoldLength(_pending);
                        output += _pending[..^hold];
                        _pending = _pending[^hold..];
                        return output;
                    }
                    output += _pending[..start];
                    _pending = _pending[(start + Open.Length)..];
                    _inThink = true;
                }
            }
        }

        /// <summary>尾部若恰好是 "&lt;think" 的前缀（被劈开），扣下等下个 chunk。</summary>
        private static int HoldLength(string s)
        {
            int max = Math.Min(Open.Length - 1, s.Length);
            for (int hold = max; hold > 0; hold--)
            {
                if (Open.StartsWith(s[^hold..], System.StringComparison.Ordinal))
                    return hold;
            }
            return 0;
        }
    }
}
