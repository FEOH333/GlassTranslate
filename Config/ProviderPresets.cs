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
    /// 内置服务商预设。OpenAI / Anthropic 只是两种“协议”，兼容这两种协议的官方服务都内置支持：
    /// DeepSeek、通义千问（阿里云百炼）、Kimi（月之暗面）、GLM（智谱）均为 OpenAI 协议兼容，
    /// 直接复用 OpenAiProvider；Anthropic 协议用于 Anthropic 官方与中转。
    /// 模型名均可自由编辑（下拉只是预设），地址、模型信息以 2026-10 官方文档为准。
    /// </summary>
    public static class ProviderPresets
    {
        public static readonly ProviderPreset[] All =
        {
            new("deepseek", "DeepSeek 深度求索", "openai",
                "https://api.deepseek.com",
                new[] { "deepseek-chat", "deepseek-reasoner", "deepseek-flash" },
                "deepseek-chat",
                "platform.deepseek.com 申请 Key"),

            new("qwen", "通义千问 · 阿里云百炼", "openai",
                "https://dashscope.aliyuncs.com/compatible-mode/v1",
                new[] { "qwen-flash", "qwen-plus", "qwen-turbo", "qwen-max" },
                "qwen-flash",
                "百炼控制台 bailian.console.aliyun.com 申请 Key"),

            new("kimi", "Kimi · 月之暗面 Moonshot", "openai",
                "https://api.moonshot.cn/v1",
                new[] { "kimi-k2-turbo-preview", "kimi-k2-0905-preview", "kimi-k2-0711-preview", "moonshot-v1-8k" },
                "kimi-k2-turbo-preview",
                "platform.moonshot.cn 申请 Key"),

            new("glm", "GLM · 智谱 BigModel", "openai",
                "https://open.bigmodel.cn/api/paas/v4",
                new[] { "glm-4.5-flash", "glm-4.7-flash", "glm-4-flash-250414", "glm-4.6", "glm-4.5-air" },
                "glm-4.5-flash",
                "bigmodel.cn 申请 Key（Flash 系列免费）"),

            new("openai", "OpenAI（官方 / 中转）", "openai",
                "https://api.openai.com/v1",
                new[] { "gpt-4o-mini", "gpt-4.1-mini", "gpt-4.1", "gpt-4o" },
                "gpt-4o-mini",
                "platform.openai.com 申请 Key"),

            new("anthropic", "Anthropic（官方 / 中转）", "anthropic",
                "https://api.anthropic.com",
                new[] { "claude-3-5-haiku-latest", "claude-haiku-4-5", "claude-sonnet-4-5" },
                "claude-3-5-haiku-latest",
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
