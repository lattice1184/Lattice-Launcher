# 构建后自动签名：签名 TargetDir 下所有未签名 DLL/EXE（开发机自签名证书）
# 由 Directory.Build.targets 在 Build 后调用。证书不存在时自动创建。
param([string]$TargetDir)

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*LauncherDev*" } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=LauncherDev Cert" -CertStoreLocation Cert:\CurrentUser\My
}
$count = 0
$failed = 0
foreach ($f in (Get-ChildItem "$TargetDir" -Include *.dll,*.exe -File -Recurse -ErrorAction SilentlyContinue)) {
    $sig = Get-AuthenticodeSignature $f.FullName
    if ($sig.Status -eq 'NotSigned') {
        try {
            $args = @{ FilePath = $f.FullName; Certificate = $cert; HashAlgorithm = 'SHA256' }
            # 时间戳：自签名证书过期（2027-08-01）后签名不失效——无时间戳的签名到期即作废
            $r = $null
            try { $r = Set-AuthenticodeSignature @args -TimestampServer "http://timestamp.digicert.com" -ErrorAction Stop }
            catch { $r = Set-AuthenticodeSignature @args -ErrorAction Stop } # 时间戳服务器不可达 → 降级无时间戳
            # 验证真实结果——PS5.1 对 .NET 单文件（伪证书表 + bundle 附加）签名会 UnknownError「非 Win32 应用」
            # 且不抛异常（曾长期"假成功"），必须看返回 Status
            if ($r.Status -eq 'Valid') { $count++ }
            else { $failed++; Write-Host "[sign-output] 警告: $($f.Name) 签名未生效（$($r.Status) $($r.StatusMessage)）" }
        } catch { $failed++ }
    }
}
if ($count -gt 0) { Write-Host "[sign-output] 已签名 $count 个文件: $TargetDir" }
if ($failed -gt 0) { Write-Host "[sign-output] $failed 个文件签名失败（PS5.1 单文件限制，见 SESSION_NOTES AL58）" }
