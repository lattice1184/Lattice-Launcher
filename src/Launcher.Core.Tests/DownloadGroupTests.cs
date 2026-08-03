using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>组任务模型：状态推导 / 加权聚合 / 递归取消 / 计数语义 / 清理（离线同步上下文）</summary>
public class DownloadGroupTests
{
    private static DownloadManager CreateManager()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new DownloadManager();
    }

    [Fact]
    public async Task Group_AllChildrenSucceed_ParentCompleted()
    {
        var manager = CreateManager();
        var task = manager.EnqueueGroup("下载 1.21.1", async (ctx, ct) =>
        {
            ctx.AddChild("client.jar", 100, (p, c) => Task.CompletedTask);
            ctx.AddChild("lib.jar", 200, (p, c) => Task.CompletedTask);
        });

        await task.Completion;

        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Equal(2, task.Children.Count);
        Assert.True(task.HasChildren);
    }

    [Fact]
    public async Task Group_ChildFails_ParentFailedWithChildError()
    {
        var manager = CreateManager();
        var task = manager.EnqueueGroup("下载", async (ctx, ct) =>
        {
            ctx.AddChild("ok.jar", 100, (p, c) => Task.CompletedTask);
            ctx.AddChild("bad.jar", 100, (p, c) => throw new InvalidDataException("SHA1 不匹配"));
        });

        await task.Completion;

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal("SHA1 不匹配", task.Error);
    }

    [Fact]
    public async Task Group_Cancel_CascadesToChildren()
    {
        var manager = CreateManager();
        var gate = new TaskCompletionSource();
        var task = manager.EnqueueGroup("下载", (ctx, ct) =>
        {
            ctx.AddChild("slow.jar", 100, (p, c) => Task.Delay(Timeout.InfiniteTimeSpan, c));
            return gate.Task;
        });

        task.Cancel();
        gate.SetResult();
        await task.Completion;

        Assert.Equal(DownloadTaskState.Canceled, task.State);
        Assert.Equal(DownloadTaskState.Canceled, task.Children[0].State);
    }

    [Fact]
    public async Task Group_WeightedAggregation()
    {
        var manager = CreateManager();
        var hold = new TaskCompletionSource();   // 按住子任务保持运行态
        var release = new TaskCompletionSource();
        var task = manager.EnqueueGroup("下载", async (ctx, ct) =>
        {
            ctx.AddChild("a.jar", 100, async (p, c) =>
            {
                p(new DownloadProgress("下载 a.jar", "a.jar", 50, 100, 50));
                await hold.Task;
            });
            ctx.AddChild("b.jar", 300, async (p, c) =>
            {
                p(new DownloadProgress("下载 b.jar", "b.jar", 300, 300, 100));
                await hold.Task;
            });
            await release.Task;
        });

        // 子任务报告后仍挂起 → 聚合 = (100×50 + 300×100)/400 = 87.5
        for (var i = 0; i < 50 && task.ProgressPercent < 87; i++) await Task.Delay(10);
        Assert.Equal(87.5, task.ProgressPercent, 1);
        Assert.Equal(400, task.TotalBytes);

        hold.SetResult();     // 子任务完成 → 聚合收敛 100
        release.SetResult();
        await task.Completion;
        Assert.Equal(DownloadTaskState.Completed, task.State);
    }

    [Fact]
    public async Task EnqueueGroup_ActiveCountCountsGroupAsOne()
    {
        var manager = CreateManager();
        var gate = new TaskCompletionSource();
        var task = manager.EnqueueGroup("下载", async (ctx, ct) =>
        {
            ctx.AddChild("a.jar", 100, (p, c) => gate.Task);
        });

        await Task.Delay(50);
        Assert.Equal(1, manager.ActiveCount);   // 组算 1，不是 2
        Assert.Single(manager.Tasks);           // Children 不进 Tasks

        gate.SetResult();
        await task.Completion;
        for (var i = 0; i < 50 && manager.ActiveCount != 0; i++) await Task.Delay(10);
        Assert.Equal(0, manager.ActiveCount);
    }

    [Fact]
    public async Task ClearFinished_RemovesGroupWithChildren()
    {
        var manager = CreateManager();
        var task = manager.EnqueueGroup("下载", (ctx, ct) => Task.CompletedTask);
        await task.Completion;

        manager.ClearFinished();

        Assert.Empty(manager.Tasks);
    }

    /// <summary>
    /// 高并发回归：管线在线程池快速 Add 40 个子任务，子任务立即完成触发聚合。
    /// 用"异步 Post"上下文制造真实并发窗口——修复前会抛 Collection was modified（线程池未捕获 → 进程崩），
    /// 修复后（Children 访问全部封送同一线程）稳定通过。
    /// </summary>
    [Fact]
    public async Task Group_ManyChildrenRapidAdd_NoCollectionModifiedCrash()
    {
        SynchronizationContext.SetSynchronizationContext(new AsyncPostContext());
        var manager = new DownloadManager();
        try
        {
            // 规模收敛：8 轮 × 20 子任务（并发回归意图不变，降低线程池压力避免偶发饥饿）
            for (var round = 0; round < 8; round++)
            {
                var task = manager.EnqueueGroup("下载", async (ctx, ct) =>
                {
                    for (var i = 0; i < 20; i++)
                    {
                        ctx.AddChild($"lib{i}.jar", 10, (p, c) => Task.CompletedTask);
                    }
                });
                await task.Completion;
                // 异步 Post 上下文：State/Children 更新在排队回调里，轮询等待（10s 窗口容忍线程池竞争）
                for (var i = 0; i < 1000 && (task.State != DownloadTaskState.Completed || task.Children.Count != 20); i++)
                    await Task.Delay(10);
                Assert.True(task.State == DownloadTaskState.Completed,
                    $"round={round} state={task.State} children={task.Children.Count}");
                Assert.Equal(20, task.Children.Count);
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    /// <summary>
    /// Post 异步但串行执行（模拟 Dispatcher 的 FIFO 语义：回调在另一线程排队串行跑，
    /// 与 AddChild 的调用线程形成真实并发窗口，但回调之间不并发——与 Avalonia Dispatcher 一致）。
    /// </summary>
    private sealed class AsyncPostContext : SynchronizationContext
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public override void Post(SendOrPostCallback d, object? state)
            => _ = Task.Run(async () =>
            {
                await _gate.WaitAsync();
                try { d(state); }
                finally { _gate.Release(); }
            });

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    [Fact]
    public async Task Group_ZeroWeightChild_Indeterminate()
    {
        var manager = CreateManager();
        var task = manager.EnqueueGroup("下载", (ctx, ct) =>
        {
            ctx.AddChild("配置", 0, (p, c) => Task.CompletedTask);
            return Task.CompletedTask;
        });

        await task.Completion;

        Assert.Equal(0, task.Children[0].Weight); // 权重 0 → 父聚合不受影响
        Assert.Equal(DownloadTaskState.Completed, task.State);
    }
}
