using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.Core.Download;

public enum DownloadTaskState { Queued, Downloading, Verifying, Completed, Failed, Canceled, Paused }

/// <summary>
/// 下载任务（全局下载中心 UI 数据源）：叶子任务（下载一个文件）或组任务（版本下载，Children 为各文件）。
/// 组任务聚合子进度（按 Weight 加权），状态推导：有失败→失败、取消→取消、否则完成。
/// 属性更新通过 Enqueue 时捕获的 SynchronizationContext 封送（测试环境为 null → 同步直跑）。
/// </summary>
public partial class DownloadTask : ObservableObject
{
    private static readonly Dictionary<DownloadTaskState, string> StateTexts = new()
    {
        [DownloadTaskState.Queued] = "排队",
        [DownloadTaskState.Downloading] = "下载中",
        [DownloadTaskState.Verifying] = "校验中",
        [DownloadTaskState.Completed] = "完成",
        [DownloadTaskState.Failed] = "失败",
        [DownloadTaskState.Canceled] = "已取消",
        [DownloadTaskState.Paused] = "已暂停",
    };

    private CancellationTokenSource _cts = new();
    private readonly SynchronizationContext? _ui;
    private readonly Stopwatch _watch = new();
    private readonly object _lock = new();
    private readonly List<CancellationTokenRegistration> _externalCancellations = [];
    private long _sampleStartBytes;
    private long _lastBytes = -1;
    private double _sampleStartTime;

    // 暂停/继续：保留 work 委托与用户暂停标记；恢复时重放（文件断点续传）
    private Func<DownloadProgressHandler, CancellationToken, Task>? _work;
    private Func<DownloadGroupContext, CancellationToken, Task>? _groupWork;
    private volatile bool _suspendRequested;

    public string Name { get; }

    /// <summary>
    /// 完成信号（内部 TCS，**终态**（完成/失败/取消）才完成；暂停不完成——Resume 后继续等待）。
    /// 对象稳定：Resume 重跑不替换（下游 await/自动移除/角标订阅始终有效）。
    /// </summary>
    private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completionTcs.Task;

    public bool IsActive => State is DownloadTaskState.Queued or DownloadTaskState.Downloading or DownloadTaskState.Verifying;

    /// <summary>组任务：子任务集合（叶子为空）。Children 不进 DownloadManager.Tasks，不参与 ActiveCount</summary>
    public DownloadTask? Parent { get; }
    public ObservableCollection<DownloadTask> Children { get; } = [];
    public bool IsGroup { get; }
    public bool HasChildren => Children.Count > 0;

    /// <summary>聚合权重（预估字节；0 = 进度不确定）</summary>
    public long Weight { get; internal set; }

    [ObservableProperty]
    public partial DownloadTaskState State { get; set; } = DownloadTaskState.Queued;

    [ObservableProperty]
    public partial string Stage { get; set; } = "排队等待…";

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial long BytesDone { get; set; }

    [ObservableProperty]
    public partial long TotalBytes { get; set; }

