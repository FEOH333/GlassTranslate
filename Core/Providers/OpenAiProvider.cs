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
    /// OpenAI Chat Completions 协议（兼容 DeepSeek/通义/Kimi/GLM/各类中转）。
    /// 工程要点：
    ///  - 进程级共享 HttpClient（连接池复用，修复每批 new HttpClient 的 socket 泄漏）
    ///  - 流式开始前的 429/5xx/网络错误指数退避重试（最多 3 次尝试）
    ///  - 通义系模型发送 enable_thinking:false（Qwen3 默认开思考，翻译场景首字延迟×N）
    ///  - 正文内联 &lt;think&gt; 思考段过滤
    ///  - System Prompt 固定前缀 &gt;1024 token → OpenAI 自动前缀缓存（输入 5 折）
    /// </summary>
    public sealed class OpenAiProvider : ITranslationProvider
    {
        /// <summary>全进程共享；鉴权按请求注入，不落在 DefaultRequestHeaders。</summary>
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        { Timeout = TimeSpan.FromSeconds(120) };

        private static readonly int[] BackoffMs = { 900, 2600 };

        private readonly string _endpoint;
        private readonly string _model;
        private readonly string _apiKey;
        private readonly bool _noThinking;
        public string Name => "OpenAI";

        public OpenAiProvider(string baseUrl, string apiKey, string model, bool noThinking = false)
        {
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-5.2" : model;
            _apiKey = apiKey;
            _noThinking = noThinking;
            _endpoint = NormalizeChatUrl(baseUrl);
        }

        internal static string NormalizeChatUrl(string baseUrl)
        {
            var b = baseUrl.Trim().TrimEnd('/');
            if (b.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return b;
            try
            {
                var path = new Uri(b).AbsolutePath;
                if (path.Length > 1) return b + "/chat/completions";
            }
            catch { }
            return b + "/v1/chat/completions";
        }

        public async Task StreamTranslateAsync(IReadOnlyList<string> lines, LineEventHandler onLine, CancellationToken ct)
        {
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["stream"] = true,
                ["messages"] = new object[]
                {
                    new { role = "system", content = PromptCatalog.SystemPrompt },
                    new { role = "user",   content = PromptCatalog.BuildUserPrompt(lines) }
                }
            };
            if (_noThinking) payload["enable_thinking"] = false;
            var body = JsonSerializer.Serialize(payload);

            HttpResponseMessage resp;
            int attempt = 0;
            while (true)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
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
            UsageStats.AddApiCall();

            using (resp)
            {
                var parser = new StreamLineParser(onLine);
                var stripper = new ThinkTagStripper();
                await SseClient.ReadAsync(resp, async payloadStr =>
                {
                    if (payloadStr == "[DONE]") return;
                    string? delta = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(payloadStr);
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
                    if (!string.IsNullOrEmpty(delta))
                    {
                        var clean = stripper.Filter(delta!);
                        if (clean.Length > 0) parser.Feed(clean);
                    }
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
