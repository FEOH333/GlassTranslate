using System.Linq;

namespace GlassTranslate.Config
{
    public sealed record ProviderPreset(
        string Id,
        string DisplayName,
        string Protocol,        // "openai" | "anthropic"
        string BaseUrl,
        string[] Models,
        string DefaultModel,
        string KeyHint);

    /// <summary>
    /// 内置服务商预设（模型与端点以 2026-10 各家官方文档为准）。
    /// OpenAI / Anthropic 只是两种"协议"：DeepSeek、通义千问（阿里云百炼）、Kimi（月之暗面）、
    /// GLM（智谱）官方 API 均为 OpenAI 协议兼容，直接复用 OpenAiProvider；
    /// Anthropic 协议用于 Anthropic 官方与中转。模型名均可自由编辑（下拉只是预设）。
    /// </summary>
    public static class ProviderPresets
    {
        public static readonly ProviderPreset[] All =
        {
            new("deepseek", "DeepSeek 深度求索", "openai",
                "https://api.deepseek.com",
                new[] { "deepseek-v4-flash", "deepseek-v4-pro" },
                "deepseek-v4-flash",
                "platform.deepseek.com 申请 Key"),

            new("qwen", "通义千问 · 阿里云百炼", "openai",
                "https://dashscope.aliyuncs.com/compatible-mode/v1",
                new[] { "qwen3.8-flash", "qwen3.7-plus", "qwen3.8-max", "qwen3.7-max" },
                "qwen3.8-flash",
                "百炼控制台 bailian.console.aliyun.com 申请 Key"),

            new("kimi", "Kimi · 月之暗面 Moonshot", "openai",
                "https://api.moonshot.cn/v1",
                new[] { "kimi-k2.6", "kimi-k3", "kimi-k2.7-code" },
                "kimi-k2.6",
                "platform.kimi.com 申请 Key（k2.6 默认不思考、出字快；k3 恒定深度思考、较慢）"),

            new("glm", "GLM · 智谱 BigModel", "openai",
                "https://open.bigmodel.cn/api/paas/v4",
                new[] { "glm-5.3-flash", "glm-4.7-flash", "glm-5.3-flashx", "glm-5.3", "glm-5" },
                "glm-5.3-flash",
                "bigmodel.cn 申请 Key（glm-4.7-flash 免费）"),

            new("openai", "OpenAI（官方 / 中转）", "openai",
                "https://api.openai.com/v1",
                new[] { "gpt-5.2", "chat-latest", "gpt-5.2-codex" },
                "gpt-5.2",
                "platform.openai.com 申请 Key"),

            new("anthropic", "Anthropic（官方 / 中转）", "anthropic",
                "https://api.anthropic.com",
                new[] { "claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5" },
                "claude-haiku-5-5",
                "console.anthropic.com 申请 Key"),

            new("custom-openai", "自定义 · OpenAI 协议", "openai",
                "", new[] { "" }, "",
                "任何 OpenAI 协议服务 / 中转网关"),

            new("custom-anthropic", "自定义 · Anthropic 协议", "anthropic",
                "", new[] { "" }, "",
                "任何 Anthropic 协议服务 / 中转网关"),
        };

        public static ProviderPreset ById(string? id) =>
            All.FirstOrDefault(p => p.Id == id) ?? All[0];
    }
}
