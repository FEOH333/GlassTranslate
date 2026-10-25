using System;
using System.IO;
using System.Threading;
using System.Windows;
using GlassTranslate.Platform;
using GlassTranslate.UI;

namespace GlassTranslate
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        public AppController? Controller { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length > 0 && e.Args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                SelfTest.Run(e.Args.Length > 1 && e.Args[1] == "--api");
                Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                try { Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS); } catch { }
                Config.HeadlessConfig.Apply(e.Args.Skip(1).ToArray());
                Shutdown();
                return;
            }

            _mutex = new Mutex(true, "Local\\GlassTranslate_SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("GlassTranslate 已在运行（请查看任务栏右下角托盘图标）。",
                    "GlassTranslate", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, ev) =>
            {
                LogError(ev.Exception);
                Toast.Show("发生错误：" + ev.Exception.Message);
                ev.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                LogError(ev.ExceptionObject as Exception);

            Controller = new AppController();
            Controller.Initialize();
        }

        private static void LogError(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GlassTranslate");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "error.log");
                // 防无限增长：超过 256KB 只保留后半段
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                {
                    var tail = File.ReadAllText(path);
                    File.WriteAllText(path, tail.Length > 128 * 1024 ? tail[^ (128 * 1024)..] : tail);
                }
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
