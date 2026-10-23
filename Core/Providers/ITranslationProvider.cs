using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GlassTranslate.Core.Providers
{
    /// <summary>
    /// 流式行事件：index 从 1 开始；
    /// delta 为本次新增的译文片段；lineComplete=true 时 finalText 为该行最终译文。
    /// </summary>
    /// <param name="index">行号（从 1 开始）</param>
    /// <param name="delta">增量译文（可为 null）</param>
    /// <param name="lineComplete">该行是否输出完毕</param>
    /// <param name="finalText">行完成时的完整译文</param>
    public delegate void LineEventHandler(int index, string? delta, bool lineComplete, string? finalText);

    public interface ITranslationProvider
    {
        string Name { get; }

        /// <summary>流式批量翻译：输入编号行，通过 onLine 回调持续输出增量。</summary>
        Task StreamTranslateAsync(IReadOnlyList<string> lines, LineEventHandler onLine, CancellationToken ct);

        /// <summary>连通性/鉴权自检（翻译一句 Hello, world!）。</summary>
        Task<(bool Ok, string Message)> TestAsync(CancellationToken ct);
    }

    public sealed class ProviderException : Exception
    {
        public ProviderException(string message) : base(message) { }

        public static string Describe(Exception ex) => ex switch
        {
            ProviderException p => p.Message,
            HttpRequestException => "网络错误（检查网络/代理/Base URL）：" + ex.Message,
            TaskCanceledException => "请求超时，请检查网络或 Base URL",
            _ => ex.Message
        };
    }
}
