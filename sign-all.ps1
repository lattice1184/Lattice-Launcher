# 一键签名：C:\Temp\mini 目录下所有未签名 DLL（开发机自签名证书）
$ErrorActionPreference = 'Stop'
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*LauncherDev*" } | Select-Object -First 1
if (-not $cert) { $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=LauncherDev Cert" -CertStoreLocation Cert:\CurrentUser\My }
Write-Host "证书: $($cert.Thumbprint)"
$dir = "C:\Temp\mini\bin\Debug\net10.0"
$signed = 0; $skipped = 0; $failed = 0
foreach ($f in (Get-ChildItem "$dir\*.dll")) {
    $sig = Get-AuthenticodeSignature $f.FullName
    if ($sig.Status -eq 'NotSigned') {
        try {
            Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -HashAlgorithm SHA256 | Out-Null
            Write-Host ("[签名] " + $f.Name)
            $signed++
        } catch {
            Write-Host ("[失败] " + $f.Name + " : " + $_.Exception.Message)
            $failed++
        }
    } else {
        Write-Host ("[跳过] " + $f.Name + " (" + $sig.Status + ")")
        $skipped++
    }
}
Write-Host "=== 完成: 签名 $signed 个, 跳过 $skipped 个, 失败 $failed 个 ==="
