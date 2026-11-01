using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GlassTranslate.Config
{
    /// <summary>
    /// 应用配置（v2.0 纯本地版，无任何 API/密钥字段）。
    /// 读取对旧版本的多余字段（Provider/ApiKey 等）自动忽略丢弃。
    /// </summary>
    public class AppConfig
    {
        public double OcrIntervalMs { get; set; } = 1500;
        /// <summary>OCR 引擎：auto（优先 RapidOCR，回退 Windows）/ rapid / windows。</summary>
        public string OcrEngine { get; set; } = "auto";
        public string Hotkey { get; set; } = "Ctrl+Alt+T";
        public bool RealtimeEnabled { get; set; }
        public bool HotkeyModeEnabled { get; set; }
        public bool ShowBall { get; set; } = true;
        public double BallLeft { get; set; } = double.NaN;
        public double BallTop { get; set; } = double.NaN;

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassTranslate");
        private static string CfgPath => Path.Combine(Dir, "config.json");
        public static string CachePath => Path.Combine(Dir, "translation-cache.json");

        // AllowNamedFloatingPointLiterals：BallLeft/Top 未拖动过时是 NaN，需要按字面量读写
        private static readonly JsonSerializerOptions J = new()
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(CfgPath))
                {
                    var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(CfgPath), J);
                    if (loaded != null) return loaded;
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
                File.WriteAllText(CfgPath, JsonSerializer.Serialize(this, J));
            }
            catch { }
        }
    }
}
