<div align="center">

# 🔮 GlassTranslate

**Fully offline real-time screen translator for Windows · Liquid-glass orb · Zero networking code**

Recognition and translation both run **locally** (PP-OCRv5 + Opus-MT) — no API keys,
no cloud, no cost, and your screen content never leaves your machine.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/FEOH333/GlassTranslate)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Offline](https://img.shields.io/badge/100%25-offline-success)](#privacy)
[![Zero Network](https://img.shields.io/badge/network%20code-zero-brightgreen)](#privacy)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](LICENSE)
[![Release](https://img.shields.io/badge/release-v2.0.0-orange)](../../releases)

[中文](README.md) · **English**

</div>

---

## What it does

A tiny hollow glass orb sits on your screen and:

- **Detects English sentences anywhere on screen in real time**, streaming the Chinese
  translation character-by-character right beside the sentence
- Or translates the **whole screen once on a hotkey** — press again to dismiss
- Translation bubbles are fully **click-through**
- **v2.0 is fully local**: PP-OCRv5 for OCR + a dedicated Opus-MT translation model —
  no accounts, no keys, no network, no quotas

## Privacy

- **Zero networking code**: all cloud-API code was removed in v2.0 (auditable — the
  repository contains no HttpClient/Socket/any network API)
- OCR and translation run entirely on your CPU
- The only local data is your translation cache and a usage counter, both in
  `%APPDATA%\GlassTranslate\` — nothing is uploaded because there is no server

## Install

Download `GlassTranslate-vX.Y.Z-win-x64.zip` (~180MB with all models bundled) from
[Releases](../../releases), unzip, double-click. **No .NET install, no setup, no network.**

Build from source:

```bash
git clone https://github.com/FEOH333/GlassTranslate.git
cd GlassTranslate
powershell -ExecutionPolicy Bypass -File tools/download-models.ps1   # ~110MB MT model (China mirror)
dotnet build -c Release
GlassTranslate.exe --selftest    # offline check: dual-OCR benchmark + local translation
```

## Quick start

Unzip → double-click → left-click the orb → enable **实时全屏翻译**. That's it —
no account, no key.

## Local engines

| Engine | Model | Size | Speed (CPU, indicative) |
|---|---|---|---|
| **RapidOCR** (default) | PP-OCRv5 det+cls+rec (Latin, ONNX int8) | ~15MB | full-frame in hundreds of ms; cell-diff scanning only re-recognizes changed regions |
| Windows OCR (fallback) | built-in en-US | 0 | ~100ms/frame |
| **Opus-MT en→zh** | dedicated NMT model (int8 ONNX, not an LLM) | ~110MB | 200-300ms/sentence; cache hits cost zero |

The OCR engine is switchable in settings (auto / RapidOCR / Windows). The translation
engine auto-calibrates decoding conventions at startup. The tokenizer is a self-built
Unigram SentencePiece codec kept strictly consistent with the model vocabulary.

## Smart behavior

- **Motion-triggered**: a 150ms cheap probe (tiny-frame hash, layered windows excluded)
  fires a scan the moment pixels change; 4×4 cell-diff OCR re-recognizes only the
  changed band while scrolling; bubbles glide to follow their line; vanished text
  fades its bubble immediately
- **Noise control**: taskbar band excluded; icon/graphic OCR garbage filtered (vowelless
  pseudo-words); code/terminal/log lines never translated; dense scenes switch to strict
  mode (sentence-like text only, 32-bubble cap, no-cover placement); the tool's own UI
  and already-shown bubbles are reported back as no-OCR zones (no self-feedback); dark
  frames are inverted before recognition

## Upgrading from v1.x

v2.0 is a **breaking change**: all cloud API translation (DeepSeek/Kimi/GLM/OpenAI/
Anthropic) has been removed. If you need cloud-model quality, stay on
[v1.5.1](../../releases/tag/v1.5.1). Config and cache carry over; old API fields are dropped.

## License

MIT for code; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for component and
model licenses. Thanks to RapidOCR, RapidOcrNet, PaddleOCR, Helsinki-NLP Opus-MT and
Xenova's ONNX exports.
