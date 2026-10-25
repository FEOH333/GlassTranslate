using System;

namespace GlassTranslate.Config
{
    /// <summary>
    /// 无头配置：`GlassTranslate.exe --config provider=qwen key=sk-xx url=https://… model=qwen3.8-flash realtime=1`
    /// 用于脚本/CI 预置配置，或 CLI 快速改参（改完直接启动即可生效）。
    /// </summary>
    internal static class HeadlessConfig
    {
        public static void Apply(string[] args)
        {
            var cfg = AppConfig.Load();
            foreach (var a in args)
            {
                int eq = a.IndexOf('=');
                if (eq <= 0) continue;
                var k = a[..eq].ToLowerInvariant();
                var v = a[(eq + 1)..];
                switch (k)
                {
                    case "provider": cfg.ProviderId = v; break;
                    case "key": cfg.ApiKey = v; break;
                    case "url": cfg.BaseUrl = v; break;
                    case "model": cfg.Model = v; break;
                    case "realtime": cfg.RealtimeEnabled = v is "1" or "true"; break;
                    case "hotkey": cfg.HotkeyModeEnabled = v is "1" or "true"; break;
                    case "interval":
                        if (double.TryParse(v, out var ms)) cfg.OcrIntervalMs = Math.Clamp(ms, 800, 5000);
                        break;
                }
            }
            cfg.Save();
            try { Console.WriteLine($"config saved: provider={cfg.ProviderId} model={cfg.EffectiveModel} url={cfg.EffectiveBaseUrl}"); }
            catch { }
        }
    }
}
