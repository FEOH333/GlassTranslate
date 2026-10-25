using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GlassTranslate.Core.Providers
{
    /// <summary>
    /// Anthropic Messages API。
    /// 成本点：system 块带 cache_control=ephemeral → 显式提示词缓存，
    /// 静态前缀命中部分只按 10% 计价，且 5 分钟内滚动续期。
    /// </summary>
    public sealed class AnthropicProvider : ITranslationProvider
    {
        /// <summary>全进程共享；鉴权按请求注入。</summary>
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        { Timeout = TimeSpan.FromSeconds(120) };

        private static readonly int[] BackoffMs = { 900, 2600 };

        private readonly string _endpoint;
        private readonly string _model;
        private readonly string _apiKey;
        public string Name => "Anthropic";

        public AnthropicProvider(string baseUrl, string apiKey, string model)
        {
            _model = string.IsNullOrWhiteSpace(model) ? "claude-haiku-5-5" : model;
            _apiKey = apiKey;
            _endpoint = NormalizeMessagesUrl(baseUrl);
        }

        internal static string NormalizeMessagesUrl(string baseUrl)
        {
            var b = baseUrl.Trim().TrimEnd('/');
            if (b.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase)) return b;
            if (b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return b + "/messages";
            return b + "/v1/messages";
        }

        public async Task StreamTranslateAsync(IReadOnlyList<string> lines, LineEventHandler onLine, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _model,
                stream = true,
                max_tokens = Math.Min(1024 + 160 * lines.Count, 16000),
                system = new object[]
                {
                    new
                    {
                        type = "text",
                        text = PromptCatalog.SystemPrompt,
                        cache_control = new { type = "ephemeral" }
                    }
                },
                messages = new object[]
                {
                    new { role = "user", content = PromptCatalog.BuildUserPrompt(lines) }
                }
            });
            HttpResponseMessage resp;
            int attempt = 0;
            while (true)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                try
                {
                    resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch (HttpRequestException) when (attempt < BackoffMs.Length)
                {
                    await Task.Delay(BackoffMs[attempt++] + Random.Shared.Next(250), ct);
                    continue;
                }
                if (resp.IsSuccessStatusCode) break;
                var err = await resp.Content.ReadAsStringAsync(ct);
                int code = (int)resp.StatusCode;
                resp.Dispose();
                if (attempt < BackoffMs.Length && (code == 429 || code >= 500))
                {
                    await Task.Delay(BackoffMs[attempt++] + Random.Shared.Next(250), ct);
                    continue;
                }
                throw new ProviderException($"[{Name}] HTTP {code}: {Short(err)}");
            }
            using (resp)
            {
                var parser = new StreamLineParser(onLine);
            await SseClient.ReadAsync(resp, async payload =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "error")
                    {
                        var msg = root.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
                            ? m.GetString() : payload;
                        throw new ProviderException($"[{Name}] {msg}");
                    }
                    if (type == "content_block_delta" &&
                        root.TryGetProperty("delta", out var d) &&
                        d.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta" &&
                        d.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        var delta = text.GetString();
                        if (!string.IsNullOrEmpty(delta)) parser.Feed(delta!);
                    }
                }
                catch (ProviderException) { throw; }
                catch { }
                await Task.CompletedTask;
            }, ct);
                parser.Finish();
            }
        }

        public async Task<(bool Ok, string Message)> TestAsync(CancellationToken ct)
        {
            try
            {
                string final = "";
                await StreamTranslateAsync(new[] { "Hello, world!" }, (i, delta, complete, finalText) =>
                {
                    if (i == 1 && complete && finalText != null) final = finalText;
                }, ct);
                return string.IsNullOrEmpty(final)
                    ? (false, "请求成功但没有返回译文")
                    : (true, $"连通正常：Hello, world! → {final}");
            }
            catch (Exception ex) { return (false, ProviderException.Describe(ex)); }
        }

        private static string Short(string s) => s.Length > 300 ? s[..300] : s;
    }
}
