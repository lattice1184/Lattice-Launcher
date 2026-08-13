using System.Diagnostics;
using System.Net.Sockets;

namespace Launcher.Core.Download;

/// <summary>网络可达性检查：TCP 连接测试（443 端口，超时 3s）</summary>
public static class NetworkChecker
{
    public static async Task<bool> CheckAsync(IEnumerable<string> hosts, TimeSpan timeout, CancellationToken ct)
    {
        foreach (var host in hosts)
        {
            try
            {
                using var tcp = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                await tcp.ConnectAsync(host, 443, cts.Token);
                return true; // 任一可达即认为网络正常
            }
            catch
            {
                // 试下一个
            }
        }
        return false;
    }

    /// <summary>
    /// HTTP 探测（AL65 网络诊断）：HEAD 请求计时（毫秒；-1 = 不可达）——比 TCP 通断更接近真实可用性
    /// （TCP 通但 HTTP 挂/被墙的情况，如 github.com TCP 可达但请求超时）。共享连接池 + UA。
    /// </summary>
    public static async Task<long> ProbeHttpAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var sw = Stopwatch.StartNew();
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await HttpClientPool.Shared.SendAsync(req, cts.Token);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
