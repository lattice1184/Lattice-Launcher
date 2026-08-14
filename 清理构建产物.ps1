# 一键清理构建产物（bin/obj）——构建/发布会自动重建，随时可删。
# 占大头的是 Launcher.App/bin 的 libSkiaSharp.pdb 调试符号（约 80MB×4 架构）与程序集副本。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$before = (Get-ChildItem -Path $root -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum / 1MB
$dirs = Get-ChildItem -Path $root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("bin", "obj") -and $_.FullName -notmatch "node_modules" }
$count = 0
foreach ($d in $dirs) {
    Remove-Item -Path $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
    $count++
}
$after = (Get-ChildItem -Path $root -Recurse -Force -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "已清理 $count 个 bin/obj 目录：$([math]::Round($before - $after, 1)) MB 释放（$([math]::Round($before, 1)) -> $([math]::Round($after, 1)) MB）"
Write-Host "下次 dotnet build / 发布.ps1 会自动重建。"
