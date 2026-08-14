using System.Net;

namespace Launcher.Core.Download;

/// <summary>
/// 共享 HTTP 连接池（AL45 下载提速 P0+P1）：
/// 旧实现每次 new SocketsHttpHandler → 连接池永不复用，每个文件一次 TCP+TLS 握手
/// （50 个库文件 = 50 次握手，同 host 每次省 50-200ms）。
/// 连接池在 handler 上；HttpClient 是轻量无状态包装，各服务 new HttpClient(SharedHandler) 即复用连接。
/// HTTP/2：CDN 为 HTTPS 走 ALPN 协商 h2；bmclapi 等旧服务器经 RequestVersionOrLower 自动降级 HTTP/1.1。
/// </summary>
public static class HttpClientPool
{
    /// <summary>共享 handler：连接池 + 连接参数 + HTTP/2 多路复用</summary>
    public static readonly SocketsHttpHandler SharedHandler = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),     // AL32：慢源 5s 判死（原 15s 直连卡 TCP/TLS）
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),   // 防陈旧连接/DNS 变更
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2), // 突发下载期保持连接热
        EnableMultipleHttp2Connections = true,         // HTTP/2 同 host 多连接
    };

    /// <summary>共享 client（DownloadService.CreateClient 用；默认请求版本 HTTP/2，服务器不支持自动降级）</summary>
    public static readonly HttpClient Shared = CreateShared();

    private static HttpClient CreateShared()
    {
        var client = new HttpClient(SharedHandler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>创建带 15s 请求超时的 HttpClient（生态 API 用——默认 100s 超时会让慢源拖死整页）</summary>
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var client = new HttpClient(SharedHandler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// 8-18 浏览器格式 UA：ghproxy.net 实测对非浏览器 UA（YanKa-Launcher/0.1）返回 403——
    /// 镜像候选实际不可用，大文件只剩 gh-proxy.com 一个镜像。带浏览器前缀 + 保留本启动器标识
    /// （CurseForge 要求 UA 含联系信息）。全仓无 UA 读取/校验逻辑，改动低风险。
    /// </summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 YanKa-Launcher/0.1";
}
