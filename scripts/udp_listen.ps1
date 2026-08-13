# LAN 广播探测：对每个 IPv4 网卡 join MC 组播组 224.0.2.60:4445，15 秒内收 MC 的局域网广播
# 用法：游戏内开好「对局域网开放」后运行：powershell -ExecutionPolicy Bypass -File scripts/udp_listen.ps1
$ErrorActionPreference = "Continue"
$mcast = [System.Net.IPAddress]::Parse("224.0.2.60")

# 本机 IPv4 接口（按 terracotta 日志顺序）
$ifs = @("26.138.121.8","172.31.64.1","192.168.1.40","169.254.130.15")
$socks = @()

Write-Host "=== 监听 MC 局域网广播 (224.0.2.60:4445) 15 秒 ==="
foreach ($ip in $ifs) {
    try {
        $u = New-Object System.Net.Sockets.UdpClient
        $u.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::ReuseAddress, $true)
        $u.Client.Bind((New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 4445)))
        $u.JoinMulticastGroup($mcast, [System.Net.IPAddress]::Parse($ip))
        $u.Client.ReceiveTimeout = 15000
        $socks += [PSCustomObject]@{ If = $ip; Udp = $u }
        Write-Host "  [ok] 接口 $ip 已加入组播组监听"
    } catch {
        Write-Host "  [!!] 接口 $ip join 失败: $($_.Exception.Message)"
    }
}

$deadline = (Get-Date).AddSeconds(15)
$any = $false
while ((Get-Date) -lt $deadline -and $socks.Count -gt 0) {
    foreach ($s in $socks) {
        try {
            $ep = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
            $data = $s.Udp.Receive([ref]$ep)
            $any = $true
            $text = [System.Text.Encoding]::UTF8.GetString($data)
            Write-Host ("  [收到] 监听接口 $($s.If) <- 源 $($ep.Address): $text")
        } catch { }
    }
    Start-Sleep -Milliseconds 300
}
if (-not $any) { Write-Host "!! 15 秒内任何接口都没收到 MC 广播（MC 侧或网络栈问题）" }
foreach ($s in $socks) { $s.Udp.Close() }
Write-Host "=== 探测结束 ==="
