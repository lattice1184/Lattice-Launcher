# 构建后自动签名：签名 TargetDir 下所有未签名 DLL/EXE（开发机自签名证书）
# 由 Directory.Build.targets 在 Build 后调用。证书不存在时自动创建。
param([string]$TargetDir)

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*LauncherDev*" } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=LauncherDev Cert" -CertStoreLocation Cert:\CurrentUser\My
}
$count = 0
foreach ($f in (Get-ChildItem "$TargetDir" -Include *.dll,*.exe -File -Recurse -ErrorAction SilentlyContinue)) {
    $sig = Get-AuthenticodeSignature $f.FullName
    if ($sig.Status -eq 'NotSigned') {
        try {
            Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -HashAlgorithm SHA256 -ErrorAction Stop | Out-Null
            $count++
        } catch { }
    }
}
if ($count -gt 0) { Write-Host "[sign-output] 已签名 $count 个文件: $TargetDir" }
