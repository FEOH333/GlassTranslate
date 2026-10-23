@echo off
rem 构建脚本：优先用 PATH 里的 dotnet，否则用用户目录安装的 .NET 8
setlocal
set DOTNET=dotnet
where dotnet >nul 2>nul || set DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
set DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet
echo 使用 %DOTNET% 构建...
"%DOTNET%" build -c Release
if errorlevel 1 (
  echo 构建失败
  exit /b 1
)
echo.
echo 构建成功：bin\Release\net8.0-windows10.0.19041.0\GlassTranslate.exe
echo 运行前若提示需要 .NET 运行时，请设置环境变量 DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet
echo 或直接运行 run.bat
endlocal
