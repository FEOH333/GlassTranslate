<div align="center">

# 🔮 GlassTranslate

**Lightweight real-time AI screen translator for Windows · Liquid-glass floating orb · OpenAI / Anthropic streaming**

[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/FEOH333/GlassTranslate)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Dependencies](https://img.shields.io/badge/dependencies-0%20nuget-green)](#)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](LICENSE)
[![Release](https://img.shields.io/badge/release-v1.0.0-orange)](../../releases)

[中文](README.md) · **English**

</div>

---

## What is it

If you work all day with **English software, English UIs and English documents**, GlassTranslate keeps a tiny hollow glass orb on your screen that:

- **Continuously detects non-Chinese sentences** anywhere on screen and prints the Chinese translation **right next to the sentence**, streamed character-by-character (typewriter effect);
- Or translates the **whole screen once on a hotkey** — press again to dismiss;
- Overlays are fully **click-through** — they never block you;
- Supports **OpenAI** and **Anthropic** (Claude) official APIs and any compatible relay/gateway, with multi-layer caching to minimize cost.

## Install

### Option 1: Download (recommended)

Grab `GlassTranslate-vX.Y.Z-win-x64.zip` from [Releases](../../releases), unzip, double-click
`GlassTranslate.exe`. **No .NET runtime required** (self-contained, ~70MB).

### Option 2: Build from source

Requires the .NET 8 SDK.

```bash
git clone https://github.com/FEOH333/GlassTranslate.git
cd GlassTranslate
dotnet build -c Release          # build
dotnet run -c Release            # run (dev)
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish   # publish
```

## Quick start (3 steps)

1. **Left-click the orb** → **⚙ 配置 API** (Configure API)
2. Pick a provider (DeepSeek / Qwen / Kimi / GLM / OpenAI / Anthropic / custom), paste your **API key** (the base URL accepts relay endpoints), click **测试连接** (Test connection) until it shows ✓, then save
3. Click the orb again → enable **启用实时全屏翻译** (Realtime full-screen translation) — English sentences on screen now get live Chinese translations beside them

> If nothing happens on first use, see [OCR prerequisite](#ocr-prerequisite-one-time-setup).

## Interaction guide

| Action | Orb state | Panel state |
|---|---|---|
| Left click | Expands into the panel | — |
| Drag | Move the orb (position persisted) | Drag empty areas / the title row to move the panel |
| Right click | Hide orb / Exit | — |
| Mouse leaves | — | Auto-collapses after **5 s**, or click **✕** to collapse immediately |
| Hover | Scale + glow animation | — |

Panel controls:

- **⚙ 配置 API** — settings window (provider / key / base URL / model / test / cache)
- **● 启用实时全屏翻译** — realtime detection & inline streaming translation
- **● 快捷键全屏翻译 + `Ctrl+Alt+T` box** — click the box to **record any hotkey** (Esc to cancel; must include Ctrl/Alt/Win, or an F1-F24 key). Press the hotkey to translate the whole screen once; press again to dismiss

The **tray icon** keeps every function available even when the orb is hidden (right-click for the menu, double-click to show the orb).

## OCR prerequisite (one-time setup)

Uses the built-in Windows OCR engine (free, fast). Most machines already have the *English (US) OCR* component. Verify with:

```bash
GlassTranslate.exe --selftest        # headless check of capture/OCR/filters/cache
GlassTranslate.exe --selftest --api  # additionally test your configured API
```

If OCR is missing: **Windows Settings → Apps → Optional features → Add a feature → search "Optical character recognition" → install English (US) OCR**.

## API configuration

**8 built-in provider presets** (one click in the settings window; official base URL and model list fill in automatically; models current as of Oct 2026):

| Preset | Protocol | Default base URL | Models (default in bold) | Get key |
|---|---|---|---|---|
| **DeepSeek** | OpenAI-compatible | `https://api.deepseek.com` | **deepseek-v4-flash**, deepseek-v4-pro | platform.deepseek.com |
| **Tongyi Qwen (Alibaba)** | OpenAI-compatible | `https://dashscope.aliyuncs.com/compatible-mode/v1` | **qwen3.8-flash**, qwen3.7-plus, qwen3.8-max | bailian.console.aliyun.com |
| **Kimi (Moonshot AI)** | OpenAI-compatible | `https://api.moonshot.cn/v1` | **kimi-k2.6**, kimi-k3 | platform.kimi.com |
| **GLM (Zhipu BigModel)** | OpenAI-compatible | `https://open.bigmodel.cn/api/paas/v4` | **glm-5.3-flash**, glm-4.7-flash (free), glm-5.3 | bigmodel.cn |
| **OpenAI** | OpenAI | `https://api.openai.com/v1` | **gpt-5.2**, chat-latest | platform.openai.com |
| **Anthropic** | Anthropic | `https://api.anthropic.com` | **claude-haiku-5-5**, claude-sonnet-5-5, claude-opus-5-5 | console.anthropic.com |
| Custom (OpenAI protocol) | OpenAI-compatible | any relay/gateway | free-form | — |
| Custom (Anthropic protocol) | Anthropic-compatible | any relay/gateway | free-form | — |

> Default models are picked for translation duty: cheap, fast first token. Kimi's k2-series and moonshot-v1 models were all retired in Aug 2026; DeepSeek's deepseek-chat/reasoner were deprecated in Jul 2026.

Key points:

- **OpenAI / Anthropic are *protocols*, not vendors** — most Chinese and international model services expose OpenAI-compatible endpoints; pick the preset, paste that vendor's key, done.
- Switching presets auto-fills the official base URL and model list; model names remain free-form (the dropdown is just presets). Key / URL / model are remembered per provider during a session, so switching back and forth never re-pastes keys.
- Any base URL can be replaced by a relay (suffixes like `/v1`, `/chat/completions`, `/v1/messages`, `/api/paas/v4` are all recognized).
- API keys are encrypted with **Windows DPAPI** (current user) — no plaintext in `%APPDATA%\GlassTranslate\config.json`.
- Defaults to **DeepSeek** (works without a proxy in China, cheap); GLM's Flash-series models are free.
- The static system prompt also benefits from each vendor's prompt-cache pricing (Anthropic explicit `cache_control` ≈ 90% off on hits, OpenAI prefix caching ≈ 50% off, DeepSeek automatic context caching ≈ 90% off), on top of the local sentence cache.

## How cost is minimized

1. **Local sentence cache** — every finished translation is persisted with a normalized key (case/whitespace-insensitive); repeats cost **zero API calls** and display instantly.
2. **Prompt prefix caching** — the system prompt is a fixed static text (>1024 tokens, with a UI/software glossary). Anthropic gets explicit `cache_control: ephemeral` (10% price on hits), OpenAI automatic prefix caching (50%).
3. **Batching** — new sentences inside a 500 ms window are merged into a single request (up to 16 lines).
4. **Smart skipping** — pixel-hash change detection skips OCR entirely on static screens; pure numbers/paths/URLs/code identifiers/already-Chinese text never reach the API.
5. **Streaming** — SSE deltas are shown as they arrive; perceived latency ≈ time-to-first-token.

## Architecture

```
App.xaml / AppController      entry & wiring
Platform/                     all Win32 interop in one place (capture, tray, hotkeys, DPAPI, window effects)
Core/                         WPF-free background logic (pipeline, OCR, filters, cache, batching, providers)
UI/                           WPF views (orb morphing, click-through overlays, typewriter tooltips, settings)
```

See the (Chinese) main README for a detailed module map and data-flow diagram — code identifiers and file names are in English, so it reads fine with a translator. 😉

## FAQ

- **Nothing happens after enabling realtime mode** — run `--selftest`; check for API error toasts; sentences need ≥3 Latin letters (noise like "OK" is filtered on purpose).
- **Hotkey registration fails** — the combo is taken by another app; re-record via the panel box.
- **Bubbles overlap graphics** — placement currently avoids OCR-visible text only (known limitation, see Roadmap).
- **Key needs re-entering on a new PC** — DPAPI binds the secret to the current Windows user, by design.

## Roadmap

- [ ] Avoid graphic elements during placement
- [ ] Multiple bubbles for repeated sentences; draggable/pinned bubbles
- [ ] SQLite cache backend
- [ ] Exponential backoff / 429 handling
- [ ] More target languages
- [ ] Autostart

## Contributing

Issues and PRs welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports should include
`selftest.log` and `%APPDATA%\GlassTranslate\error.log`.

## License

[MIT](LICENSE) © 2026 FEOH333 — inspired by [pot-desktop](https://github.com/pot-app/pot-desktop),
[STranslate](https://github.com/STranslate/STranslate), [LunaTranslator](https://github.com/HIllya51/LunaTranslator)
and [openai-translator](https://github.com/yetone/openai-translator).
