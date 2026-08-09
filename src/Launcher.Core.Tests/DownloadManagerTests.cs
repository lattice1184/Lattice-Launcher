using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>全局下载中心：状态迁移 / 进度报告 / 取消 / 失败 / 活动计数 / 清理（全部离线，无 SynchronizationContext → 同步直跑）</summary>
public class DownloadManagerTests
{
    /// <summary>xunit 会注入 AsyncTestSyncContext，会把 Post 回调挂起；清空后属性更新同步生效</summary>
    private static DownloadManager CreateManager()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new DownloadManager();
    }

    [Fact]
    public async Task Enqueue_WorkCompletes_TaskCompletedAndPercent100()
    {
        var manager = CreateManager();
        var task = manager.Enqueue("测试下载", async (p, ct) =>
        {
            for (var i = 0; i <= 10; i++)
            {
                p(new DownloadProgress("下载库文件 1/3", "lib.jar", i * 10, 100, i * 10));
                await Task.Yield();
            }
        });

        await task.Completion;

        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Equal("完成", task.StateText);
        Assert.False(task.IsActive);
        Assert.Equal("100 B / 100 B", task.BytesText);
    }

    [Fact]
    public async Task Report_ProgressPropagatesToTask()
    {
        var manager = CreateManager();
        var gate = new TaskCompletionSource();
        var task = manager.Enqueue("进度", async (p, ct) =>
        {
            p(new DownloadProgress("下载库文件 2/5", "lib.jar", 512, 1024, 40.5));
            await gate.Task;
        });

        // 同步封送：报告立即生效
        await Task.Delay(100);
        Assert.Equal("下载库文件 2/5", task.Stage);
        Assert.Equal(512, task.BytesDone);
        Assert.Equal(1024, task.TotalBytes);
        Assert.Equal(40.5, task.ProgressPercent);
        Assert.Equal("512 B / 1 KB", task.BytesText);

        gate.SetResult();
        await task.Completion;
        Assert.Equal(100, task.ProgressPercent);
    }

    [Fact]
    public async Task Cancel_FlipsStateToCanceled()
    {
        var manager = CreateManager();
        var task = manager.Enqueue("可取消", (p, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));

        task.Cancel();
        await task.Completion;

        Assert.Equal(DownloadTaskState.Canceled, task.State);
        Assert.Equal("已取消", task.StateText);
    }

    [Fact]
    public async Task WorkThrows_StateFailedAndErrorSet()
    {
        var manager = CreateManager();
        var task = manager.Enqueue("失败任务", (p, ct) => throw new InvalidDataException("磁盘错误"));

        await task.Completion;
        // AL44：校验失败自动重试一次——等重试终态（重试中 State 短暂 Downloading）
        for (var i = 0; i < 100 && task.State != DownloadTaskState.Failed; i++) await Task.Delay(10);

        Assert.Equal(DownloadTaskState.Failed, task.State);
        Assert.Equal("磁盘错误", task.Error);
        Assert.True(task.HasError);
    }

    [Fact]
    public async Task ActiveCount_TracksEnqueueAndCompletion()
    {
        var manager = CreateManager();
        var gate = new TaskCompletionSource();
        var counts = new List<int>();
        manager.ActiveCountChanged += c => counts.Add(c);

        var task = manager.Enqueue("活动", async (p, ct) => await gate.Task);
        Assert.Equal(1, manager.ActiveCount);
        Assert.Contains(1, counts);

        gate.SetResult();
        await task.Completion;
        // 计数回调走 TaskScheduler.Default 的 ContinueWith，异步执行 → 轮询等待
        for (var i = 0; i < 50 && manager.ActiveCount != 0; i++) await Task.Delay(10);
        Assert.Equal(0, manager.ActiveCount);
        Assert.Contains(0, counts);
    }

    [Fact]
    public async Task TerminalTask_SinksToBottom_ActiveStaysOnTop()
    {
        var manager = CreateManager();
        var gate = new TaskCompletionSource();
        var a = manager.Enqueue("A", (p, ct) => Task.CompletedTask);
        var b = manager.Enqueue("B", async (p, ct) => await gate.Task);

        await a.Completion; // State 置终态先于 Completion 完成 → 分区已同步生效

        Assert.Equal(2, manager.Tasks.Count);
        Assert.Equal("B", manager.Tasks[0].Name); // 活跃任务置顶
        Assert.Equal("A", manager.Tasks[1].Name); // 终态任务沉底
        gate.SetResult();
        await b.Completion;
    }

    [Fact]
    public async Task ClearFinished_RemovesFinishedKeepsActive()
    {
        var manager = CreateManager();
        var gate = new TaskCompletionSource();
        var done = manager.Enqueue("已完成", (p, ct) => Task.CompletedTask);
        await done.Completion;
        var active = manager.Enqueue("进行中", (p, ct) => gate.Task);

        manager.ClearFinished();

        Assert.Single(manager.Tasks);
        Assert.Equal("进行中", manager.Tasks[0].Name);
        gate.SetResult();
        await active.Completion;
    }

    [Fact]
    public async Task Suspend_ThenResume_ReplaysWork()
    {
        var manager = new DownloadManager(null);
        var runs = 0;
        var task = manager.Enqueue("t", async (p, ct) =>
        {
            runs++;
            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { throw; }
        });

        task.Suspend();
        // Paused 不是终态——Completion 不完成（稳定 TCS），轮询状态
        for (var i = 0; i < 200 && task.State != DownloadTaskState.Paused; i++)
            await Task.Delay(10);
        Assert.Equal(DownloadTaskState.Paused, task.State);
        Assert.Equal(1, runs);

        task.Resume();
        // Resume 重跑 → 终态完成（同一 Completion 对象，await 有效）
        await task.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task SuspendAll_ThenResumeAll_AllTasksContinue()
    {
        var manager = new DownloadManager(null);
        var t1 = manager.Enqueue("a", async (p, ct) => await Task.Delay(100, ct));
        var t2 = manager.Enqueue("b", async (p, ct) => await Task.Delay(100, ct));

        manager.SuspendAll();
        for (var i = 0; i < 200 && (!t1.Completion.IsCompleted || !t2.Completion.IsCompleted); i++)
            await Task.Delay(10);
        Assert.True(manager.HasPaused);

        manager.ResumeAll();
        await t1.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        await t2.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DownloadTaskState.Completed, t1.State);
        Assert.Equal(DownloadTaskState.Completed, t2.State);
        Assert.False(manager.HasPaused);
    }
}
