using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>竞速淘汰制（AL59）：评估点无赢家 → 取消非领先源——龟速源（限并发镜像）不再拖死竞速</summary>
public class RaceEliminateTests
{
    [Fact]
    public async Task SlowSource_Eliminated_AfterInterval()
    {
        var handler = new SlowHandler();
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://fast.com/f.bin", "http://slow.com/f.bin"]);
        var svc = new DownloadService(http, resolver, new DownloadOptions
        {
            MaxSourceAttempts = 1,
            RaceEliminateInterval = TimeSpan.FromMilliseconds(100),
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"elim-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            // progress 非 null：淘汰评估激活（perSourceProgress 依赖它）
            await svc.DownloadFileAsync("http://fast.com/f.bin", dest, null, 6, _ => { }, CancellationToken.None);
            sw.Stop();

            Assert.Equal("SLOWOK", await File.ReadAllTextAsync(dest));       // 快源赢
            Assert.Contains("slow.com", handler.Cancelled);                  // 慢源被淘汰取消
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),               // 10s 慢源没拖死任务
                $"总耗时 {sw.Elapsed.TotalSeconds:F1}s 超过 3s——慢源未被淘汰");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task AllSourcesSlow_LeadingSourceSurvives()
    {
        // 全源都慢（都 3s）：评估点字节全 0 → 保留第一个源（pending[0]），其余取消
        var handler = new SlowHandler();
        var http = new HttpClient(handler);
        var resolver = new FixedResolver(["http://slow.com/a.bin", "http://slow.com/b.bin"]);
        var svc = new DownloadService(http, resolver, new DownloadOptions
        {
            MaxSourceAttempts = 1,
            RaceEliminateInterval = TimeSpan.FromMilliseconds(100),
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
        var dest = Path.Combine(Path.GetTempPath(), $"elim2-{Guid.NewGuid():N}.bin");
        try
        {
            var sw = Stopwatch.StartNew();
            await svc.DownloadFileAsync("http://slow.com/a.bin", dest, null, 6, _ => { }, CancellationToken.None);
            sw.Stop();

            Assert.Equal("SLOWOK", await File.ReadAllTextAsync(dest));
            Assert.Single(handler.Cancelled);                                // 只有第二个源被淘汰
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));               // 不拖到 6s（2 源并行）
        }
        finally
        {
            File.Delete(dest);
        }
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public readonly List<string> Cancelled = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var host = request.RequestUri!.Host;
            var delay = host == "slow.com" ? TimeSpan.FromSeconds(3) : TimeSpan.FromMilliseconds(300);
            if (request.Headers.Range is not null) return new HttpResponseMessage(HttpStatusCode.OK); // HEAD/探测兜底
            try
            {
                await Task.Delay(delay, ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("SLOWOK"u8.ToArray()),
                };
            }
            catch (OperationCanceledException)
            {
                lock (Cancelled) Cancelled.Add(host);
                throw;
            }
        }
    }

    // ---------- 8-13 PickRaceLeader（速度外推评估） ----------

    [Fact]
    public void PickRaceLeader_MirrorWithStableSpeed_WinsOverFrontLoadedCdn()
    {
        // 真机 8-12 PowerToys 271MB 场景：CDN 总量领先（200MB）但后段限速（窗口增量 1MB）；
        // 镜像总量少（50MB）但稳定 2MB/s（窗口增量 30MB）——预计剩余时间：CDN ≈1064s vs 镜像 ≈110s
        var total = 271L * 1024 * 1024;
        var mb = 1024L * 1024;
        var lead = DownloadService.PickRaceLeader(
            [200 * mb, 50 * mb],   // bytes
            [199 * mb, 20 * mb],   // lastBytes
            total, 15);

        Assert.Equal(1, lead); // 镜像胜——总量评估会错选 0（CDN）
    }

    [Fact]
    public void PickRaceLeader_MergingSource_ProtectedFromElimination()
    {
        // 已下完（bytes=total，合并中、增量 0）的源直接保留——弃它 = 弃已下完文件
        var total = 100L * 1024 * 1024;
        var mb = 1024L * 1024;
        var lead = DownloadService.PickRaceLeader(
            [total, 50 * mb],    // 源 0 已下完（合并中）
            [total, 40 * mb],    // 源 0 增量 0
            total, 15);

        Assert.Equal(0, lead);
    }

    [Fact]
    public void PickRaceLeader_AllStalled_FallsBackToTotalLeader()
    {
        // 全源无增量（卡死）→ 回退总量领先
        var lead = DownloadService.PickRaceLeader(
            [80, 120],  // bytes
            [80, 120],  // lastBytes（增量全 0）
            1000, 15);

        Assert.Equal(1, lead); // 总量最大者
    }

    [Fact]
    public void PickRaceLeader_TraditionalVersion_StillRespected()
    {
        // 快源领先（增量+总量都大）→ 正常选中（不回归）
        var lead = DownloadService.PickRaceLeader(
            [900, 100],  // bytes
            [800, 0],    // lastBytes
            1000, 15);

        Assert.Equal(0, lead);
    }

    [Fact]
    public void RaceKey_StablePerUrl()
    {
        // 8-13 片集键与 URL 绑定（同 URL 跨轮复用；候选顺序变化不影响）——哈希稳定性
        var u1 = "https://example.com/a.bin";
        var u2 = "https://example.com/b.bin";
        Assert.Equal(DownloadService.RaceKey(u1), DownloadService.RaceKey(u1));  // 同 URL 同键
        Assert.NotEqual(DownloadService.RaceKey(u1), DownloadService.RaceKey(u2)); // 不同 URL 不同键
        Assert.Equal(8, DownloadService.RaceKey(u1).Length);                     // 8 位 hex
    }

    private sealed class FixedResolver : IDlSourceResolver
    {
        private readonly string[] _urls;
        public FixedResolver(params string[] urls) => _urls = urls;
        public IReadOnlyList<string> Resolve(string officialUrl) => _urls;
    }
}
