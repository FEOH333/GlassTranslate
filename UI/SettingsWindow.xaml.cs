using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GlassTranslate.Config;
using GlassTranslate.Core;
using GlassTranslate.Core.Providers;

namespace GlassTranslate.UI
{
    /// <summary>
    /// API 配置窗口。
    /// 服务商下拉含内置预设（DeepSeek/通义/Kimi/GLM/OpenAI/Anthropic + 两个自定义协议），
    /// 切换预设自动填官方地址与模型；每个服务商的 Key/地址/模型在会话内独立记忆，
    /// 避免来回切换时反复粘贴。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private sealed class Draft
        {
            public string Key = "";
            public string BaseUrl = "";
            public string Model = "";
        }

        private readonly AppController _controller;
        private readonly AppConfig _cfg;
        private bool _keyVisible;
        private bool _switching;

        /// <summary>会话内各服务商的草稿（含已保存的当前项）。</summary>
        private readonly Dictionary<string, Draft> _drafts = new();

        public SettingsWindow(AppController controller)
        {
            InitializeComponent();
            _controller = controller;
            _cfg = controller.Config;

            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");

            foreach (var p in ProviderPresets.All)
                ProviderBox.Items.Add(p.DisplayName);
            ModelBox.IsEditable = true;

            Header.MouseLeftButtonDown += (s, e) => DragMove();
            BtnClose.Click += (s, e) => Close();
            BtnEye.Click += (s, e) => ToggleKeyVisibility();
            BtnSave.Click += (s, e) => SaveAndClose();
            BtnClearCache.Click += async (s, e) => await ClearCacheAsync();
            BtnTest.Click += async (s, e) => await TestConnectionAsync();
            ProviderBox.SelectionChanged += (s, e) => OnProviderChanged();

            LoadFromConfig();
        }

        // ---------------- 载入 / 切换 ----------------

        private string CurrentKeyText => _keyVisible ? KeyPlain.Text : KeyBox.Password;

        private void SetKeyText(string key)
        {
            KeyBox.Password = key;
            KeyPlain.Text = key;
        }

        private Draft DraftOf(string providerId) =>
            _drafts.TryGetValue(providerId, out var d) ? d : new Draft();

        private void LoadFromConfig()
        {
            _switching = true;
            var preset = _cfg.Preset;
            ProviderBox.SelectedIndex = IndexOfPreset(preset.Id);
            _switching = false;

            // 当前已保存的服务商：从配置取；其余预设用官方默认
            _drafts[preset.Id] = new Draft
            {
                Key = _cfg.ApiKey,
                BaseUrl = _cfg.BaseUrl,
                Model = _cfg.Model
            };
            ApplyDraft(preset.Id);

            CacheInfo.Text = $"本地缓存：{_controller.CacheCount} 条（命中的句子不再调用 API）";
            OcrInfo.Text = _controller.OcrLanguage is { Length: > 0 } lang
                ? $"OCR 引擎：{lang}"
                : "OCR 引擎：未检测到英文 OCR（Windows 设置 → 应用 → 可选功能 → 添加）";
        }

        private static int IndexOfPreset(string id)
        {
            for (int i = 0; i < ProviderPresets.All.Length; i++)
                if (ProviderPresets.All[i].Id == id) return i;
            return 0;
        }

        /// <summary>切换前把当前输入收进草稿。</summary>
        private void StashCurrent()
        {
            var id = _cfg.Preset.Id;
            _drafts[id] = new Draft
            {
                Key = CurrentKeyText,
                BaseUrl = BaseUrlBox.Text.Trim(),
                Model = ModelBox.Text.Trim()
            };
        }

        private void OnProviderChanged()
        {
            if (_switching) return;
            StashCurrent();

            var preset = SelectedPreset();
            _cfg.ProviderId = preset.Id;
            ApplyDraft(preset.Id);
        }

        private ProviderPreset SelectedPreset()
        {
            var idx = ProviderBox.SelectedIndex >= 0 ? ProviderBox.SelectedIndex : 0;
            return ProviderPresets.All[Math.Clamp(idx, 0, ProviderPresets.All.Length - 1)];
        }

        private void ApplyDraft(string providerId)
        {
            var preset = ProviderPresets.ById(providerId);
            var d = DraftOf(providerId);

            SetKeyText(d.Key);
            BaseUrlBox.Text = d.BaseUrl.Length > 0 ? d.BaseUrl : preset.BaseUrl;
            if (string.IsNullOrWhiteSpace(preset.BaseUrl) && d.BaseUrl.Length == 0)
                BaseUrlBox.Text = ""; // 自定义：留空待填

            ModelBox.Items.Clear();
            foreach (var m in preset.Models)
                if (m.Length > 0) ModelBox.Items.Add(m);
            ModelBox.Text = d.Model.Length > 0 ? d.Model : preset.DefaultModel;

            ProtoInfo.Text = preset.Protocol == "anthropic"
                ? $"协议：Anthropic Messages 兼容 · Key：{preset.KeyHint}"
                : $"协议：OpenAI 兼容 · Key：{preset.KeyHint}";

            TestResult.Text = "";
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

        // ---------------- 测试 / 缓存 / 保存 ----------------

        private async Task TestConnectionAsync()
        {
            StashCurrent();
            if (string.IsNullOrWhiteSpace(CurrentKeyText))
            {
                ShowResult(false, "请先填写 API Key");
                return;
            }
            BtnTest.IsEnabled = false;
            ShowResult(null, "测试中…");
            try
            {
                var preset = _cfg.Preset;
                var draft = DraftOf(preset.Id);
                var baseUrl = draft.BaseUrl.Length > 0 ? draft.BaseUrl : preset.BaseUrl;
                var model = draft.Model.Length > 0 ? draft.Model : preset.DefaultModel;
                ITranslationProvider provider = preset.Protocol == "anthropic"
                    ? new AnthropicProvider(baseUrl, CurrentKeyText, model)
                    : new OpenAiProvider(baseUrl, CurrentKeyText, model);
                var (ok, msg) = await Task.Run(() => provider.TestAsync(CancellationToken.None));
                ShowResult(ok, msg);
            }
            catch (Exception ex)
            {
                ShowResult(false, ProviderException.Describe(ex));
            }
            finally { BtnTest.IsEnabled = true; }
        }

        private void ShowResult(bool? ok, string msg)
        {
            TestResult.Foreground = new SolidColorBrush(ok switch
            {
                true => Color.FromRgb(0x4A, 0xDE, 0x80),
                false => Color.FromRgb(0xF8, 0x71, 0x71),
                _ => Color.FromRgb(0xB8, 0xC0, 0xCC)
            });
            TestResult.Text = msg;
        }

        private async Task ClearCacheAsync()
        {
            await Task.Run(() => _controller.ClearCache());
            CacheInfo.Text = $"本地缓存：0 条（命中的句子不再调用 API）";
            Toast.Show("本地翻译缓存已清空");
        }

        private void SaveAndClose()
        {
            StashCurrent();
            var preset = _cfg.Preset;
            var draft = DraftOf(preset.Id);
            if (string.IsNullOrWhiteSpace(draft.Key))
            {
                ShowResult(false, "API Key 不能为空");
                return;
            }
            _cfg.ApiKey = draft.Key;
            // 与预设默认相同就不存，预设日后升级模型名时自动跟进
            _cfg.BaseUrl = draft.BaseUrl == preset.BaseUrl ? "" : draft.BaseUrl;
            _cfg.Model = draft.Model == preset.DefaultModel ? "" : draft.Model;
            _controller.SaveConfig();
            _controller.OnConfigSaved();
            Close();
        }
    }
}
