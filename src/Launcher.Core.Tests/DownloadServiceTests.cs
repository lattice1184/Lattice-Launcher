using System.Net;
using System.Security.Cryptography;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 分片并发下载测试：本地 HttpListener 模拟支持 Range 的服务器，
/// 验证 10MB 文件分片下载后与源数据完全一致 + 幂等。
/// </summary>
public class DownloadServiceTests : IAsyncLifetime
{
    private const int Port = 18345;
    private HttpListener? _listener;
    private byte[] _payload = [];
    private byte[] _smallPayload = [];

    public Task InitializeAsync()
    {
        _payload = new byte[10 * 1024 * 1024];
        _smallPayload = new byte[64 * 1024];
        Random.Shared.NextBytes(_payload);
        Random.Shared.NextBytes(_smallPayload);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = ServeAsync();
        return Task.CompletedTask;
    }

    /// <summary>按 URL 路径路由到对应数据：/small.bin → 64KB，其余 → 10MB</summary>
    private byte[] PayloadFor(HttpListenerContext ctx)
        => ctx.Request.Url?.AbsolutePath == "/small.bin" ? _smallPayload : _payload;

    public Task DisposeAsync()
    {
        _listener?.Stop();
        _listener?.Close();
        return Task.CompletedTask;
    }

    private async Task ServeAsync()
    {
        while (_listener!.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            // 并发处理请求 + 异常隔离（客户端断开不拖垮循环）
            _ = Task.Run(async () =>
            {
                try
                {
                    var data = PayloadFor(ctx);
                    var range = ctx.Request.Headers["Range"];
                    if (range is not null && range.StartsWith("bytes="))
                    {
                        var spec = range["bytes=".Length..];
                        var parts = spec.Split('-');
                        var start = long.Parse(parts[0]);
                        var end = parts.Length > 1 && parts[1].Length > 0
                            ? long.Parse(parts[1])
                            : data.Length - 1;
                        ctx.Response.StatusCode = 206;
                        ctx.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{data.Length}");
                        ctx.Response.ContentLength64 = end - start + 1;
                        await ctx.Response.OutputStream.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)));
                    }
                    else if (ctx.Request.HttpMethod == "HEAD")
                    {
                        // HEAD：只返回长度，绝不能写 body（HttpListener 写 HEAD body 会挂起）
                        ctx.Response.ContentLength64 = data.Length;
                    }
                    else
                    {
                        ctx.Response.ContentLength64 = data.Length;
                        await ctx.Response.OutputStream.WriteAsync(data);
                    }
                }
                catch { /* 客户端断开 */ }
                finally
                {
                    try { ctx.Response.Close(); } catch { }
                }
            });
        }
    }

    private string TempPath() => Path.Combine(Path.GetTempPath(), $"dl-{Guid.NewGuid():N}.bin");

    [Fact]
    public async Task ChunkedDownload_ProducesIdenticalFile()
    {
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(_payload));
        var dest = TempPath();
        try
        {
            var svc = new DownloadService();
            await svc.DownloadFileAsync($"http://localhost:{Port}/big.bin", dest, sha1, _payload.Length);
            var actual = await File.ReadAllBytesAsync(dest);
            Assert.Equal(_payload, actual);
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task ChunkedDownload_SecondCall_IsIdempotent()
    {
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(_payload));
        var dest = TempPath();
        try
        {
            var svc = new DownloadService();
            await svc.DownloadFileAsync($"http://localhost:{Port}/big.bin", dest, sha1, _payload.Length);
            var first = await File.ReadAllBytesAsync(dest);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await svc.DownloadFileAsync($"http://localhost:{Port}/big.bin", dest, sha1, _payload.Length);
            sw.Stop();

            var second = await File.ReadAllBytesAsync(dest);
            Assert.Equal(first, second);
            Assert.True(sw.ElapsedMilliseconds < 500, $"幂等跳过应 <500ms，实际 {sw.ElapsedMilliseconds}ms");
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task SmallFile_SingleConnection_Works()
    {
        var small = _smallPayload;
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(small));
        var dest = TempPath();
        try
        {
            var svc = new DownloadService();
            await svc.DownloadFileAsync($"http://localhost:{Port}/small.bin", dest, sha1, small.Length);
            var actual = await File.ReadAllBytesAsync(dest);
            Assert.Equal(small, actual);
        }
        finally { File.Delete(dest); }
    }
}
