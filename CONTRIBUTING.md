# 贡献指南 / Contributing Guide

感谢你愿意为 GlassTranslate 出力!这个项目保持小而精:**零第三方 NuGet 依赖、分层清晰、可维护性优先**。
提交前请花两分钟读完本文。

## 开发环境 / Setup

| 依赖 | 版本 | 说明 |
|---|---|---|
| Windows | 10 19041+ / 11 | 仅支持 Windows(WPF + WinRT OCR) |
| .NET SDK | 8.0+ | 含 Windows Desktop 工作负载 |
| OCR | 英语(美国) 光学字符识别 | Windows 可选功能,见 README |

```bash
git clone https://github.com/FEOH333/GlassTranslate.git
cd GlassTranslate
dotnet build -c Release
dotnet run -c Release -- --selftest   # 无 GUI 自检,验证截屏/OCR/过滤器/缓存链路
```

## 分层约定 / Architecture Rules

```
Platform/  原生互操作(只放 P/Invoke 与封装,禁止业务逻辑)
Core/      纯后台逻辑(禁止引用 WPF 类型;可被任意宿主复用、可单测)
UI/        WPF 视图(禁止直接碰 Win32,统一走 Platform)
AppController  装配层(唯一的 UI ↔ Core 桥)
```

- **新增翻译服务商**:实现 `Core/Providers/ITranslationProvider`(参考 `OpenAiProvider`),
  在 `AppController.CreateProvider` 注册。不需要动 UI。
- **新增 OCR 引擎**:实现 `Core/OcrService` 同等接口的替代品即可,流水线不关心引擎来源。
- **新增 Win32 调用**:一律加到 `Platform/Win32.cs`,不要散落在别处。

## 提交规范 / Commits

- 用中文或英文均可,格式:`<类型>: <做了什么>`,如 `fix: 修复面板拖拽阈值误判`、`feat: 支持 SQLite 缓存后端`。
- 一个 PR 只做一件事;重构和行为变更分开提交。

## 报 Bug / Bug Reports

请使用 Issue 模板,并附上:

1. `GlassTranslate.exe --selftest` 的输出(或 exe 旁 `selftest.log`)
2. `%APPDATA%\GlassTranslate\error.log`(如有)
3. 系统版本、DPI 缩放比例、单/多显示器

> 这些日志不包含 API Key(密钥只以 DPAPI 密文落盘),可放心粘贴。
> 但请自行检查截图中是否含敏感内容。

## 行为准则 / Code of Conduct

保持友善、对事不对人。开源是业余时间的事,回复慢请多包涵。
