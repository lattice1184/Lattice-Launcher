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

    /// <summary>应用内通常使用 Instance；公开构造函数供测试创建独立实例（测试环境无 UI 上下文 → 同步直跑）</summary>
    public DownloadManager() => _ui = SynchronizationContext.Current;

    public DownloadTask Enqueue(string name, Func<DownloadProgressHandler, CancellationToken, Task> work)
    {
        var task = new DownloadTask(name, work, _ui);
        Tasks.Add(task);
        SetActive(Tasks.Count(t => t.IsActive));
        task.Completion.ContinueWith(_ => UiPost(() => SetActive(Tasks.Count(t => t.IsActive))),
            TaskScheduler.Default);
        return task;
    }

    public void Cancel(DownloadTask task) => task.Cancel();

    /// <summary>清除已结束（完成/失败/取消）任务；需在 UI 线程调用</summary>
    public void ClearFinished()
    {
        for (var i = Tasks.Count - 1; i >= 0; i--)
            if (!Tasks[i].IsActive) Tasks.RemoveAt(i);
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
