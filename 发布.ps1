# ============================================================
#  YanKa Launcher 一键发布
#  产物：发布\YanKa启动器.exe —— 单文件自包含，双击即用（无需安装 .NET）
#  用法：右键 → 使用 PowerShell 运行（或 powershell -ExecutionPolicy Bypass -File 发布.ps1）
# ============================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $root "发布"

Write-Host ""
Write-Host "=== YanKa Launcher 发布 ===" -ForegroundColor Cyan

# 1) 清空旧产物
if (Test-Path $out) { Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Path $out -Force | Out-Null

# 2) 单文件自包含发布（win-x64，含 native 运行库）
Write-Host "[1/3] dotnet publish（单文件自包含，约 2-4 分钟）..."
$stage = Join-Path $out "stage"
& dotnet publish (Join-Path $root "src/Launcher.App") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw "publish 失败 (exit $LASTEXITCODE)" }

# 3) 取 exe → 移到 发布\ 并重命名为友好名，清掉 stage 残留
$exe = Get-ChildItem (Join-Path $stage "*.exe") | Select-Object -First 1
$final = Join-Path $out "YanKa启动器.exe"
Move-Item $exe.FullName $final -Force
Remove-Item $stage -Recurse -Force

# 4) 签名（复用 LauncherDev 自签名证书；无证书时自动创建）
Write-Host "[2/3] 签名..."
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "scripts/sign-output.ps1") $out

# 5) 使用说明
$sizeMB = [Math]::Round((Get-Item $final).Length / 1MB)
@"
YanKa Launcher（闫卡启动器）
=============================
双击 [YanKa启动器.exe] 即可运行 —— 单文件自包含，无需安装 .NET 运行库。
首次启动会解压运行库到系统临时目录，需要几秒到十几秒，属正常现象。

构建时间：$(Get-Date -Format "yyyy-MM-dd HH:mm")
文件大小：约 $sizeMB MB
位置：$final
"@ | Out-File (Join-Path $out "使用说明.txt") -Encoding UTF8

Write-Host "[3/3] 完成！" -ForegroundColor Green
Write-Host "  -> $final"
Write-Host "  -> $(Join-Path $out '使用说明.txt')"
