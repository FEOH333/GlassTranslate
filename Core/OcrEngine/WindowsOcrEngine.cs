using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace GlassTranslate.Core.OcrEngine
{
    /// <summary>Windows 内置 OCR（免费、快）；作为 RapidOCR 不可用时的回退引擎。</summary>
    public sealed class WindowsOcrEngine : IOcrEngine
    {
        private readonly Windows.Media.Ocr.OcrEngine _engine;
        public string EngineName => $"Windows OCR ({_lang})";
        private readonly string _lang;

        private WindowsOcrEngine(Windows.Media.Ocr.OcrEngine engine, string lang)
        {
            _engine = engine;
            _lang = lang;
        }

        public int MaxDimension => (int)Windows.Media.Ocr.OcrEngine.MaxImageDimension;

        public static WindowsOcrEngine? Create()
        {
            try
            {
                foreach (var tag in new[] { "en-US", "en-GB", "en" })
                {
                    var lang = new Language(tag);
                    if (Windows.Media.Ocr.OcrEngine.IsLanguageSupported(lang))
                    {
                        var e = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang);
                        if (e != null) return new WindowsOcrEngine(e, tag);
                    }
                }
                var profile = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                if (profile != null) return new WindowsOcrEngine(profile, "user-profile");
                var any = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (any != null)
                {
                    var e = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(any);
                    if (e != null) return new WindowsOcrEngine(e, any.LanguageTag);
                }
                return null;
            }
            catch { return null; }
        }

        public IOcrEngine CreatePeer() => this; // WinRT OCR 引擎本身可并发（文档保证）

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
