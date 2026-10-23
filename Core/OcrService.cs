using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GlassTranslate.Core
{
    public sealed class OcrLineResult
    {
        public string Text = "";
        public double X, Y, Width, Height;
    }

    /// <summary>
    /// Windows 内置 OCR（免费、快、英文精度好）。优先 en-US 引擎，逐级回退。
    /// </summary>
    public sealed class OcrService
    {
        private readonly OcrEngine _engine;
        public string EngineLanguage { get; }

        private OcrService(OcrEngine engine, string lang)
        {
            _engine = engine;
            EngineLanguage = lang;
        }

        public int MaxDimension => (int)OcrEngine.MaxImageDimension;

        public static OcrService? Create()
        {
            try
            {
                foreach (var tag in new[] { "en-US", "en-GB", "en" })
                {
                    var lang = new Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang))
                    {
                        var e = OcrEngine.TryCreateFromLanguage(lang);
                        if (e != null) return new OcrService(e, tag);
                    }
                }
                var profile = OcrEngine.TryCreateFromUserProfileLanguages();
                if (profile != null) return new OcrService(profile, "user-profile");
                var any = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (any != null)
                {
                    var e = OcrEngine.TryCreateFromLanguage(any);
                    if (e != null) return new OcrService(e, any.LanguageTag);
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>识别一帧 BGRA 位图，返回每行文本与包围盒（位图像素坐标）。</summary>
        public async Task<List<OcrLineResult>> RecognizeAsync(byte[] bgra, int w, int h)
        {
            var lines = new List<OcrLineResult>();
            try
            {
                var bmp = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, w, h);
                var result = await _engine.RecognizeAsync(bmp).AsTask();
                foreach (var line in result.Lines)
                {
                    if (line.Words.Count == 0) continue;
                    double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                    foreach (var word in line.Words)
                    {
                        var r = word.BoundingRect;
                        x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y);
                        x1 = Math.Max(x1, r.X + r.Width); y1 = Math.Max(y1, r.Y + r.Height);
                    }
                    var text = line.Text.Trim();
                    if (text.Length == 0) continue;
                    lines.Add(new OcrLineResult { Text = text, X = x0, Y = y0, Width = x1 - x0, Height = y1 - y0 });
                }
                bmp.Dispose();
            }
            catch { }
            return lines;
        }
    }
}
