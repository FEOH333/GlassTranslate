using System;

namespace GlassTranslate.Config
{
    /// <summary>
    /// 无头配置：`GlassTranslate.exe --config realtime=1 interval=1200 engine=rapid hotkey=1`
    /// （v2.0：仅本地行为参数，无任何 API/密钥项。）
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
                    case "realtime": cfg.RealtimeEnabled = v is "1" or "true"; break;
                    case "hotkey": cfg.HotkeyModeEnabled = v is "1" or "true"; break;
                    case "engine":
                        cfg.OcrEngine = v is "auto" or "rapid" or "windows" ? v : "auto";
                        break;
                    case "interval":
                        if (double.TryParse(v, out var ms)) cfg.OcrIntervalMs = Math.Clamp(ms, 800, 5000);
                        break;
                }
            }
            cfg.Save();
            try { Console.WriteLine($"config saved: engine={cfg.OcrEngine} interval={cfg.OcrIntervalMs}ms realtime={cfg.RealtimeEnabled}"); }
            catch { }
        }
    }
}
