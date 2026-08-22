using System.Collections.ObjectModel;
using Launcher.Core.Utils;

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
    private readonly SemaphoreSlim _gate;

    public ObservableCollection<DownloadTask> Tasks { get; } = [];

    public int ActiveCount => _activeCount;

    /// <summary>活动任务数变化（0→1 / n→n-1），用于导航角标</summary>
    public event Action<int>? ActiveCountChanged;

    /// <summary>应用内通常使用 Instance；公开构造函数供测试创建独立实例。</summary>
    public DownloadManager() : this(SynchronizationContext.Current) { }

    /// <summary>显式同步上下文（测试传 null = Post 同步直跑，不依赖当前线程上下文）</summary>
    public DownloadManager(SynchronizationContext? syncContext)
        : this(syncContext, LauncherSettings.Current.MaxConcurrentDownloads) { }

    /// <summary>测试注入并发上限（0 = 不限）</summary>
    public DownloadManager(SynchronizationContext? syncContext, int maxConcurrentDownloads)
    {
        _ui = syncContext;
        // AL65 全局并发门：设置里「最大并发下载数」>0 时同时跑的任务数受限，
        // 超出的排队（Queued 状态显示「排队等待…」）——多任务不再无限并行抢带宽
        _gate = new SemaphoreSlim(maxConcurrentDownloads > 0 ? maxConcurrentDownloads : int.MaxValue);
    }

    /// <summary>8-18 排队序号计数（并发门等待位；拿到门即递减）</summary>
    private int _queued;

    /// <summary>
    /// 入队单个文件任务。sourceUrl/targetPath 供下载历史「重新下载 / 打开位置」使用（第三方下载传入，其余为 null）。
    /// </summary>
    public DownloadTask Enqueue(string name, Func<DownloadProgressHandler, CancellationToken, Task> work,
        string? sourceUrl = null, string? targetPath = null)
    {
        var task = new DownloadTask(name, Gated(work), _ui)
        {
            SourceUrl = sourceUrl,
            TargetPath = targetPath,
        };
        task.QueuePosition = Interlocked.Increment(ref _queued);
        task.Stage = task.QueuePosition > 1 ? $"排队（前面 {task.QueuePosition - 1} 个任务）" : "排队等待…";
        AddAndTrack(task);
        return task;
    }

    /// <summary>组任务：子任务不进 Tasks、不计 ActiveCount（组算 1）。
    /// sourceUrl/targetPath（8-22）供下载历史「重下/位置」用——模组安装等组任务原本不传，
    /// 历史里永远看不到安装落点；传入后历史按钮可用。</summary>
    public DownloadTask EnqueueGroup(string name, Func<DownloadGroupContext, CancellationToken, Task> groupWork,
        string? sourceUrl = null, string? targetPath = null)
    {
        var task = new DownloadTask(name, Gated(groupWork), _ui)
        {
            SourceUrl = sourceUrl,
            TargetPath = targetPath,
        };
        task.QueuePosition = Interlocked.Increment(ref _queued);
        task.Stage = task.QueuePosition > 1 ? $"排队（前面 {task.QueuePosition - 1} 个任务）" : "排队等待…";
        AddAndTrack(task);
        return task;
    }

    /// <summary>全局并发门包装（AL65）：排队等待 → 启动 → 完成释放；取消时 WaitAsync 抛 OCE</summary>
    private Func<TArg, CancellationToken, Task> Gated<TArg>(Func<TArg, CancellationToken, Task> inner)
        => async (arg, ct) =>
        {
            await _gate.WaitAsync(ct);
            Interlocked.Decrement(ref _queued); // 8-18：拿到门，排队计数递减
            try { await inner(arg, ct); }
            finally { _gate.Release(); }
        };

    private void AddAndTrack(DownloadTask task)
    {
        Tasks.Add(task);
        KeepActiveOnTop(); // AL11：新任务插入后活跃置顶（终态任务沉底）
        SetActive(Tasks.Count(t => t.IsActive));
        task.Completion.ContinueWith(_ => UiPost(() => SetActive(Tasks.Count(t => t.IsActive))),
            TaskScheduler.Default);
        // 自动清空：终态（完成/失败/取消）任务 3 秒后自动从队列移除（暂停 Paused 不是终态不移除）
        task.Completion.ContinueWith(_ => ScheduleAutoRemove(task), TaskScheduler.Default);
        // 防未观察异常：完成回调本身出错时记录（不崩溃进程）
        task.Completion.ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"[DownloadManager] 计数回调异常: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        // AL11：终态沉底——活跃任务置顶（PropertyChanged 由任务经 UI 上下文封送，此处直接 Move 安全）
        task.PropertyChanged += OnTaskStateChanged;
        // 8-22 步骤3：完成/失败事件发布（UI/日志订阅；不直接调用各模块）
        task.Completion.ContinueWith(t =>
        {
            var ts = task.TerminalState;
            if (ts == DownloadTaskState.Completed)
                Launcher.Core.Events.AppEvents.Publish(new Launcher.Core.Events.DownloadCompletedEvent(
                    task.Name, task.Name, task.TargetPath ?? "", "完成", DateTime.Now));
            else if (ts == DownloadTaskState.Failed)
                Launcher.Core.Events.AppEvents.Publish(new Launcher.Core.Events.DownloadFailedEvent(
                    task.Name, task.Name, task.Error ?? "未知错误", DateTime.Now));
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>任务状态变化 → 活跃任务前置（终态沉底）。组任务的子任务不在 Tasks（IndexOf<0 跳过）。</summary>
    private void OnTaskStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloadTask.State)) return;
        if (sender is not DownloadTask t) return;
        if (Tasks.IndexOf(t) < 0) return;
        KeepActiveOnTop();
    }

    /// <summary>稳定分区：活跃（下载中/排队/校验）在前、非活跃（终态/暂停）在后，各自保持相对顺序。
    /// 下载记录里正在下的任务始终置顶；终态任务沉底（3s 后自动移除）。</summary>
    private void KeepActiveOnTop()
    {
        var active = new List<DownloadTask>();
        var rest = new List<DownloadTask>();
        foreach (var t in Tasks) (t.IsActive ? active : rest).Add(t);
        for (var i = 0; i < active.Count; i++)
        {
            var cur = Tasks.IndexOf(active[i]);
            if (cur != i) Tasks.Move(cur, i);
        }
        for (var j = 0; j < rest.Count; j++)
        {
            var cur = Tasks.IndexOf(rest[j]);
            var target = active.Count + j;
            if (cur != target) Tasks.Move(cur, target);
        }
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
                UiPost(() => { task.PropertyChanged -= OnTaskStateChanged; Tasks.Remove(task); SetActive(Tasks.Count(t => t.IsActive)); });
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
