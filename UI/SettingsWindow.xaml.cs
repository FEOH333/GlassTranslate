using System;
using System.Windows;
using System.Windows.Controls;
using GlassTranslate.Config;
using GlassTranslate.Core;

namespace GlassTranslate.UI
{
    /// <summary>
    /// 设置窗口（v2.0 引擎状态页）：本地引擎状态、OCR 引擎选择、
    /// 扫描间隔、句库与用量。全程离线——没有 API Key 之类的任何字段。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppController _controller;
        private readonly AppConfig _cfg;

        public SettingsWindow(AppController controller)
        {
            InitializeComponent();
            _controller = controller;
            _cfg = controller.Config;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");

            Header.MouseLeftButtonDown += (s, e) => DragMove();
            BtnClose.Click += (s, e) => Close();
            BtnClearCache.Click += async (s, e) => await ClearCacheAsync();
            BtnSave.Click += (s, e) => SaveAndClose();
            IntervalSlider.ValueChanged += (s, e) =>
                IntervalText.Text = $"{IntervalSlider.Value / 1000:0.0} 秒";

            LoadFromConfig();
        }

        private void LoadFromConfig()
        {
            MtInfo.Text = _controller.MtStatus;
            OcrInfo.Text = _controller.OcrStatus;
            EngineBox.SelectedIndex = _cfg.OcrEngine switch
            {
                "rapid" => 1,
                "windows" => 2,
                _ => 0
            };
            IntervalSlider.Value = Math.Clamp(_cfg.OcrIntervalMs, 800, 3000);
            CacheInfo.Text = $"本地句库：{_controller.CacheCount} 条（重复句子零推理）";
            UsageInfo.Text = _controller.UsageSummary;
        }

        private async System.Threading.Tasks.Task ClearCacheAsync()
        {
            await System.Threading.Tasks.Task.Run(() => _controller.ClearCache());
            CacheInfo.Text = $"本地句库：0 条（重复句子零推理）";
            Toast.Show("本地句库已清空");
        }

        private void SaveAndClose()
        {
            var enginePref = EngineBox.SelectedIndex switch
            {
                1 => "rapid",
                2 => "windows",
                _ => "auto"
            };
            bool engineChanged = enginePref != _cfg.OcrEngine;
            _cfg.OcrEngine = enginePref;
            _cfg.OcrIntervalMs = IntervalSlider.Value;

            if (engineChanged) _controller.ApplyOcrEngine(enginePref);
            _controller.SaveConfig();
            _controller.OnConfigSaved();
            Close();
        }
    }
}
