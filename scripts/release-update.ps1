# v1.0.6 release 更新：替换两个 exe 资产 + body 追加生态修缮章节
# ASCII-only 脚本（中文文案从外部 UTF-8 文件读入，规避 PowerShell 5.1 无 BOM 编码坑）
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$cred = "protocol=https`nhost=github.com`n" | git credential fill | Select-String "^password=" | ForEach-Object { $_.Line.Substring(9) }
$headers = @{ Authorization = "Bearer $cred"; "User-Agent" = "Lattice-Release"; Accept = "application/vnd.github+json" }

$rel = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/lattice1184/Lattice-Launcher/releases/tags/v1.0.6" -Headers $headers
$relId = $rel.id
Write-Host "release id: $relId"

# 1) 删除旧 exe 资产（README.txt 保留）
foreach ($name in @("Lattice-Launcher-Setup.exe", "Lattice-Launcher-Lite.exe")) {
    $asset = $rel.assets | Where-Object { $_.name -eq $name }
    if ($asset) {
        Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/lattice1184/Lattice-Launcher/releases/assets/$($asset.id)" -Headers $headers | Out-Null
        Write-Host "deleted: $name"
    }
}

# 2) 上传新资产（二进制，英文资产名）
$upl = "https://uploads.github.com/repos/lattice1184/Lattice-Launcher/releases/$relId/assets"
$files = @(
    @{ Local = Join-Path $root "..\发布\Lattice启动器.exe"; Name = "Lattice-Launcher-Setup.exe" },
    @{ Local = Join-Path $root "..\发布\Lattice启动器-轻量版.exe"; Name = "Lattice-Launcher-Lite.exe" }
)
foreach ($f in $files) {
    Invoke-RestMethod -Method Post -Uri "$upl`?name=$($f.Name)" -Headers $headers `
        -InFile $f.Local -ContentType "application/octet-stream" | Out-Null
    Write-Host "uploaded: $($f.Name) ($((Get-Item $f.Local).Length) bytes)"
}

# 3) body 追加生态修缮章节（现有 body + 新章节；UTF-8 bytes 提交防中文损坏）
$newSection = [IO.File]::ReadAllText((Join-Path $root "body-eco.md"), [Text.Encoding]::UTF8)
$fullBody = $rel.body + $newSection
$json = @{ body = $fullBody } | ConvertTo-Json -Depth 3
Invoke-RestMethod -Method Patch -Uri "https://api.github.com/repos/lattice1184/Lattice-Launcher/releases/$relId" `
    -Headers $headers -ContentType "application/json; charset=utf-8" `
    -Body ([Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
Write-Host "body updated, new length: $($fullBody.Length)"
Write-Host "DONE"
