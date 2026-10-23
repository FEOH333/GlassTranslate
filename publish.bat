@echo off
rem 发布独立单文件 exe（无需安装 .NET 运行时，体积约 70-90MB）
rem 想要小体积依赖版：把 --self-contained true 改为 false（需目标机装有 .NET 8 Desktop Runtime）
setlocal
set DOTNET=dotnet
where dotnet >nul 2>nul || set DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
set DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet
echo 正在发布独立单文件版本...
"%DOTNET%" publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true ^
  -o "%~dp0publish"
if errorlevel 1 (
  echo 发布失败
  exit /b 1
)
echo.
echo 发布完成：publish\GlassTranslate.exe （双击即可运行，无需 .NET 运行时）
endlocal
