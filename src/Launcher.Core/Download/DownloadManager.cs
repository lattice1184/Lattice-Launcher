using System.Collections.ObjectModel;

namespace Launcher.Core.Download;

/// <summary>
/// 进程级下载中心：所有下载（游戏版本/加载器/模组）统一入队，下载页绑定 Tasks。
/// 集合增删与事件回调封送 UI 线程（在 UI 线程首次构造并捕获 SynchronizationContext；
/// 测试环境无上下文 → 同步直跑）。任务结束后任务内部捕获全部异常，Completion 永不抛。
/// </summary>
public sealed class DownloadManager
{
    public static DownloadManager Instance { get; } = new();

    private readonly SynchronizationContext? _ui;
    private int _activeCount;

    public ObservableCollection<DownloadTask> Tasks { get; } = [];

    public int ActiveCount => _activeCount;

    /// <summary>活动任务数变化（0→1 / n→n-1），用于导航角标</summary>
    public event Action<int>? ActiveCountChanged;

    /// <summary>应用内通常使用 Instance；公开构造函数供测试创建独立实例。</summary>
    public DownloadManager() : this(SynchronizationContext.Current) { }

    /// <summary>显式同步上下文（测试传 null = Post 同步直跑，不依赖当前线程上下文）</summary>
    public DownloadManager(SynchronizationContext? syncContext) => _ui = syncContext;

    public DownloadTask Enqueue(string name, Func<DownloadProgressHandler, CancellationToken, Task> work)
    {
        var task = new DownloadTask(name, work, _ui);
        AddAndTrack(task);
        return task;
    }

    /// <summary>组任务：子任务不进 Tasks、不计 ActiveCount（组算 1）</summary>
    public DownloadTask EnqueueGroup(string name, Func<DownloadGroupContext, CancellationToken, Task> groupWork)
    {
        var task = new DownloadTask(name, groupWork, _ui);
        AddAndTrack(task);
        return task;
    }

    private void AddAndTrack(DownloadTask task)
    {
        Tasks.Add(task);
        SetActive(Tasks.Count(t => t.IsActive));
        task.Completion.ContinueWith(_ => UiPost(() => SetActive(Tasks.Count(t => t.IsActive))),
            TaskScheduler.Default);
        // 自动清空：终态（完成/失败/取消）任务 3 秒后自动从队列移除（暂停 Paused 不是终态不移除）
        task.Completion.ContinueWith(_ => ScheduleAutoRemove(task), TaskScheduler.Default);
        // 防未观察异常：完成回调本身出错时记录（不崩溃进程）
        task.Completion.ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"[DownloadManager] 计数回调异常: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public void Cancel(DownloadTask task) => task.Cancel();

    /// <summary>是否有已暂停任务（继续按钮显示条件）</summary>
    public bool HasPaused => Tasks.Any(t => t.State == DownloadTaskState.Paused);

    /// <summary>暂停全部活跃任务（文件断点保留）</summary>
    public void SuspendAll()
    {
        foreach (var t in Tasks.Where(t => t.IsActive)) t.Suspend();
        NotifyPausedChanged();
    }

    /// <summary>继续全部已暂停任务（断点续传）</summary>
    public void ResumeAll()
    {
        foreach (var t in Tasks.Where(t => t.State == DownloadTaskState.Paused)) t.Resume();
        NotifyPausedChanged();
    }

    private void NotifyPausedChanged()
    {
        UiPost(() => PausedChanged?.Invoke(HasPaused));
    }

    /// <summary>暂停状态变化（UI 继续按钮显隐）</summary>
    public event Action<bool>? PausedChanged;

    /// <summary>终态任务延迟自动移除（3 秒——让用户看到完成状态与 Toast）</summary>
    private async void ScheduleAutoRemove(DownloadTask task)
    {
        try
        {
            await Task.Delay(3000);
            if (task.State is DownloadTaskState.Completed or DownloadTaskState.Failed or DownloadTaskState.Canceled)
                UiPost(() => { Tasks.Remove(task); SetActive(Tasks.Count(t => t.IsActive)); });
        }
        catch { /* 进程退出等 */ }
    }

    /// <summary>清除已结束（完成/失败/取消）任务；需在 UI 线程调用。已暂停任务保留（可继续）。</summary>
    public void ClearFinished()
    {
        for (var i = Tasks.Count - 1; i >= 0; i--)
        {
            var t = Tasks[i];
            if (!t.IsActive && t.State != DownloadTaskState.Paused) Tasks.RemoveAt(i);
        }
        SetActive(Tasks.Count(t => t.IsActive));
    }

    private void SetActive(int count)
    {
        if (_activeCount == count) return;
        _activeCount = count;
        ActiveCountChanged?.Invoke(count);
    }

    private void UiPost(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(_ => action(), null);
    }
}
