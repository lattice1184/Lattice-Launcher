using System.Net;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>
/// AL34 回归：单候选源（maven.fabricmc.net 等无镜像映射 URL）等待响应头超时——
/// HttpClient.Timeout（默认 100s）抛 TaskCanceledException（OCE 但 token 未被请求）。
/// 修复前从单候选直连路径漏出 → 叶子任务 catch(OperationCanceledException) 误判"已取消"
/// （无错误、UI 不可重试、文件缺失；实机 08-09 探针 asm-9.10.1.jar 即此）。
/// </summary>
public class SingleCandidateTimeoutTests
{
    /// <summary>可控 stub：第 1 次调用抛 TaskCanceledException（模拟 HttpClient.Timeout，token 未请求），之后返回好字节</summary>
    private sealed class TimeoutThenSuccessHandler : HttpMessageHandler
    {
        private readonly byte[] _body = "12345"u8.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var calls = Interlocked.Increment(ref Calls);
            if (calls == 1) throw new TaskCanceledException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_body),
            });
        }

        /// <summary>调用次数（并发安全）</summary>
        public int Calls;
    }

    /// <summary>永远超时的 stub（各轮全部 TaskCanceledException）</summary>
    private sealed class AlwaysTimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            throw new TaskCanceledException();
        }

        public int Calls;
    }

    private static DownloadService CreateService(HttpMessageHandler handler)
    {
        // 单候选：maven.fabricmc.net 无镜像映射（BmclapiDlSourceMapper.Map 原样返回）→ resolver 去重后只有 [原 url]
        var resolver = new ResolvingDlSourceMapper(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());
        return new DownloadService(new HttpClient(handler), resolver, new DownloadOptions
        {
            MaxSourceAttempts = 3,
            BackoffProvider = _ => TimeSpan.Zero,
        }, Path.GetTempPath(), (_, _) => Task.FromResult(true)); // 跳过真实网络预检
    }

    [Fact]
    public async Task HeaderTimeout_IsRetried_AndSucceeds()
    {
        var handler = new TimeoutThenSuccessHandler();
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"timeout-{Guid.NewGuid():N}.jar");
        try
        {
            // 修复前：TaskCanceledException 漏出（叶子误判已取消），此调用直接抛——断言不达
            // AL39 后 maven.fabricmc.net 有镜像（双候选）——单候选场景改用 example.com（无镜像映射）
            await svc.DownloadFileAsync(
                "https://example.com/org/ow2/asm/asm/9.10.1/asm-9.10.1.jar",
                dest, null, 5, null, CancellationToken.None);

            Assert.Equal("12345", await File.ReadAllTextAsync(dest)); // 第 2 次尝试成功落盘
            Assert.Equal(2, handler.Calls);                           // 第 1 次超时 → 退避 → 第 2 次成功
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task HeaderTimeout_Exhausted_FailsWithTimeoutMessage()
    {
        var handler = new AlwaysTimeoutHandler();
        var svc = CreateService(handler);
        var dest = Path.Combine(Path.GetTempPath(), $"timeout-{Guid.NewGuid():N}.jar");
        try
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                svc.DownloadFileAsync("https://example.com/x.jar", dest, null, 5, null, CancellationToken.None));

            Assert.Contains("超时", ex.Message); // 转成可重试错误后才报，不能是 OCE 静默漏出
            Assert.Equal(3, handler.Calls);      // 3 轮全部超时后放弃
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task Leaf_UnexpectedOperationCanceled_FailsWithMessage()
    {
        // 叶子层防御：work 抛 OCE 但 token 未被请求（HttpClient 超时泄漏等）→ 失败带信息（可重试），
        // 而不是静默"已取消"（Error=null、UI RetryCommand 只认 Failed、文件缺失）
        SynchronizationContext.SetSynchronizationContext(null); // 测试环境可能残留 context → Post 排队不执行
        var mgr = new DownloadManager();
        var task = mgr.Enqueue("asm-9.10.1.jar", (_, _) => throw new TaskCanceledException());

        await task.Completion;
        // AL44：网络/超时类失败自动重试一次——等重试终态（重试中 State 短暂 Downloading）
        for (var i = 0; i < 100 && task.State != DownloadTaskState.Failed; i++) await Task.Delay(10);

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.NotNull(task.Error);
    }
}
