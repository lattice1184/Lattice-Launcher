# 把 LauncherDev 证书导入系统受信任根（WDAC 信任链诊断）
Import-PfxCertificate -FilePath C:\Temp\launcherdev.pfx -CertStoreLocation Cert:\LocalMachine\Root -Password (ConvertTo-SecureString 'test1234' -AsPlainText -Force)
Write-Host "=== 完成 ==="
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like '*LauncherDev*' } | Select-Object Subject
