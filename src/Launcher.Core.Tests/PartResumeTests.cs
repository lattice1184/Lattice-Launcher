using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 片断点续传（AL67）：Modrinth 实测每次下载末尾断流（37.4/39MB 挂起）——心跳判死后的重试
/// 若整片重下 = 已下 95% 全浪费。部分片（中断残留/重试）从已下载长度续拉（Range from=have），
/// 且 206 才追加、200（服务器忽略 Range）重写防错位；进度从断点续走不归零。
/// </summary>
public class PartResumeTests
{
    private const long MB = 1024 * 1024;

    /// <summary>先吐 N 字节然后挂起等待取消（中断残留模拟）</summary>
    private sealed class PartialThenStallStream : Stream
    {
        private readonly int _initial;
        private bool _sent;

        public PartialThenStallStream(int initial) => _initial = initial;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_sent)
            {
                _sent = true;
                var n = Math.Min(_initial, count);
                Array.Fill(buffer, (byte)'P', offset, n);
                return n;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    /// <summary>按 Range start 记录请求次数；指定片第一次中断（吐 16KB 后挂起），重试给剩余</summary>
    private sealed class ResumeHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<long, int> _counts = new();
        private readonly long _total;
        public readonly List<(long Start, long End, int Count)> Requests = [];
        public readonly object Lock = new();

        /// <summary>测试指定：哪一片（按 Range start）会在第一次请求中断</summary>
        public long ResumeFrom = -1;
        public int StallChunkBytes = 16 * 1024;
        /// <summary>探测段延迟（毫秒）：调慢探测速度触发分片（秒回会判快源 → 单片，ResumeFrom 永不触发）</summary>
        public int ProbeDelayMs;
        /// <summary>true = 中断片的重试请求返回 200 全量（服务器忽略 Range——片超长应被拒绝换路）</summary>
        public bool RetryReturns200;

        public ResumeHandler(long total) => _total = total;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is null)
            {
                // 无 Range 请求 = 单连接（分片失败回退路径）→ 正常服务器语义：200 全量
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[_total]),
                };
            }
            var start = range.From!.Value;
            var end = range.To!.Value;
            var n = _counts.AddOrUpdate(start, 1, (_, v) => v + 1);
            lock (Lock) Requests.Add((start, end, n));

            var len = end - start + 1;
            if (start == 0 && n == 1 && ProbeDelayMs > 0)
                await Task.Delay(ProbeDelayMs, ct); // 探测段慢速 → 触发分片
            if (start == ResumeFrom && n == 1)
            {
                // 第一次请求该片：吐 16KB 后静默挂起（中断残留）
                return new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new StreamContent(new PartialThenStallStream(StallChunkBytes)),
                };
            }
            if (RetryReturns200 && start > ResumeFrom && start - ResumeFrom <= StallChunkBytes)
            {
                // 中断片的续传请求：服务器忽略 Range 返回 200 全量 → 片超长 → 引擎应拒绝换路
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[_total]),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(new byte[len]),
            };
        }
    }

    private static DownloadService CreateService(HttpMessageHandler handler, long stallMs = 300)
    {
        return new DownloadService(new HttpClient(handler), null, new DownloadOptions
        {
            MaxSourceAttempts = 1,
            ReadStallTimeoutMs = stallMs,
            SlowSpeedBps = 0, // 关慢速检测——单测专注续传语义
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true));
    }

    [Fact]
    public async Task PartialChunk_RetryResumesFromOffset()
    {
        // 2MB 单候选固定片 1MB = 2 片：片 1（Range start=1MB）第一次只吐 16KB 后挂起 → 心跳判死
        // → 重试应从 (1MB + 16KB) 续拉——断言重试 Range 起点，最终文件完整
        const long total = 2 * MB;
        var handler = new ResumeHandler(total) { ResumeFrom = total / 2, ProbeDelayMs = 2000 }; // 片 1（1MB 片边界）
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"resume1-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, _ => { }, CancellationToken.None);

            Assert.Equal(total, new FileInfo(dest).Length);
            // 片 1 重试请求：Range 从中断点续拉
            var resume = handler.Requests.FirstOrDefault(r => r.Start == total / 2 + 16 * 1024);
            Assert.True(resume.Count > 0,
                $"未从断点续传。Range 请求: {string.Join("; ", handler.Requests.Select(r => $"{r.Start}-{r.End}#{r.Count}"))}");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task PartialChunk_ProgressDoesNotReset()
    {
        // 残留片（have>0）入账 cp——进度从断点续走；最终合并 2MB 完整
        const long total = 2 * MB;
        var handler = new ResumeHandler(total) { ResumeFrom = total / 4, ProbeDelayMs = 2000 }; // 片 1
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"resume2-{Guid.NewGuid():N}.bin");
        var progress = new List<DownloadProgress>();
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, progress.Add, CancellationToken.None);

            Assert.Equal(total, new FileInfo(dest).Length);
            var last = progress[^1];
            Assert.True(last.FileBytesDone >= total - 32 * 1024, $"进度归零/少算：最后上报 {last.FileBytesDone}/{total}");
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public async Task ServerReturns200_OverlongChunk_SelfHealsViaSingleConnection()
    {
        // 服务器忽略 Range（200 全量）：片超长（2MB ≠ 512KB）→ 片长度校验触发分片失败
        // → 回退单连接自愈（真实服务器语义：单连接全量）→ 最终文件正确，绝不落错位文件
        const long total = 2 * MB;
        var handler = new ResumeHandler(total)
        {
            ResumeFrom = total / 4, // 片 1 中断
            ProbeDelayMs = 2000,
            RetryReturns200 = true,
        };
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"resume3-{Guid.NewGuid():N}.bin");
        try
        {
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, _ => { }, CancellationToken.None);
            Assert.Equal(total, new FileInfo(dest).Length);
        }
        finally
        {
            if (Directory.Exists(dest + ".parts")) Directory.Delete(dest + ".parts", true);
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public async Task Pause_MidDownload_KeepsCompletedChunks()
    {
        // 8-13 暂停归零修复：片 2 下载中用户暂停（取消）→ 新代码保留 .parts（旧代码 OCE 走通用
        // catch 清片集 → Resume 从零）→ Resume 后片 1 完整复用 + 片 2 从断点续传
        const long total = 3 * MB;
        var handler = new ResumeHandler(total) { ResumeFrom = MB, ProbeDelayMs = 2000 }; // 片 2（start=1MB）首次挂起
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"pause-{Guid.NewGuid():N}.bin");
        try
        {
            using var cts = new CancellationTokenSource();
            var first = svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, _ => { }, cts.Token);
            // 等片 2 挂起（探测 2s 慢速 → 片 1 完成后片 2 首次请求已发出）
            for (var i = 0; i < 600 && !handler.Requests.Any(r => r.Start == MB); i++) await Task.Delay(10);
            Assert.True(handler.Requests.Any(r => r.Start == MB), "片 2 应已发起请求（挂起中）");
            cts.Cancel();
            await Assert.ThrowsAsync<System.Threading.Tasks.TaskCanceledException>(() => first);

            // 暂停后片集保留：片 1 完整（1MB）——Resume 材料（旧代码被通用 catch 清掉 → 本断言失败）
            var part0 = Path.Combine(dest + ".parts", "0.part");
            Assert.True(File.Exists(part0) && new FileInfo(part0).Length == MB,
                "取消后已完成片应保留（暂停归零修复——Resume 从断点续，不从零）");

            // Resume：同 dest 重新下载 → 片 1 复用 → 完成
            await svc.DownloadFileAsync("https://example.com/f.bin", dest, null, total, _ => { }, CancellationToken.None);
            Assert.Equal(total, new FileInfo(dest).Length);
        }
        finally
        {
            if (Directory.Exists(dest + ".parts")) Directory.Delete(dest + ".parts", true);
            if (File.Exists(dest)) File.Delete(dest);
        }
    }
}
