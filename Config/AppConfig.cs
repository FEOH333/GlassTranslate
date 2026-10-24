using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlassTranslate.Platform;

namespace GlassTranslate.Config
{
    /// <summary>
    /// 应用配置。磁盘为 JSON；API Key 以 DPAPI 密文落盘（ApiKeyEnc），明文只在内存。
    /// v1.1 起以 ProviderId + BaseUrl + Model 描述服务商（见 ProviderPresets），
    /// 读取时自动迁移 v1.0 的旧双字段格式。
    /// </summary>
    public class AppConfig
    {
        public string ProviderId { get; set; } = "deepseek";
        public string BaseUrl { get; set; } = "";
        public string Model { get; set; } = "";
        public string ApiKeyEnc { get; set; } = "";
        public double OcrIntervalMs { get; set; } = 1500;
        public string Hotkey { get; set; } = "Ctrl+Alt+T";
        public bool RealtimeEnabled { get; set; }
        public bool HotkeyModeEnabled { get; set; }
        public bool ShowBall { get; set; } = true;
        public double BallLeft { get; set; } = double.NaN;
        public double BallTop { get; set; } = double.NaN;

        [JsonIgnore]
        public string ApiKey { get; set; } = "";

        /// <summary>旧版字段（仅用于读取迁移，保存时清除）。</summary>
        public string? Provider { get; set; }
        public string? OpenAiBaseUrl { get; set; }
        public string? OpenAiModel { get; set; }
        public string? AnthropicBaseUrl { get; set; }
        public string? AnthropicModel { get; set; }

        public ProviderPreset Preset => ProviderPresets.ById(ProviderId);
        public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? Preset.BaseUrl : BaseUrl.Trim();
        public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? Preset.DefaultModel : Model.Trim();

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassTranslate");
        private static string CfgPath => Path.Combine(Dir, "config.json");
        public static string CachePath => Path.Combine(Dir, "translation-cache.json");
        private static readonly JsonSerializerOptions J = new() { WriteIndented = true };

        public static AppConfig Load()
        {
            AppConfig cfg = new();
            try
            {
                if (File.Exists(CfgPath))
                {
                    var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(CfgPath));
                    if (loaded != null) cfg = loaded;
                }
            }
            catch { }

            MigrateLegacy(cfg);

            if (!string.IsNullOrEmpty(cfg.ApiKeyEnc))
                cfg.ApiKey = DpApi.Unprotect(cfg.ApiKeyEnc);
            return cfg;
        }

        /// <summary>v1.0 旧格式（OpenAI/Anthropic 双字段）→ ProviderId + BaseUrl + Model。</summary>
        private static void MigrateLegacy(AppConfig cfg)
        {
            if (string.IsNullOrEmpty(cfg.Provider)) return;
            if (cfg.Provider == "anthropic")
            {
                cfg.ProviderId = "anthropic";
                cfg.BaseUrl = cfg.AnthropicBaseUrl ?? "";
                cfg.Model = cfg.AnthropicModel ?? "";
            }
            else
            {
                cfg.ProviderId = "openai";
                cfg.BaseUrl = cfg.OpenAiBaseUrl ?? "";
                cfg.Model = cfg.OpenAiModel ?? "";
            }
            cfg.Provider = null;
            cfg.OpenAiBaseUrl = cfg.OpenAiModel = null;
            cfg.AnthropicBaseUrl = cfg.AnthropicModel = null;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                ApiKeyEnc = string.IsNullOrEmpty(ApiKey) ? "" : DpApi.Protect(ApiKey);
                // 保存时清掉旧字段，避免新旧并存
                Provider = null;
                OpenAiBaseUrl = OpenAiModel = AnthropicBaseUrl = AnthropicModel = null;
                File.WriteAllText(CfgPath, JsonSerializer.Serialize(this, J));
            }
            catch { }
        }
    }
}
