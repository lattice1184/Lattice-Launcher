using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>全局并发门（AL65）：设置「最大并发下载数」>0 时任务排队串行，多任务不无限并行抢带宽</summary>
public class DownloadManagerGateTests
{
    private static DownloadManager Create(int maxConcurrent)
        => new(null, maxConcurrentDownloads: maxConcurrent);

    [Fact]
    public async Task Gate1_SerializesTasks()
    {
        var manager = Create(1);
        var running = 0;
        var maxRunning = 0;

        async Task Work(DownloadProgressHandler _, CancellationToken ct)
        {
            Interlocked.Increment(ref running);
            var cur = Interlocked.CompareExchange(ref maxRunning, 0, 0) + 0; // 读
            while (true)
            {
                var seen = Volatile.Read(ref running);
                var prev = Volatile.Read(ref maxRunning);
                if (seen <= prev) break;
                if (Interlocked.CompareExchange(ref maxRunning, seen, prev) == prev) break;
            }
            await Task.Delay(200, ct);
            Interlocked.Decrement(ref running);
        }

        var t1 = manager.Enqueue("a", Work);
        var t2 = manager.Enqueue("b", Work);
        await Task.WhenAll(t1.Completion, t2.Completion);

        Assert.Equal(1, Volatile.Read(ref maxRunning)); // 从未同时跑 2 个
    }

    [Fact]
    public async Task Gate2_RunsTwoInParallel()
    {
        var manager = Create(2);
        // 同步闸：两个 work 都进入后即断言——消除线程池调度竞态
        // （旧断言 maxRunning==2：t2 启动晚于 t1 结束时 running 到不了 2——并行负载下 flaky）
        var entered = new CountdownEvent(2);

        async Task Work(DownloadProgressHandler _, CancellationToken ct)
        {
            entered.Signal();
            entered.Wait(ct); // 等两个都放行进入（门=2 全并发）
            await Task.Delay(50, ct);
        }

        var t1 = manager.Enqueue("a", Work);
        var t2 = manager.Enqueue("b", Work);
        await Task.WhenAll(t1.Completion, t2.Completion);

        Assert.True(entered.IsSet); // 两个任务都进来了 = 门 2 并发放行
    }

    [Fact]
    public async Task Gate0_Unlimited()
    {
        var manager = Create(0); // 0 = 不限（旧行为）
        var t1 = manager.Enqueue("a", async (_, ct) => await Task.Delay(50, ct));
        await t1.Completion;
        Assert.Equal(DownloadTaskState.Completed, t1.TerminalState);
    }
}
