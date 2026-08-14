using System.Net;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>
/// AL29 H1：原子写入（tmp + rename）——下载中断只残留 .tmp，绝不出现「File.Exists 通过但内容半截」的 destPath；
/// 校验通过前旧 destPath 不被覆盖。
/// </summary>
public class AtomicWriteTests
{
    /// <summary>按 Range 切片返回固定字节流；无 Range 返回全量（stub 无真实网络）</summary>
    private sealed class RangeStubHandler : HttpMessageHandler
    {
        private readonly byte[] _body;
        public RangeStubHandler(string body) => _body = System.Text.Encoding.UTF8.GetBytes(body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = _body;
            var status = HttpStatusCode.OK;
            if (request.Headers.Range is { } r && r.Ranges.FirstOrDefault() is { } range && range.From is { } from)
            {
                var start = (int)from;
                var end = range.To is { } to ? (int)to + 1 : _body.Length;
                body = _body[start..end];
                status = HttpStatusCode.PartialContent;
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private static DownloadService CreateService(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        // 任意 host 都按官方源（单候选，不换镜像）+ 零退避 + 跳过真实网络预检
        var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
        return new DownloadService(http, resolver, new DownloadOptions
        {
            MaxSourceAttempts = 2,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
    }

    private static string TempDest() => Path.Combine(Path.GetTempPath(), $"atomic-{Guid.NewGuid():N}.jar");

    [Fact]
    public async Task ResumeFromTmp_CompletesAnd_NoTmpLeft()
    {
        var svc = CreateService(new RangeStubHandler("12345"));
        var dest = TempDest();
        // 模拟崩溃残留：tmp 已有前 2 字节 → 断点续传（Range from=2 → "345"）→ 5 字节齐 → rename
        File.WriteAllText(dest + ".tmp", "12");

        await svc.DownloadFileAsync("https://test/a.jar", dest, null, 5, null, CancellationToken.None);

        Assert.Equal("12345", File.ReadAllText(dest)); // 完整内容
        Assert.False(File.Exists(dest + ".tmp"));      // 无残留
    }

    [Fact]
    public async Task FailedDownload_LeavesNoDest_NoTmp()
    {
        var svc = CreateService(new RangeStubHandler("12")); // 源返回 2 字节但声明 size=5 → 校验必败
        var dest = TempDest();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            svc.DownloadFileAsync("https://test/b.jar", dest, null, 5, null, CancellationToken.None));

        Assert.False(File.Exists(dest));      // 校验失败不落真名
        Assert.False(File.Exists(dest + ".tmp")); // 半文件被清理
    }

    [Fact]
    public async Task ChunkedDownload_Succeeds_NoTmp_NoParts()
    {
        // 300KB ≥ 256KB 分片阈值；Range 切片 → 8 分片各得正确段 → 合并 → rename
        var svc = CreateService(new RangeStubHandler(new string('a', 300 * 1024)));
        var dest = TempDest();

        await svc.DownloadFileAsync("https://test/c.jar", dest, null, 300 * 1024, null, CancellationToken.None);

        Assert.True(File.Exists(dest));
        Assert.Equal(300 * 1024, new FileInfo(dest).Length);
        Assert.False(File.Exists(dest + ".tmp"));
        Assert.False(Directory.Exists(dest + ".parts"));
    }
}
