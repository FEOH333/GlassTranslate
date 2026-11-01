using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using GlassTranslate.Config;
using GlassTranslate.Core;
using GlassTranslate.Core.LocalMt;
using GlassTranslate.Core.OcrEngine;
using GlassTranslate.Platform;
using GlassTranslate.UI;

namespace GlassTranslate
{
    /// <summary>
    /// 应用总装配（v2.0 纯本地版）：
    /// 启动时装配 OCR 引擎（RapidOCR 优先/Windows 回退）与本地翻译引擎（Opus-MT），
    /// UI（悬浮球/悬浮层/设置/托盘）与核心（流水线/调度器/句库）在此对接。
    /// 全程零网络——解决方案内已无任何 HTTP 客户端代码。
    /// </summary>
    public sealed class AppController
    {
        private AppConfig _cfg = null!;
        private TranslationCache _cache = null!;
        private LocalMtEngine? _mt;
        private IOcrEngine? _ocr;
        private TranslationDirector? _director;
        private OverlayController _overlay = null!;
        private MessageWindow _msg = null!;
        private TrayIcon _tray = null!;
        private HotkeyManager _hotkey = null!;
        private BallWindow _ball = null!;
        private SettingsWindow? _settings;

        private MenuItem _miShowBall = null!, _miSet = null!, _miRealtime = null!, _miHotkey = null!;

        public AppConfig Config => _cfg;
        public int CacheCount => _cache.Count;

        public string MtStatus => _mt != null ? _mt.EngineName
            : "未加载（models/mt 缺失，运行 tools/download-models.ps1）";
        public string OcrStatus => _ocr?.EngineName ?? "无可用 OCR 引擎";
        public string OcrEngineName => _director?.OcrEngineName ?? OcrStatus;
        public bool FullyReady => _mt != null && _ocr != null;

        public void Initialize()
        {
            _cfg = AppConfig.Load();
            _cache = new TranslationCache(AppConfig.CachePath);

            // ---- 引擎装配：本地翻译 + OCR（Rapid 优先，Windows 回退） ----
            _mt = LocalMtEngine.Create();
            _ocr = CreateOcrEngine(_cfg.OcrEngine);

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
            _overlay.AfterRealtimeSync += RefreshExclusions;   // 译文气泡区域也纳入"不识别"范围

            if (_mt != null && _ocr != null)
                _director = new TranslationDirector(_overlay, _cache, _mt, _ocr);

            _ball = new BallWindow(this);
            _ball.LocationChanged += (s, e) => RefreshExclusions();
            _ball.SizeChanged += (s, e) => RefreshExclusions();
            _ball.IsVisibleChanged += (s, e) => RefreshExclusions();
            if (_cfg.ShowBall) _ball.Show();

            if (_mt == null)
                Toast.Show("本地翻译模型未找到：请运行 tools/download-models.ps1 下载（约 110MB，仅一次）。");
            if (_ocr == null)
                Toast.Show("未检测到可用 OCR 引擎：RapidOCR 模型缺失且 Windows OCR 不可用。");

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

        private static IOcrEngine? CreateOcrEngine(string pref)
        {
            if (pref == "windows")
                return WindowsOcrEngine.Create();
            var rapid = RapidOcrEngine.Create();
            if (rapid != null || pref == "rapid") return rapid;
            return WindowsOcrEngine.Create();
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

        public void ToggleRealtime(bool on)
        {
            if (on)
            {
                if (!TryStartRealtime()) { SyncUi(); return; }
            }
            else
            {
                _cfg.RealtimeEnabled = false;
                _director?.StopRealtime();
            }
            SaveConfig();
            SyncUi();
        }

        private bool TryStartRealtime()
        {
            if (_mt == null)
            {
                Toast.Show("本地翻译模型未加载：请运行 tools/download-models.ps1 下载模型后重启。");
                _cfg.RealtimeEnabled = false;
                return false;
            }
            if (_ocr == null)
            {
                Toast.Show("无可用 OCR 引擎（RapidOCR 模型缺失且 Windows OCR 不可用）。");
                _cfg.RealtimeEnabled = false;
                return false;
            }
            _director ??= new TranslationDirector(_overlay, _cache, _mt, _ocr);
            _cfg.RealtimeEnabled = true;
            _director.StartRealtime((int)Math.Clamp(_cfg.OcrIntervalMs, 800, 5000));
            Toast.Show("实时全屏翻译已开启（本地离线）");
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
                _director?.ClearOneShot();   // 关闭时清掉可能还在屏幕上的一次性译文
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
            if (_director == null)
            {
                Toast.Show("引擎未就绪：请先下载模型（tools/download-models.ps1）。");
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

        /// <summary>引擎/间隔等配置变化后调用：实时模式热重启。</summary>
        public void OnConfigSaved()
        {
            Toast.Show("设置已保存");
            if (_director != null && _director.RealtimeRunning)
            {
                _director.StopRealtime();
                TryStartRealtime();
            }
            else if (_cfg.RealtimeEnabled)
            {
                TryStartRealtime();
            }
            SyncUi();
        }

        /// <summary>应用新的 OCR 引擎选择（设置窗口保存时调用）。</summary>
        public void ApplyOcrEngine(string pref)
        {
            _cfg.OcrEngine = pref;
            var next = CreateOcrEngine(pref);
            if (next == null)
            {
                Toast.Show("该 OCR 引擎不可用，保持原引擎。");
                return;
            }
            _ocr = next;
            if (_mt != null)
            {
                _director?.Dispose();
                _director = new TranslationDirector(_overlay, _cache, _mt, _ocr);
                if (_cfg.RealtimeEnabled) TryStartRealtime();
            }
        }

        public void ClearCache() => _cache.Clear();

        /// <summary>用量汇总（设置窗口展示）。</summary>
        public string UsageSummary
        {
            get
            {
                var (calls, lines) = UsageStats.Snapshot();
                return $"累计翻译 {lines} 句 · 本地推理 {calls} 批 · 本次缓存命中 {_cache.CacheHits} 次";
            }
        }

        /// <summary>一键清除屏幕上全部译文气泡（悬浮球中键 / 右键菜单 / 托盘）。</summary>
        public void ClearTranslations() => _overlay.ClearAll();

        public void ExitApp()
        {
            try
            {
                if (_director != null) _cfg.RealtimeEnabled = _director.RealtimeRunning;
                SaveConfig();
                _cache.Flush();
                UsageStats.Flush();
                _director?.Dispose();
                _mt?.Dispose();
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
            _miSet = new MenuItem { Header = "设置…" };
            _miSet.Click += (s, e) => OpenSettings();
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
            menu.Items.Add(_miSet);
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

        /// <summary>把自身窗口 + 译文气泡的物理矩形上报给流水线，这些区域内的文字不识别不翻译。</summary>
        private void RefreshExclusions()
        {
            var rects = new List<Win32.RECT>();
            void Add(Window? w)
            {
                if (w == null || !w.IsVisible) return;
                var h = WindowEffects.Hwnd(w);
                if (h != IntPtr.Zero && Win32.GetWindowRect(h, out var r) && r.Width > 0)
                    rects.Add(r);
            }
            Add(_ball);
            Add(_settings);
            rects.AddRange(_overlay.CollectBubbleRects());
            _director?.UpdatePipelineExclusions(rects);
        }
    }
}