    [ObservableProperty]
    public partial double SpeedBps { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string StateText => StateTexts[State];
    public bool HasError => Error is not null;
    public bool IsPaused => State == DownloadTaskState.Paused;
    public bool IsFailed => State == DownloadTaskState.Failed;
    public bool HasProgress => ProgressPercent > 0;

    public string SpeedText => SpeedBps >= 1024 * 1024
        ? $"{SpeedBps / 1024 / 1024:0.0} MB/s"
        : SpeedBps >= 1024 ? $"{SpeedBps / 1024:0} KB/s" : "";

    public string EtaText
    {
        get
        {
            if (State != DownloadTaskState.Downloading || SpeedBps <= 0 || TotalBytes <= BytesDone) return "";
            var ts = TimeSpan.FromSeconds((TotalBytes - BytesDone) / SpeedBps);
            return $"剩余 {ts.Minutes}:{ts.Seconds:00}";
        }
    }

    public string BytesText => $"{FormatBytes(BytesDone)} / {FormatBytes(TotalBytes)}";

    /// <summary>子任务迷你行文本（叶子任务的百分比文字）</summary>
    public string ChildProgressText => HasProgress ? $"{ProgressPercent:0}%" : "…";

    public IRelayCommand CancelCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand ResumeCommand { get; }
    public IRelayCommand RetryCommand { get; }

    // ---------- 构造 ----------

    /// <summary>叶子任务（下载一个文件）</summary>
    internal DownloadTask(string name, Func<DownloadProgressHandler, CancellationToken, Task> work, SynchronizationContext? ui)
        : this(name, ui)
    {
        _work = work;
        _ = RunAsync(work);
    }

    /// <summary>组任务（下载一个版本；children 由 DownloadGroupContext 创建）</summary>
    internal DownloadTask(string name, Func<DownloadGroupContext, CancellationToken, Task> groupWork, SynchronizationContext? ui)
        : this(name, ui)
    {
        IsGroup = true;
        _groupWork = groupWork;
        _ = RunGroupAsync(groupWork);
    }

    private DownloadTask(string name, SynchronizationContext? ui)
    {
        Name = name;
        _ui = ui;
        CancelCommand = new RelayCommand(Cancel);
        PauseCommand = new RelayCommand(Suspend);
        ResumeCommand = new RelayCommand(Resume);
        RetryCommand = new RelayCommand(Retry);
    }

    // ---------- 叶子生命周期 ----------

    private async Task RunAsync(Func<DownloadProgressHandler, CancellationToken, Task> work)
    {
        try
        {
            await Task.Run(async () =>
            {
                _watch.Start();
                SetState(DownloadTaskState.Downloading);
                await work(Report, _cts.Token);
                SetState(_cts.IsCancellationRequested
                    ? (_suspendRequested ? DownloadTaskState.Paused : DownloadTaskState.Canceled)
                    : DownloadTaskState.Completed);
                if (!_cts.IsCancellationRequested) Post(() => ProgressPercent = 100);
            });
        }
        catch (OperationCanceledException)
        {
            SetState(_suspendRequested ? DownloadTaskState.Paused : DownloadTaskState.Canceled);
        }
        catch (Exception ex)
        {
            SetState(DownloadTaskState.Failed);
            Post(() => Error = ex.Message);
        }
        finally
        {
            _watch.Stop();
            // 终态（含 Paused？否——暂停只是挂起，Resume 后继续；这里 Paused 由 Suspend 的 Cancel 触发，
            // 需区分：用户暂停 → 不完成；取消 → 完成）。用 _suspendRequested 判定。
            if (!_suspendRequested) _completionTcs.TrySetResult();
        }
    }

    // ---------- 组生命周期 ----------

    private async Task RunGroupAsync(Func<DownloadGroupContext, CancellationToken, Task> groupWork)
    {
        try
        {
            await Task.Run(async () =>
            {
                _watch.Start();
                SetState(DownloadTaskState.Downloading);
                var ctx = new DownloadGroupContext(this, _ui);
                await groupWork(ctx, _cts.Token);                 // 编排：创建并等待全部子任务
                await Task.WhenAll(ctx.Children.Select(c => c.Completion));

                var failed = ctx.Children.FirstOrDefault(c => c.State == DownloadTaskState.Failed);
                // 任一子任务失败 → 级联取消其余兄弟（停止无效下载/写盘，如版本下载中一个库 404 不再白白下 assets）
                if (failed is not null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                    foreach (var c in ctx.Children) c.Cancel();
                }
                // 失败优先（内部级联取消后父任务仍是 Failed）；用户主动取消时子任务全 Canceled → 走 Canceled 分支
                if (failed is not null)
                {
                    SetState(DownloadTaskState.Failed);
                    Post(() => Error = failed.Error ?? "子任务失败");
                }
                else if (_cts.IsCancellationRequested)
                {
                    SetState(_suspendRequested ? DownloadTaskState.Paused : DownloadTaskState.Canceled);
                }
                else
                {
                    SetState(DownloadTaskState.Completed);
                    Post(() => ProgressPercent = 100);
                }
            });
        }
        catch (OperationCanceledException)
        {
            SetState(_suspendRequested ? DownloadTaskState.Paused : DownloadTaskState.Canceled);
        }
        catch (Exception ex)
        {
            SetState(DownloadTaskState.Failed);
            Post(() => Error = ex.Message);
        }
        finally
        {
            _watch.Stop();
            if (!_suspendRequested) _completionTcs.TrySetResult();
        }
    }

    // ---------- 暂停 / 继续 ----------

    /// <summary>暂停：取消当前执行（文件断点保留），状态置"已暂停"。
    /// 用 _suspendRequested（volatile）判断，不依赖 State 的即时性（UI 线程 Post 异步时状态可能滞后）。</summary>
    public void Suspend()
    {
        if (_suspendRequested) return;
        _suspendRequested = true;
        foreach (var child in Children) child.Suspend();
        _cts.Cancel();
    }

    /// <summary>失败重试：清错误重跑 work（断点续传已下载部分）</summary>
    public void Retry()
    {
        if (State != DownloadTaskState.Failed) return;
        _suspendRequested = false;
        _cts = new CancellationTokenSource();
        Post(() => Error = null);
        if (IsGroup) Post(() => Children.Clear());
        if (IsGroup && _groupWork is not null)
            _ = RunGroupAsync(_groupWork);
        else if (_work is not null)
            _ = RunAsync(_work);
    }

    /// <summary>继续：重放 work（断点续传已下载部分）</summary>
    public void Resume()
    {
        if (!_suspendRequested) return;
        _suspendRequested = false;
        _cts = new CancellationTokenSource();
        if (IsGroup) Post(() => Children.Clear()); // 清掉暂停的旧子任务，重跑会新建
        if (IsGroup && _groupWork is not null)
            _ = RunGroupAsync(_groupWork);
        else if (_work is not null)
            _ = RunAsync(_work);
    }

    /// <summary>
    /// 挂载子任务并订阅聚合（由 DownloadGroupContext 在线程池调用）。
    /// 整个挂载过程封送 UI 线程：Children（ObservableCollection）的全部读写
    /// （Add / OnChildPropertyChanged / RecomputeAggregate / Cancel 遍历）收敛到同一线程，
    /// 消除"线程池 Add 与 UI 线程枚举"的 Collection was modified 竞态（曾导致闪退）。
    /// </summary>
    internal void AttachChild(DownloadTask child)
    {
        Post(() =>
        {
            lock (_lock)
            {
                Children.Add(child);
                child.PropertyChanged += OnChildPropertyChanged;
                // 父取消级联：覆盖"父先取消、子后创建"的时序（Children 级联只覆盖已存在的子任务）
                child._externalCancellations.Add(_cts.Token.Register(child.Cancel));
                RecomputeAggregate();
            }
        });
    }

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProgressPercent) or nameof(TotalBytes)
            or nameof(State) or nameof(Stage) or nameof(Error))
        {
            RecomputeAggregate();
        }
    }

    /// <summary>加权聚合：TotalBytes=ΣWeight；percent=Σ(Weight×child%)/Σ；Stage=最后活动子任务；聚合计速。
    /// 与 AttachChild 共用锁（Monitor 可重入）保证 Children 迭代/修改互斥，防御偶发 NRE/竞态。</summary>
    private void RecomputeAggregate()
    {
        if (!IsGroup) return;

        lock (_lock)
        {
            long total = 0;
            double weighted = 0;
            DownloadTask? active = null;
            foreach (var c in Children)
            {
                if (c is null) continue; // 防御
                var w = Math.Max(c.Weight, 0);
                total += w;
                weighted += w * c.ProgressPercent;
                if (c.IsActive) active = c;
            }
            active ??= Children.LastOrDefault(c => c is not null && c.State == DownloadTaskState.Downloading);

        var percent = total > 0 ? weighted / total : 0;
        Stage = active?.Stage ?? (total > 0 ? "正在下载…" : "准备中…");
        TotalBytes = total;
        if (percent > ProgressPercent) ProgressPercent = percent;
        BytesDone = (long)(total * percent / 100);

        // 聚合计速（聚合字节单调，无需重置基线）
        var now = _watch.Elapsed.TotalSeconds;
        if (_lastBytes < 0)
        {
            _sampleStartBytes = BytesDone;
            _sampleStartTime = now;
            _lastBytes = BytesDone;
        }
        else if (BytesDone > _lastBytes)
        {
            var dt = now - _sampleStartTime;
            if (dt > 0) SpeedBps = (BytesDone - _sampleStartBytes) / dt;
            _lastBytes = BytesDone;
        }
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(EtaText));
            OnPropertyChanged(nameof(BytesText));
            }
    }

    // ---------- 控制 ----------

    public void Cancel()
    {
        _cts.Cancel();
        foreach (var child in Children) child.Cancel();
    }

    /// <summary>报告进度（可来自任意线程）：计速按当前文件字节采样（新文件起始重置基线）</summary>
    private void Report(DownloadProgress p)
    {
        string stage, speedText, etaText, bytesText;
        double speed, overall;
        long done, total;
        lock (_lock)
        {
            var now = _watch.Elapsed.TotalSeconds;
            if (_lastBytes < 0 || p.FileBytesDone < _lastBytes)
            {
                _sampleStartBytes = p.FileBytesDone;
                _sampleStartTime = now;
            }
            var dt = now - _sampleStartTime;
            speed = dt > 0 && p.FileBytesDone > _sampleStartBytes
                ? (p.FileBytesDone - _sampleStartBytes) / dt
                : SpeedBps;
            _lastBytes = p.FileBytesDone;

            stage = string.IsNullOrEmpty(p.Stage) ? "下载中…" : p.Stage;
            done = p.FileBytesDone;
            total = p.FileTotalBytes;
            overall = Math.Clamp(p.OverallPercent, 0, 100);

            speedText = speed >= 1024 * 1024 ? $"{speed / 1024 / 1024:0.0} MB/s"
                : speed >= 1024 ? $"{speed / 1024:0} KB/s" : "";
            etaText = speed > 0 && total > done
                ? $"剩余 {TimeSpan.FromSeconds((total - done) / speed):m\\:ss}"
                : "";
            bytesText = $"{FormatBytes(done)} / {FormatBytes(total)}";
        }
        Post(() =>
        {
            Stage = stage;
            BytesDone = done;
            TotalBytes = total;
            if (overall > ProgressPercent) { ProgressPercent = overall; OnPropertyChanged(nameof(HasProgress)); }
            SpeedBps = speed;
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(EtaText));
            OnPropertyChanged(nameof(BytesText));
            OnPropertyChanged(nameof(ChildProgressText));
        });
    }

    private void SetState(DownloadTaskState state)
    {
        Post(() =>
        {
            State = state;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsFailed));
        });
    }

    private void Post(Action action)
    {
        if (_ui is null) action();
        else _ui.Post(_ => action(), null);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }
}
