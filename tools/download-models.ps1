# 拉取本地翻译模型到 models/ 目录
# OCR 模型无需下载（RapidOcrNet NuGet 自带 PP-OCRv5，构建时自动复制）
# 翻译模型：Opus-MT en->zh int8 量化 ONNX（经 hf-mirror.com 镜像，国内可达）
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
$root = Split-Path $PSScriptRoot -Parent
$mt  = Join-Path $root "models\mt"
New-Item -ItemType Directory -Force -Path $mt | Out-Null

function Get-File($url, $dest) {
    if ((Test-Path $dest) -and (Get-Item $dest).Length -gt 1024) {
        Write-Host "skip (exists): $(Split-Path $dest -Leaf)"
        return
    }
    Write-Host "downloading: $url"
    $tmp = "$dest.tmp"
    try { Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -TimeoutSec 600 }
    catch { Write-Host "  FAILED: $($_.Exception.Message)"; if (Test-Path $tmp) { Remove-Item $tmp }; return }
    Move-Item $tmp $dest -Force
    Write-Host ("  ok: {0:N1} MB" -f ((Get-Item $dest).Length / 1MB))
}

$hf = "https://hf-mirror.com/Xenova/opus-mt-en-zh/resolve/main"
Get-File "$hf/onnx/encoder_model_quantized.onnx" (Join-Path $mt "encoder.onnx")
Get-File "$hf/onnx/decoder_model_quantized.onnx"  (Join-Path $mt "decoder.onnx")
Get-File "$hf/tokenizer.json"                     (Join-Path $mt "tokenizer.json")

Write-Host "`nmodels ready:"
Get-ChildItem -Recurse $root\models -File | Select-Object Name, @{n='MB';e={"{0:N1}" -f ($_.Length/1MB)}}
