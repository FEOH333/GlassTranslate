using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GlassTranslate.Core.Providers
{
    /// <summary>通用 SSE 读取器：逐行回调 "data:" 负载。</summary>
    internal static class SseClient
    {
        public static async Task ReadAsync(HttpResponseMessage resp, Func<string, Task> onData, CancellationToken ct)
        {
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (line.Length == 0 || line.StartsWith(':')) continue;
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    await onData(line.Substring(5).TrimStart());
            }
        }
    }
}
