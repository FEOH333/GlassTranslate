using System;
using System.Windows;
using System.Windows.Controls;
using GlassTranslate.Config;
using GlassTranslate.Core;
using GlassTranslate.Core.Providers;
using GlassTranslate.Platform;
using GlassTranslate.UI;

namespace GlassTranslate
{
    /// <summary>
    /// 应用总装配：UI（悬浮球/悬浮层/设置/托盘）+ 核心（流水线/调度器/缓存/Provider）在此对接。
    /// UI 只发命令、收状态；核心不依赖任何 WPF 类型。
    /// </summary>
    public sealed class AppController
    {
        private AppConfig _cfg = null!;
        private TranslationCache _cache = null!;
        private OcrService? _ocr;
        private TranslationDirector _director = null!;
        private OverlayController _overlay = null!;
        private MessageWindow _msg = null!;
        private TrayIcon _tray = null!;
        private HotkeyManager _hotkey = null!;
        private BallWindow _ball = null!;
        private SettingsWindow? _settings;

        private MenuItem _miShowBall = null!, _miApi = null!, _miRealtime = null!, _miHotkey = null!;

        public AppConfig Config => _cfg;
        public int CacheCount => _cache.Count;
        public string OcrLanguage => _ocr?.EngineLanguage ?? "";

        public void Initialize()
        {
            _cfg = AppConfig.Load();
            _cache = new TranslationCache(AppConfig.CachePath);
            _ocr = OcrService.Create();

            _msg = new MessageWindow();
            _tray = new TrayIcon(_msg.Handle, (uint)MessageWindow.TrayCallbackMsg);
            BuildTrayMenu();

            _hotkey = new HotkeyManager(_msg.Handle);
            _msg.HotkeyPressed += id =>
            {
                if (id == HotkeyManager.MainId) OnHotkeyFired();
            };
            _msg.TrayMessage += msg =>
            {
                switch (msg)
                {
                    case Win32.WM_RBUTTONUP: _tray.ShowContextMenu(); break;
                    case Win32.WM_LBUTTONDBLCLK: ShowBall(true); break;
                }
            };

            _overlay = new OverlayController(_cache);
            _overlay.OnOverlayCreated += () => _ball?.ReassertTopmost();
            _director = new TranslationDirector(_overlay, _cache, CreateProvider, _ocr!);
            if (_ocr == null) Toast.Show("未检测到英文 OCR 引擎，翻译功能不可用。请到 Windows 设置 → 应用 → 可选功能 → 添加功能，安装 “英语(美国) 光学字符识别”。");

            _ball = new BallWindow(this);
            if (_cfg.ShowBall) _ball.Show();

            // 悬浮球/面板区域变化时把自身窗口上报为"不翻译"区域（防止翻译自己界面上的文字）
            _ball.LocationChanged += (s, e) => RefreshExclusions();
            _ball.SizeChanged += (s, e) => RefreshExclusions();
            _ball.IsVisibleChanged += (s, e) => RefreshExclusions();
            RefreshExclusions();

            if (_cfg.HotkeyModeEnabled)
            {
                if (!_hotkey.Register(_cfg.Hotkey))
                {
                    _cfg.HotkeyModeEnabled = false;
                    Toast.Show($"快捷键 {_cfg.Hotkey} 注册失败（可能被其他软件占用），已自动关闭快捷键模式。");
                }
            }
            if (_cfg.RealtimeEnabled) TryStartRealtime();

            SyncUi();
        }

        // ---------------- Provider ----------------

        private ITranslationProvider CreateProvider()
        {
            if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
                throw new ProviderException("尚未配置 API Key：点击悬浮球 → “配置 API”。");
            var preset = _cfg.Preset;
            return preset.Protocol == "anthropic"
                ? new AnthropicProvider(_cfg.EffectiveBaseUrl, _cfg.ApiKey, _cfg.EffectiveModel)
                : new OpenAiProvider(_cfg.EffectiveBaseUrl, _cfg.ApiKey, _cfg.EffectiveModel, preset.NoThinking);
        }

        // ---------------- 命令（悬浮球/托盘共用） ----------------

