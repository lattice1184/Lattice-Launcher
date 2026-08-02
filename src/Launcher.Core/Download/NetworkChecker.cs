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
}
