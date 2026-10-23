using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GlassTranslate.Config;
using GlassTranslate.Core;
using GlassTranslate.Core.Providers;

namespace GlassTranslate.UI
{
    /// <summary>API 配置窗口：提供商切换时自动搬运各自的地址/模型/密钥草稿。</summary>
    public partial class SettingsWindow : Window
    {
        private static readonly string[] OpenAiModels = { "gpt-4o-mini", "gpt-4.1-mini", "gpt-4.1", "gpt-4o" };
        private static readonly string[] AnthropicModels = { "claude-3-5-haiku-latest", "claude-haiku-4-5", "claude-sonnet-4-5" };

        private readonly AppController _controller;
        private readonly AppConfig _cfg;
        private bool _keyVisible;

        public SettingsWindow(AppController controller)
        {
            InitializeComponent();
            _controller = controller;
            _cfg = controller.Config;

            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");

            Header.MouseLeftButtonDown += (s, e) => DragMove();
            BtnClose.Click += (s, e) => Close();
            BtnEye.Click += (s, e) => ToggleKeyVisibility();
            BtnSave.Click += (s, e) => SaveAndClose();
            BtnClearCache.Click += async (s, e) => await ClearCacheAsync();
            BtnTest.Click += async (s, e) => await TestConnectionAsync();
            SegOpenAi.Checked += (s, e) => SwitchProvider("openai");
            SegAnthropic.Checked += (s, e) => SwitchProvider("anthropic");

            LoadFromConfig(_cfg.Provider);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
        }

        private void LoadFromConfig(string provider)
        {
            if (provider == "anthropic")
            {
                SegAnthropic.IsChecked = true;
                KeyBox.Password = _cfg.ApiKey;
                BaseUrlBox.Text = _cfg.AnthropicBaseUrl;
                ModelBox.ItemsSource = AnthropicModels;
                ModelBox.Text = _cfg.AnthropicModel;
            }
            else
            {
                SegOpenAi.IsChecked = true;
                KeyBox.Password = _cfg.ApiKey;
                BaseUrlBox.Text = _cfg.OpenAiBaseUrl;
                ModelBox.ItemsSource = OpenAiModels;
                ModelBox.Text = _cfg.OpenAiModel;
            }
            CacheInfo.Text = $"本地缓存：{_controller.CacheCount} 条（命中的句子不再调用 API）";
            OcrInfo.Text = _controller.OcrLanguage is { Length: > 0 } lang
                ? $"OCR 引擎：{lang}"
                : "OCR 引擎：未检测到英文 OCR（Windows 设置 → 应用 → 可选功能 → 添加）";
        }

        /// <summary>切换提供商前，把当前输入暂存回 cfg 的对应字段。</summary>
        private void StashCurrentFields()
        {
            var key = _keyVisible ? KeyPlain.Text : KeyBox.Password;
            if (_cfg.Provider == "anthropic")
            {
                _cfg.AnthropicBaseUrl = BaseUrlBox.Text.Trim();
                _cfg.AnthropicModel = ModelBox.Text.Trim();
                _cfg.ApiKey = key;
            }
            else
            {
                _cfg.OpenAiBaseUrl = BaseUrlBox.Text.Trim();
                _cfg.OpenAiModel = ModelBox.Text.Trim();
                _cfg.ApiKey = key;
            }
        }

        private void SwitchProvider(string provider)
        {
            if (_cfg.Provider == provider) return;
            StashCurrentFields();
            _cfg.Provider = provider;
            LoadFromConfig(provider);
        }

        private void ToggleKeyVisibility()
        {
            _keyVisible = !_keyVisible;
            if (_keyVisible)
            {
                KeyPlain.Text = KeyBox.Password;
                KeyPlain.Visibility = Visibility.Visible;
                KeyBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                KeyBox.Password = KeyPlain.Text;
                KeyBox.Visibility = Visibility.Visible;
                KeyPlain.Visibility = Visibility.Collapsed;
            }
        }

        private async Task TestConnectionAsync()
        {
            StashCurrentFields();
            if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
            {
                TestResult.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                TestResult.Text = "请先填写 API Key";
                return;
            }
            BtnTest.IsEnabled = false;
            TestResult.Foreground = (Brush)FindResource("TextSubtle");
            TestResult.Text = "测试中…";
            try
            {
                ITranslationProvider provider = _cfg.Provider == "anthropic"
                    ? new AnthropicProvider(_cfg.AnthropicBaseUrl, _cfg.ApiKey, _cfg.AnthropicModel)
                    : new OpenAiProvider(_cfg.OpenAiBaseUrl, _cfg.ApiKey, _cfg.OpenAiModel);
                var (ok, msg) = await Task.Run(() => provider.TestAsync(CancellationToken.None));
                TestResult.Foreground = new SolidColorBrush(ok
                    ? Color.FromRgb(0x4A, 0xDE, 0x80)
                    : Color.FromRgb(0xF8, 0x71, 0x71));
                TestResult.Text = msg;
            }
            catch (Exception ex)
            {
                TestResult.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                TestResult.Text = ProviderException.Describe(ex);
            }
            finally { BtnTest.IsEnabled = true; }
        }

        private async Task ClearCacheAsync()
        {
            await Task.Run(() => _controller.ClearCache());
            CacheInfo.Text = $"本地缓存：0 条（命中的句子不再调用 API）";
            Toast.Show("本地翻译缓存已清空");
        }

        private void SaveAndClose()
        {
            StashCurrentFields();
            if (string.IsNullOrWhiteSpace(_cfg.ApiKey))
            {
                TestResult.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                TestResult.Text = "API Key 不能为空";
                return;
            }
            _controller.SaveConfig();
            _controller.OnConfigSaved();
            Close();
        }
    }
}
