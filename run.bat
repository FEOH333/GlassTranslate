@echo off
rem 开发运行：以 Release 产物启动（自动带上用户级 .NET 运行时路径）
setlocal
set DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet
start "" "%~dp0bin\Release\net8.0-windows10.0.19041.0\GlassTranslate.exe"
endlocal
