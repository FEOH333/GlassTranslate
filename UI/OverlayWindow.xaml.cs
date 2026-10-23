using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GlassTranslate.Platform;

namespace GlassTranslate.UI
{
    /// <summary>
    /// 覆盖单个显示器的透明悬浮层：点击穿透、永不抢焦点，
    /// 所有译文气泡都在这层画布上渲染（每显示器一个窗口，避免跨 DPI 拉伸）。
    /// </summary>
    public partial class OverlayWindow : Window
    {
        internal Win32.RECT PixelRect { get; }
        internal double Dpi { get; private set; } = 96;
        internal int MonitorIndex { get; }

        private readonly ObservableCollection<TooltipVm> _items = new();

        internal OverlayWindow(Win32.RECT pixelRect, int monitorIndex)
        {
            InitializeComponent();
            PixelRect = pixelRect;
            MonitorIndex = monitorIndex;
            Items.ItemsSource = _items;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowEffects.MakeClickThrough(this);
            WindowEffects.SetWindowPixelRect(this,
                PixelRect.Left, PixelRect.Top, PixelRect.Width, PixelRect.Height);
            var m = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            Dpi = 96.0 * m.M11;
        }

        internal void Add(TooltipVm vm) => _items.Add(vm);
        internal void Remove(TooltipVm vm) => _items.Remove(vm);
        internal int Count => _items.Count;

        internal double ToDip(int virtualPx) => virtualPx * 96.0 / Dpi;
    }
}
