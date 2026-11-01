using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GlassTranslate.Core;
using GlassTranslate.Core.LocalMt;
using GlassTranslate.Core.OcrEngine;
using GlassTranslate.Platform;

namespace GlassTranslate
{
    /// <summary>
    /// 无 GUI 离线自检：--selftest
    /// 检查显示器/截屏/双 OCR 引擎（各自行数与耗时对比）/过滤器/句库/本地翻译引擎。
    /// 输出写入 exe 旁 selftest.log。全程零网络。
    /// </summary>
    internal static class SelfTest
    {
        public static void Run()
        {
            // 整个自检放到 MTA 线程池线程跑：WinRT 的 RecognizeAsync 在 STA 主线程上
            // 同步等待会死锁（WPF 调度器被阻塞，完成回调无法回泵）。
            System.Threading.Tasks.Task.Run(RunCore).Wait();
        }

        private static void RunCore()
        {
            var sb = new System.Text.StringBuilder();
            void W(string s)
            {
                sb.AppendLine(s);
                try { Console.WriteLine(s); } catch { }
            }

            W($"==== GlassTranslate v2.0 self-test (offline) @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
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

            // 截屏
            int cw = Math.Min(primary.Width, 1600), ch = Math.Min(primary.Height, 900);
            var px = ScreenCapture.CaptureSmart(primary.Left, primary.Top, cw, ch,
                4096, out int sw, out int sh, out double scale);
            W($"capture: {sw}x{sh} (scale 1/{scale:0.##}) bytes={px.Length}");

            // 双 OCR 引擎对比
            var engines = new (string name, IOcrEngine? engine)[]
            {
                ("rapid",  RapidOcrEngine.Create()),
                ("windows", WindowsOcrEngine.Create()),
            };
            var rapidForDiag = RapidOcrEngine.Create();
            foreach (var (name, engine) in engines)
            {
                if (engine == null)
                {
                    var why = name == "rapid" ? RapidOcrEngine.LastCreateError : "(无引擎)";
                    W($"OCR[{name}]: 不可用 —— {why}");
                    continue;
                }
                var peer = engine.CreatePeer();
                var watch = Stopwatch.StartNew();
                var lines = peer.RecognizeAsync(px, sw, sh).GetAwaiter().GetResult();
                watch.Stop();
                W($"OCR[{name}] {peer.EngineName}: {lines.Count} 行, {watch.ElapsedMilliseconds}ms");
                foreach (var l in lines.Take(6))
                    W($"  [{(int)(l.X * scale):D4},{(int)(l.Y * scale):D4}] \"{Trunc(l.Text, 56)}\" translate={TextFilter.ShouldTranslate(l.Text)}");
            }

            // 过滤器样例
            W("filter samples:");
            foreach (var s in new[]
            {
                "Settings", "确定", "C:\\Users\\file.txt", "Click here to submit your changes",
                "https://github.com", "OK", "fn main()", "error: mismatched types"
            })
                W($"  \"{s}\" → {TextFilter.ShouldTranslate(s)}");

            // 句库
            var p = Path.Combine(Path.GetTempPath(), "gt_selftest_cache.json");
            try { File.Delete(p); } catch { }
            var c = new TranslationCache(p);
            c.Put("hello", "你好");
            c.Flush();
            W($"cache roundtrip: {(c.Get("hello") == "你好" ? "OK" : "FAIL")}");

            // 本地翻译引擎
            W($"MT models present: {LocalMtEngine.ModelsPresent()} ({LocalMtEngine.ModelsDir})");
            var mt = LocalMtEngine.Create();
            if (mt == null)
            {
                W($"MT: 引擎创建失败 —— {LocalMtEngine.LastCreateError}");
                DumpMtDiagnostics(W);
            }
            else
            {
                var warm = mt.Translate("Hello"); // 首次推理含预热
                foreach (var src in new[] { "Hello, world!", "Click the button below to upload your file.", "If the deployment fails, check the network." })
                {
                    var w2 = Stopwatch.StartNew();
                    var dst = mt.Translate(src);
                    w2.Stop();
                    W($"MT: \"{Trunc(src, 40)}\" → \"{dst}\" ({w2.ElapsedMilliseconds}ms)");
                }
                mt.Dispose();
            }

            W("==== self-test done ====");
            var outp = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            try { File.WriteAllText(outp, sb.ToString()); } catch { }
        }

        private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

        /// <summary>MT 失败时的深度诊断：会话 IO 面 + 分词器往返。</summary>
        private static void DumpMtDiagnostics(Action<string> W)
        {
            try
            {
                var dir = LocalMtEngine.ModelsDir;
                using var enc = new Microsoft.ML.OnnxRuntime.InferenceSession(System.IO.Path.Combine(dir, "encoder.onnx"));
                using var dec = new Microsoft.ML.OnnxRuntime.InferenceSession(System.IO.Path.Combine(dir, "decoder.onnx"));
                W("encoder inputs: " + string.Join(", ", enc.InputMetadata.Select(kv => $"{kv.Key}:{string.Join('x', kv.Value.Dimensions)}")));
                W("encoder outputs: " + string.Join(", ", enc.OutputMetadata.Select(kv => $"{kv.Key}:{string.Join('x', kv.Value.Dimensions)}")));
                W("decoder inputs: " + string.Join(", ", dec.InputMetadata.Select(kv => $"{kv.Key}:{string.Join('x', kv.Value.Dimensions)}")));
                W("decoder outputs: " + string.Join(", ", dec.OutputMetadata.Select(kv => $"{kv.Key}:{string.Join('x', kv.Value.Dimensions)}")));

                var src = SpmUnigram.Load(System.IO.Path.Combine(dir, "source.spm"));
                var tgt = SpmUnigram.Load(System.IO.Path.Combine(dir, "target.spm"));
                var enc2 = src.Encode("Hello, world!");
                W($"spm src vocab={src.Count} unk={src.UnkId} roundtrip=\"{src.Decode(enc2)}\" ids=[{string.Join(',', enc2.Take(12))}]");
                var dec2 = tgt.Encode("你好，世界！");
                W($"spm tgt vocab={tgt.Count} unk={tgt.UnkId} roundtrip=\"{tgt.Decode(dec2)}\" ids=[{string.Join(',', dec2.Take(12))}]");
                W($"tgt ids: </s>={tgt.FindId("</s>")} <s>={tgt.FindId("<s>")} <pad>={tgt.FindId("<pad>")} <unk>={tgt.FindId("<unk>")}");
            }
            catch (Exception ex)
            {
                W("diag error: " + ex.Message);
            }
        }
    }
}
