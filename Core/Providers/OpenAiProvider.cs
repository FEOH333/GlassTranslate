using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GlassTranslate.Core.Providers
{
    /// <summary>
    /// OpenAI Chat Completions（兼容所有 OpenAI 协议中转/网关）。
    /// 成本点：SystemPrompt 为固定前缀且 >1024 token → 自动命中 OpenAI 前缀缓存（输入 5 折）。
    /// </summary>
    public sealed class OpenAiProvider : ITranslationProvider
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _model;
        public string Name => "OpenAI";

        public OpenAiProvider(string baseUrl, string apiKey, string model)
        {
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _endpoint = NormalizeChatUrl(baseUrl);
        }

        internal static string NormalizeChatUrl(string baseUrl)
        {
            var b = baseUrl.Trim().TrimEnd('/');
            if (b.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return b;
            if (b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return b + "/chat/completions";
            return b + "/v1/chat/completions";
        }

        public async Task StreamTranslateAsync(IReadOnlyList<string> lines, LineEventHandler onLine, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _model,
                stream = true,
                messages = new object[]
                {
                    new { role = "system", content = PromptCatalog.SystemPrompt },
                    new { role = "user",   content = PromptCatalog.BuildUserPrompt(lines) }
                }
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new ProviderException($"[{Name}] HTTP {(int)resp.StatusCode}: {Short(err)}");
            }

            var parser = new StreamLineParser(onLine);
            await SseClient.ReadAsync(resp, async payload =>
            {
                if (payload == "[DONE]") return;
                string? delta = null;
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var e)) throw new ProviderException($"[{Name}] {e}");
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var c = choices[0];
                        if (c.TryGetProperty("delta", out var d) &&
                            d.TryGetProperty("content", out var content) &&
                            content.ValueKind == JsonValueKind.String)
                            delta = content.GetString();
                    }
                }
                catch (ProviderException) { throw; }
                catch { /* 单块解析失败不影响整体 */ }
                if (!string.IsNullOrEmpty(delta)) parser.Feed(delta!);
                await Task.CompletedTask;
            }, ct);
            parser.Finish();
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
