using System.Threading;
using Launcher.Core.Diagnostics;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>
/// 把 Post 入队不立即执行的同步上下文——复现 AL5 竞态：
/// 子任务失败时 SetState(Failed) 是 Post 异步生效，而 Completion 同步完成，
/// 父任务 WhenAll 返回时子任务 State 仍是 Downloading → 误判父任务 Completed（下载历史误报"完成"）。
/// </summary>
internal sealed class DeferredSyncContext : SynchronizationContext
{
    private readonly object _lock = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_lock) _queue.Enqueue((d, state));
    }

    /// <summary>手动执行全部排队回调（模拟 UI 线程泵）</summary>
    public void Drain()
    {
        while (true)
        {
            SendOrPostCallback cb;
            object? st;
            lock (_lock)
            {
                if (_queue.Count == 0) return;
                (cb, st) = _queue.Dequeue();
            }
            cb(st);
        }
    }
}

/// <summary>组任务状态推导：子任务失败必须推导为父任务失败（修复前 UI Post 延迟导致误判 Completed）</summary>
public class DownloadTaskGroupStateTests
{
    [Fact]
    public async Task GroupChildFails_ParentStateFailed_WhenUiPostDeferred()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var task = mgr.EnqueueGroup("组", (g, _) =>
        {
            g.AddChild("坏", 1, (_, _) => throw new InvalidOperationException("下载失败"));
            return Task.CompletedTask;
        });

        // 子任务 Completion 同步完成时其 State 的 Post 尚未执行（仍 Downloading）
        await task.Completion;
        ctx.Drain(); // 父任务推导已在 WhenAll 后执行并 Post——修复前最终 State=Completed（误报！）

        Assert.Equal(DownloadTaskState.Failed, task.State);
    }

    [Fact]
    public async Task GroupAllChildrenOk_ParentCompleted()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var task = mgr.EnqueueGroup("组", (g, _) =>
        {
            g.AddChild("好", 1, (_, _) => Task.CompletedTask);
            return Task.CompletedTask;
        });

        await task.Completion;
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Completed, task.State);
    }

    [Fact]
    public async Task LeafFails_StateFailed()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var task = mgr.Enqueue("叶子", (_, _) => throw new InvalidOperationException("坏"));

        await task.Completion;
        ctx.Drain();

        Assert.Equal(DownloadTaskState.Failed, task.State);
    }

    // ---------- AL44 自动重试一次 ----------

    private static async Task DrainUntil(DeferredSyncContext ctx, Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            await Task.Delay(10);
            ctx.Drain();
        }
    }

    [Fact]
    public async Task LeafNetworkFailure_AutoRetriesOnce_ThenSucceeds()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var calls = 0;
        var task = mgr.Enqueue("重试成功", (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new HttpRequestException("网络超时");
            return Task.CompletedTask;
        });

        await task.Completion;
        ctx.Drain();
        await DrainUntil(ctx, () => task.State == DownloadTaskState.Completed);

        Assert.Equal(2, calls); // 首败 + 自动重试一次
        Assert.Equal(DownloadTaskState.Completed, task.State);
    }

    [Fact]
    public async Task LeafNetworkFailure_TwiceFails_ExactlyOneRetry()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var calls = 0;
        var task = mgr.Enqueue("重试仍败", (_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new HttpRequestException("网络超时");
        });

        await task.Completion;
        ctx.Drain();
        await DrainUntil(ctx, () => task.State == DownloadTaskState.Failed && calls >= 2);
        ctx.Drain();

        Assert.Equal(2, calls); // 恰一次自动重试，不无限重试
        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.NotNull(task.Diagnosis);
        Assert.Equal(FixKind.CheckNetwork, task.Diagnosis!.Fix); // 重试仍败 → 网络建议
    }

    [Fact]
    public async Task LeafFailure_Canceled_NoAutoRetry()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var calls = 0;
        var task = mgr.Enqueue("取消", (_, c) =>
        {
            Interlocked.Increment(ref calls);
            c.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        task.Cancel();

        await task.Completion;
        ctx.Drain();
        await Task.Delay(50);
        ctx.Drain();

        Assert.Equal(1, calls); // 取消不触发自动重试
    }

    [Fact]
    public async Task LeafUnknownException_NoAutoRetry_NoDiagnosis()
    {
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx);
        var calls = 0;
        var task = mgr.Enqueue("未知", (_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("未知错误");
        });

        await task.Completion;
        ctx.Drain();
        await Task.Delay(50);
        ctx.Drain();

        Assert.Equal(1, calls);
        Assert.Null(task.Diagnosis); // 未知异常不诊断不重试
    }
}
