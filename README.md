<div align="center">

# 🔮 GlassTranslate 玻璃翻译

**纯本地离线的 Windows 实时 AI 全屏翻译 · 液态玻璃悬浮球 · 零网络代码**

Fully offline real-time screen translator for Windows. Recognition and translation
both run **locally** — the binary contains **zero networking code**.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/FEOH333/GlassTranslate)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Offline](https://img.shields.io/badge/100%25-offline-success)](#-隐私)
[![Zero Network](https://img.shields.io/badge/network%20code-zero-brightgreen)](#-隐私)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](LICENSE)
[![Release](https://img.shields.io/badge/release-v2.0.0-orange)](../../releases)

**中文** · [English](README.en.md)

</div>

---

## ✨ 这是什么

面对满屏英文软件/文档，GlassTranslate 在屏幕上留一颗小小的"空心玻璃球"：

- **实时检测**屏幕上的英文语句，在句子旁边的空白处**逐字打出**中文译文（打字机效果）
- 或按**快捷键**整屏翻译一次，再按一次消失
- 译文气泡**点击穿透**，不挡任何操作
- **v2.0 起完全本地化**：OCR 用 PP-OCRv5，翻译用 Opus-MT 专用翻译模型——
  不需要 API Key、不联网、零成本、无限量，屏幕内容**永不离开你的电脑**

## 🔒 隐私

- **零网络代码**：v2.0 起删除了全部云端 API 代码（可审计：全仓库无 HttpClient/Socket 等任何网络 API）
- 识别（OCR）与翻译（Opus-MT 推理）全部在本机 CPU 完成
- 本地数据仅两样：句库缓存 `%APPDATA%\GlassTranslate\translation-cache.json`（你的翻译历史，可随时清空）与用量计数——不上传、不存在服务器，因为根本没有服务器
- 旧版的 API Key 字段已在 v2.0 移除；升级后旧配置中的密钥字段被直接丢弃

## 📥 安装

### 方式一：直接下载（推荐）

到 [Releases](../../releases) 下载 `GlassTranslate-vX.Y.Z-win-x64.zip`（约 180MB，
内置全部模型），解压后双击 `GlassTranslate.exe`。**无需 .NET、无需配置、无需联网**。

### 方式二：从源码构建

```bash
git clone https://github.com/FEOH333/GlassTranslate.git
cd GlassTranslate

# 拉取翻译模型（OCR 模型随 NuGet 自动附带；约 110MB，仅需一次，走国内镜像）
powershell -ExecutionPolicy Bypass -File tools/download-models.ps1

dotnet build -c Release
dotnet run -c Release
```

环境自检（离线，输出双 OCR 引擎行数/耗时对比 + 本地翻译实测）：

```bash
GlassTranslate.exe --selftest
```

## 🚀 快速开始

**解压 → 双击 → 左键点悬浮球 → 开启"启用实时全屏翻译"**。没有第二步——不需要任何账号或密钥。

## 🖱️ 交互指南

| 操作 | 球态 | 面板态 |
|---|---|---|
| 左键点按 | 向两端展开成面板 | —— |
| 按住拖动 | 移动悬浮球（位置记忆） | 按住空白处/标题行移动面板 |
| 右键 | 清除译文 / 隐藏 / 退出 | —— |
| 中键 | 一键清除全部译文 | —— |
| 鼠标移开 | —— | 5 秒自动缩回，或点 ✕ 立即收起 |
| 悬停 | 放大 + 高光动效 | —— |

面板三按钮：**⚙ 引擎与设置** / **启用实时全屏翻译** / **快捷键全屏翻译 + 可录制快捷键框**。
托盘图标保留全部功能，隐藏悬浮球后仍可使用。

## ⚙️ 本地引擎

| 引擎 | 模型 | 体积 | 速度（参考，CPU） |
|---|---|---|---|
| **RapidOCR**（默认） | PP-OCRv5 det+cls+rec（Latin，ONNX int8） | ~15MB | 整帧识别数百 ms；分格差异扫描下仅识别变化区域 |
| Windows OCR（回退） | 系统内置 en-US | 0 | ~100ms/帧 |
| **Opus-MT en→zh** | 专用神经翻译模型（int8 ONNX，非 LLM） | ~110MB | 单句 200-300ms；句库缓存命中的句子零推理 |

- OCR 引擎可在设置中切换（自动/RapidOCR/Windows），默认自动选择
- 翻译引擎启动时自动校准解码参数，对不同导出变体稳健
- 分词器为自研 Unigram SentencePiece 编解码器（与模型词表严格一致）

## 🧠 智能行为

**响应性**：150ms 运动探针（低清帧哈希，不含层叠窗口）——画面一变立即扫描，新文字约 0.3 秒被发现；滚动时 4×4 分格差异 OCR 只重识别滚动带，气泡平滑跟随；文字消失/关页，气泡随即退场。

**降噪**：任务栏整带排除；图标/图形 OCR 乱码（无元音"真词"）过滤；代码/终端/日志行识别（括号、函数调用、snake_case、error/warning 前缀）一律不翻；密集画面自动切严格模式（只翻"句子样"文本，单屏上限 32 气泡）；**工具自身界面与译文气泡区域实时上报为不识别区**（永不自我翻译）；深色画面自动反色增强识别。

**成本**：全部本地推理，无任何费用与配额；句库缓存（大小写/空白/首尾标点归一化）让重复句子零推理。

## 🏗️ 架构

```
├── App.xaml(.cs) / AppController    入口与总装配（引擎选择/UI 接线/排除区上报）
├── Config/                          配置 + 无头 CLI（--config realtime=1 …）
├── Platform/                        Win32 互操作（截屏/托盘/热键/窗口效果）
├── Core/
│   ├── OcrEngine/                   IOcrEngine 接口 + RapidOcrEngine / WindowsOcrEngine
│   ├── LocalMt/                     LocalMtEngine（Opus-MT ONNX 编解码循环）
│   │                                + SpmUnigram（自研 Unigram SentencePiece）
│   ├── ScreenPipeline.cs            运动探针 + 分格差异 OCR + 噪声过滤
│   ├── TranslationDirector.cs       批处理/句库/失败重排
│   └── TranslationCache/UsageStats  本地句库与用量
└── UI/                              悬浮球/面板/点击穿透悬浮层/设置/Toast
```

## 📜 许可与致谢

代码 [MIT](LICENSE)；第三方组件与模型许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
感谢 [RapidOCR](https://github.com/RapidAI/RapidOCR)、[RapidOcrNet](https://github.com/BobLd/RapidOcrNet)、
[PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)、[Helsinki-NLP Opus-MT](https://github.com/Helsinki-NLP/Opus-MT)
与 [Xenova](https://huggingface.co/Xenova) 的 ONNX 导出。

## ⬆️ 从 v1.x 升级

v2.0 是**破坏性变更**：云端 API 翻译（DeepSeek/Kimi/GLM/OpenAI/Anthropic 等）已全部移除。
若你依赖云端模型质量，请继续使用 [v1.5.1](../../releases/tag/v1.5.1)。配置与句库向前兼容，旧配置中的 API 字段会被丢弃。
