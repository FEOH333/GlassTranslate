using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RapidOcrNet;
using SkiaSharp;

namespace GlassTranslate.Core.OcrEngine
{
    /// <summary>
    /// RapidOCR 引擎（PP-OCRv5，det→cls→rec 三模型 ONNX 流水线，CPU）。
    /// 使用 NuGet 内置模型（Latin 集随包复制到 models/v5/），无需手工下载；
    /// 每个并行扫描线程创建独立实例（RapidOcr 非线程安全）。
    /// </summary>
    public sealed class RapidOcrEngine : IOcrEngine
    {
        private readonly RapidOcr _ocr;
        public string EngineName => "RapidOCR (PP-OCRv5)";

        public static string? LastCreateError { get; private set; }

        private RapidOcrEngine(RapidOcr ocr) => _ocr = ocr;

        public int MaxDimension => 4096; // det/rec 管线的安全上限（模型支持任意，取保守值防内存暴涨）

        /// <summary>创建并加载 NuGet 内置模型；失败返回 null（调用方回退 Windows OCR）。</summary>
        public static RapidOcrEngine? Create()
        {
            try
            {
                var ocr = new RapidOcr();
                // InitModels() 默认按"当前工作目录"找模型——必须传绝对路径
                var v5 = System.IO.Path.Combine(AppContext.BaseDirectory, "models", "v5");
                if (System.IO.File.Exists(System.IO.Path.Combine(v5, "ch_PP-OCRv5_mobile_det.onnx")))
                {
                    ocr.InitModels(
                        detPath: System.IO.Path.Combine(v5, "ch_PP-OCRv5_mobile_det.onnx"),
                        clsPath: System.IO.Path.Combine(v5, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                        recPath: System.IO.Path.Combine(v5, "latin_PP-OCRv5_rec_mobile_infer.onnx"),
                        keysPath: System.IO.Path.Combine(v5, "ppocrv5_latin_dict.txt"));
                }
                else
                {
                    ocr.InitModels();
                }
                return new RapidOcrEngine(ocr);
            }
            catch (Exception ex)
            {
                LastCreateError = ex.Message;
                return null;
            }
        }

        public IOcrEngine CreatePeer()
        {
            try
            {
                var ocr = new RapidOcr();
                ocr.InitModels();
                return new RapidOcrEngine(ocr);
            }
            catch
            {
                return this; // 退化为共享实例：扫描线程串行使用时依然安全
            }
        }

        public unsafe Task<List<OcrLineResult>> RecognizeAsync(byte[] bgra, int w, int h)
        {
            // RapidOcrNet 输入是 SKBitmap；BGRA 以固定指针零拷贝装填
            return Task.Run(() =>
            {
                var result = new List<OcrLineResult>();
                try
                {
                    var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    using var bmp = new SKBitmap();
                    fixed (byte* p = bgra)
                    {
                        bmp.InstallPixels(info, (IntPtr)p, info.RowBytes);
                        var ocrResult = _ocr.Detect(bmp, RapidOcrOptions.Default);
                        if (ocrResult?.TextBlocks == null) return result;
                        foreach (var block in ocrResult.TextBlocks)
                        {
                            var text = block.Text?.Trim();
                            if (string.IsNullOrEmpty(text) || block.BoxPoints == null) continue;
                            double x0 = double.MaxValue, y0 = double.MaxValue,
                                   x1 = double.MinValue, y1 = double.MinValue;
                            foreach (var p2 in block.BoxPoints)
                            {
                                x0 = Math.Min(x0, p2.X); y0 = Math.Min(y0, p2.Y);
                                x1 = Math.Max(x1, p2.X); y1 = Math.Max(y1, p2.Y);
                            }
                            result.Add(new OcrLineResult
                            {
                                Text = text,
                                X = x0, Y = y0,
                                Width = x1 - x0, Height = y1 - y0
                            });
                        }
                    }
                }
                catch { }
                return result;
            });
        }
    }
}
