# 第三方组件与模型声明 / Third-Party Notices

本项目使用以下开源组件与模型（均随发布包分发）：

| 组件 | 许可证 | 用途 | 来源 |
|---|---|---|---|
| [RapidOcrNet](https://github.com/BobLd/RapidOcrNet) | Apache-2.0 | OCR 推理管线封装（含 SkiaSharp、PContourNet 依赖） | NuGet |
| [PP-OCRv5 模型](https://github.com/PaddlePaddle/PaddleOCR)（Latin det/cls/rec，ONNX） | Apache-2.0 | 文字检测与识别 | 随 RapidOcrNet NuGet 分发 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime)（Microsoft.ML.OnnxRuntime） | MIT | 本地模型推理引擎 | NuGet |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | MIT | 图像像素装填（RapidOcrNet 依赖） | NuGet |
| [Opus-MT en→zh](https://huggingface.co/Helsinki-NLP/opus-mt-en-zh)（Xenova int8 ONNX 导出） | CC-BY 4.0 | 英→中翻译模型（~110MB，位于 models/mt/） | 本地分发 |
| [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) | Apache-2.0 | PP-OCR 模型的原始训练项目（致谢） | — |
| [MarianNMT / OPUS](https://github.com/Helsinki-NLP/Opus-MT) | CC-BY 4.0 | 翻译模型的原始训练项目（致谢） | — |

分词器（Unigram SentencePiece 编解码）与全部分发/推理代码为本项目自研（MIT，见 [LICENSE](LICENSE)）。
