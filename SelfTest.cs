using System;
using System.IO;
using System.Threading;
using GlassTranslate.Core;
using GlassTranslate.Core.Providers;
using GlassTranslate.Platform;

namespace GlassTranslate
{
    /// <summary>
    /// 无 GUI 自检：--selftest（控制台/日志输出捕获、OCR、过滤器、缓存）
    /// 加 --api 同时测试已配置的翻译接口。输出写入 exe 旁 selftest.log。
    /// </summary>
    internal static class SelfTest
    {
        public static void Run(bool withApi)
        {
            // 整个自检放到 MTA 线程池线程跑：WinRT 的 RecognizeAsync 在 STA 主线程上
            // 同步等待会死锁（WPF 调度器被阻塞，完成回调无法回泵）。
            System.Threading.Tasks.Task.Run(() => RunCore(withApi)).Wait();
        }

        private static void RunCore(bool withApi)
        {
            var sb = new System.Text.StringBuilder();
            void W(string s)
            {
                sb.AppendLine(s);
                try { Console.WriteLine(s); } catch { }
            }

            W($"==== GlassTranslate self-test @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
            W($"OS: {Environment.OSVersion}  64bit={Environment.Is64BitProcess}");

            // 显示器
            int monitorCount = 0;
            Win32.RECT primary = default;
            Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hmon, _, _, _) =>
            {
                var mi = Win32.MONITORINFO.Create();
                if (Win32.GetMonitorInfo(hmon, ref mi))
                {
                    if (monitorCount == 0) primary = mi.rcMonitor;
                    uint dpi = 96;
                    try { Win32.GetDpiForMonitor(hmon, 0, out dpi, out _); } catch { }
                    W($"monitor[{monitorCount}]: {mi.rcMonitor.Left},{mi.rcMonitor.Top} " +
                      $"{mi.rcMonitor.Width}x{mi.rcMonitor.Height} dpi={dpi}");
                }
                monitorCount++;
                return true;
            }, IntPtr.Zero);

            // OCR
            var ocr = OcrService.Create();
            if (ocr == null)
            {
                W("OCR: ✗ 未找到可用引擎。安装：Windows 设置 → 应用 → 可选功能 → 添加功能 → “英语(美国) 光学字符识别”");
            }
            else
            {
                W($"OCR: ✓ engine={ocr.EngineLanguage} MaxImageDimension={ocr.MaxDimension}");
                int w = Math.Min(primary.Width, 1920), h = Math.Min(primary.Height, 1080);
                var px = ScreenCapture.CaptureSmart(primary.Left, primary.Top, w, h,
                    ocr.MaxDimension, out int sw, out int sh, out double scale);
                W($"capture: {sw}x{sh} (scale 1/{scale:0.##}) bytes={px.Length}");
                var lines = ocr.RecognizeAsync(px, sw, sh).GetAwaiter().GetResult();
                W($"ocr lines: {lines.Count}");
                int willTranslate = 0;
                foreach (var l in lines.Take(15))
                {
                    bool tr = TextFilter.ShouldTranslate(l.Text);
                    if (tr) willTranslate++;
                    W($"  [{(int)(l.X * scale):D4},{(int)(l.Y * scale):D4}] \"{Trunc(l.Text, 60)}\" translate={tr}");
                }
                W($"translatable in first 15: {willTranslate}");
            }

            // 过滤器样例
            W("filter samples:");
            foreach (var s in new[]
            {
                "Settings", "确定", "C:\\Users\\file.txt", "Version 2.10.3",
                "Click here to submit your changes", "https://github.com", "OK"
            })
                W($"  \"{s}\" → {TextFilter.ShouldTranslate(s)}");

            // 缓存
            var p = Path.Combine(Path.GetTempPath(), "gt_selftest_cache.json");
            try { File.Delete(p); } catch { }
            var c = new TranslationCache(p);
            c.Put("hello", "你好");
            c.Flush();
            var ok = c.Get("hello") == "你好";
            W($"cache roundtrip: {(ok ? "✓" : "✗")}");

            // API
            if (withApi)
            {
                var cfg = Config.AppConfig.Load();
                if (string.IsNullOrEmpty(cfg.ApiKey))
                {
                    W("API: 未配置 key，跳过");
                }
                else
                {
                    try
                    {
                        ITranslationProvider pv = cfg.Provider == "anthropic"
                            ? new AnthropicProvider(cfg.AnthropicBaseUrl, cfg.ApiKey, cfg.AnthropicModel)
                            : new OpenAiProvider(cfg.OpenAiBaseUrl, cfg.ApiKey, cfg.OpenAiModel);
                        var (okApi, msg) = pv.TestAsync(CancellationToken.None).GetAwaiter().GetResult();
                        W($"API [{pv.Name}] {cfg.ActiveModel}: {(okApi ? "✓ " : "✗ ")}{msg}");
                    }
                    catch (Exception ex) { W("API test error: " + ex.Message); }
                }
            }

            W("==== self-test done ====");
            var outp = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            try { File.WriteAllText(outp, sb.ToString()); } catch { }
        }

        private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
    }
}