        public void OpenSettings()
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow(this);
                _settings.LocationChanged += (s, e) => RefreshExclusions();
                _settings.SizeChanged += (s, e) => RefreshExclusions();
                _settings.IsVisibleChanged += (s, e) => RefreshExclusions();
                _settings.Show();
                RefreshExclusions();
            }
            else
            {
                _settings.Activate();
            }
        }

        /// <summary>把悬浮球/设置窗的物理矩形上报给流水线，这些区域内的文字不翻译。</summary>
        private void RefreshExclusions()
        {
            var rects = new List<Win32.RECT>();
            void Add(System.Windows.Window? w)
            {
                if (w == null || !w.IsVisible) return;
                var h = WindowEffects.Hwnd(w);
                if (h != IntPtr.Zero && Win32.GetWindowRect(h, out var r) && r.Width > 0)
                    rects.Add(r);
            }
            Add(_ball);
            Add(_settings);
            _director.UpdatePipelineExclusions(rects);
        }

        public void ToggleRealtime(bool on)
        {
            if (on)
            {
                if (!TryStartRealtime()) { SyncUi(); return; }
            }
            else
            {
                _cfg.RealtimeEnabled = false;
                _director.StopRealtime();
            }
            SaveConfig();
            SyncUi();
        }

        private bool TryStartRealtime()
        {
            if (_ocr == null)
            {
                Toast.Show("无可用 OCR 引擎：请在 Windows 设置 → 应用 → 可选功能 中安装 “英语(美国) 光学字符识别”。");
                _cfg.RealtimeEnabled = false;
                return false;
            }
            try { CreateProvider(); }
            catch (Exception ex)
            {
                Toast.Show(ex.Message);
                _cfg.RealtimeEnabled = false;
                OpenSettings();
                return false;
            }
            _cfg.RealtimeEnabled = true;
            _director.StartRealtime((int)Math.Clamp(_cfg.OcrIntervalMs, 800, 5000));
            Toast.Show("实时全屏翻译已开启");
            return true;
        }

        public void SetHotkeyMode(bool on)
        {
            if (on)
            {
                if (!_hotkey.Register(_cfg.Hotkey))
                {
                    Toast.Show($"快捷键 {_cfg.Hotkey} 注册失败，可能被其他软件占用。点击悬浮球上的快捷键框可重新录制。");
                    _cfg.HotkeyModeEnabled = false;
                }
                else
                {
                    _cfg.HotkeyModeEnabled = true;
                    Toast.Show($"快捷键翻译已开启：按 {_cfg.Hotkey} 全屏翻译，再按一次消失");
                }
            }
            else
            {
                _hotkey.Unregister();
                _cfg.HotkeyModeEnabled = false;
            }
            SaveConfig();
            SyncUi();
        }

        public void ApplyHotkeyCombo(string combo)
        {
            _cfg.Hotkey = combo;
            if (_cfg.HotkeyModeEnabled)
            {
                _hotkey.Unregister();
                if (!_hotkey.Register(combo))
                    Toast.Show($"快捷键 {combo} 注册失败，可能被其他软件占用。");
            }
            SaveConfig();
            SyncUi();
        }

        private void OnHotkeyFired()
        {
            if (!_cfg.HotkeyModeEnabled)
            {
                Toast.Show("快捷键翻译未启用：点击悬浮球 → “快捷键全屏翻译”。");
                return;
            }
            _director.ToggleOneShot();
        }

        public void ShowBall(bool show)
        {
            _cfg.ShowBall = show;
            if (show)
            {
                _ball.ForceCollapseInstant();
                _ball.Show();
                _ball.ReassertTopmost();
            }
            else
            {
                _ball.ForceCollapseInstant();
                _ball.Hide();
            }
            SaveConfig();
            SyncUi();
        }

        public void SaveConfig() => _cfg.Save();

        public void OnConfigSaved()
        {
            Toast.Show("配置已保存");
            // 轮询间隔可能变化：实时模式重启一次
            if (_cfg.RealtimeEnabled)
            {
                _director.StopRealtime();
                TryStartRealtime();
            }
            SyncUi();
        }

        public void ClearCache() => _cache.Clear();

        /// <summary>用量汇总（设置窗口展示）。</summary>
        public string UsageSummary
        {
            get
            {
                var (api, lines, hits) = UsageStats.Snapshot();
                return $"累计翻译 {lines} 句 · API 调用 {api} 次 · 本次缓存命中 {hits} 次";
            }
        }

        /// <summary>一键清除屏幕上全部译文气泡（悬浮球中键 / 右键菜单 / 托盘）。</summary>
        public void ClearTranslations() => _overlay.ClearAll();

        public void ExitApp()
        {
            try
            {
                _cfg.RealtimeEnabled = _director.RealtimeRunning;
                SaveConfig();
                _cache.Flush();
                UsageStats.Flush();
                _director.Dispose();
                _tray.Dispose();
                _hotkey.Unregister();
                _msg.Dispose();
            }
            catch { }
            Application.Current.Shutdown();
        }

        // ---------------- 状态同步 ----------------

        private void BuildTrayMenu()
        {
            var menu = _tray.Menu;
            _miShowBall = new MenuItem { Header = "显示悬浮球", IsCheckable = true };
            _miShowBall.Click += (s, e) => ShowBall(_miShowBall.IsChecked);
            _miApi = new MenuItem { Header = "配置 API…" };
            _miApi.Click += (s, e) => OpenSettings();
            _miRealtime = new MenuItem { Header = "启用实时全屏翻译", IsCheckable = true };
            _miRealtime.Click += (s, e) => ToggleRealtime(_miRealtime.IsChecked);
            _miHotkey = new MenuItem { Header = "启用快捷键全屏翻译", IsCheckable = true };
            _miHotkey.Click += (s, e) => SetHotkeyMode(_miHotkey.IsChecked);
            var clear = new MenuItem { Header = "清除当前译文" };
            clear.Click += (s, e) => ClearTranslations();
            var exit = new MenuItem { Header = "退出" };
            exit.Click += (s, e) => ExitApp();

            menu.Items.Add(_miShowBall);
            menu.Items.Add(new Separator());
            menu.Items.Add(_miApi);
            menu.Items.Add(_miRealtime);
            menu.Items.Add(_miHotkey);
            menu.Items.Add(clear);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);
        }

        private void SyncUi()
        {
            void Apply()
            {
                _miShowBall.IsChecked = _cfg.ShowBall;
                _miRealtime.IsChecked = _cfg.RealtimeEnabled;
                _miHotkey.IsChecked = _cfg.HotkeyModeEnabled;
                _ball?.UpdateStates(_cfg.RealtimeEnabled, _cfg.HotkeyModeEnabled, _cfg.Hotkey);
            }
            var app = Application.Current;
            if (app?.Dispatcher.CheckAccess() == true) Apply();
            else app?.Dispatcher.BeginInvoke(Apply);
        }
    }
}
