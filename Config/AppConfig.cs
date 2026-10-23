using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlassTranslate.Platform;

namespace GlassTranslate.Config
{
    /// <summary>
    /// 应用配置。磁盘格式为 JSON，API Key 以 DPAPI 密文落盘（ApiKeyEnc），
    /// 明文只存在于内存（ApiKey 带 [JsonIgnore]）。
    /// </summary>
    public class AppConfig
    {
        public string Provider { get; set; } = "openai";   // openai | anthropic
        public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string OpenAiModel { get; set; } = "gpt-4o-mini";
        public string AnthropicBaseUrl { get; set; } = "https://api.anthropic.com";
        public string AnthropicModel { get; set; } = "claude-3-5-haiku-latest";
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

        public string ActiveBaseUrl => Provider == "anthropic" ? AnthropicBaseUrl : OpenAiBaseUrl;
        public string ActiveModel => Provider == "anthropic" ? AnthropicModel : OpenAiModel;

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassTranslate");
        private static string CfgPath => Path.Combine(Dir, "config.json");
        public static string CachePath => Path.Combine(Dir, "translation-cache.json");
        private static readonly JsonSerializerOptions J = new() { WriteIndented = true };

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(CfgPath))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(CfgPath));
                    if (cfg != null)
                    {
                        if (!string.IsNullOrEmpty(cfg.ApiKeyEnc))
                            cfg.ApiKey = DpApi.Unprotect(cfg.ApiKeyEnc);
                        return cfg;
                    }
                }
            }
            catch { }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                ApiKeyEnc = string.IsNullOrEmpty(ApiKey) ? "" : DpApi.Protect(ApiKey);
                File.WriteAllText(CfgPath, JsonSerializer.Serialize(this, J));
            }
            catch { }
        }
    }
}
