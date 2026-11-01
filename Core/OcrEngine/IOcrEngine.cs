using System.Collections.Generic;
using System.Threading.Tasks;

namespace GlassTranslate.Core.OcrEngine
{
    public sealed class OcrLineResult
    {
        public string Text = "";
        public double X, Y, Width, Height;
    }

    /// <summary>
    /// OCR 引擎统一接口：实现方负责自身的线程约束与模型生命周期。
    /// 每个显示器扫描线程使用独立 Peer 实例（CreatePeer）。
    /// </summary>
    public interface IOcrEngine
    {
        string EngineName { get; }
        int MaxDimension { get; }
        Task<List<OcrLineResult>> RecognizeAsync(byte[] bgra, int w, int h);
        /// <summary>为并行扫描线程创建独立实例；不支持时返回自身。</summary>
        IOcrEngine CreatePeer();
    }
}
