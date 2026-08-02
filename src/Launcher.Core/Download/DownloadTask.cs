using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.Core.Download;

public enum DownloadTaskState { Queued, Downloading, Verifying, Completed, Failed, Canceled }

/// <summary>
/// 下载任务（全局下载中心 UI 数据源）：状态机 + 阶段 + 计速 + ETA + 取消。
/// 属性更新通过 Enqueue 时捕获的 SynchronizationContext 封送（测试环境为 null → 同步直跑）。
/// 状态写安全：Report 可来自任意线程（分片并行），内部加锁后统一封送。
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
    };

    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext? _ui;
    private readonly Stopwatch _watch = new();
    private readonly object _lock = new();
    private long _sampleStartBytes;
    private long _lastBytes = -1;
    private double _sampleStartTime;

    public string Name { get; }
    public Task Completion { get; }
    public bool IsActive => State is DownloadTaskState.Queued or DownloadTaskState.Downloading or DownloadTaskState.Verifying;

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

    public IRelayCommand CancelCommand { get; }

    internal DownloadTask(string name, Func<DownloadProgressHandler, CancellationToken, Task> work, SynchronizationContext? ui)
    {
        Name = name;
        _ui = ui;
        CancelCommand = new RelayCommand(Cancel);
        Completion = RunAsync(work);
    }

    private async Task RunAsync(Func<DownloadProgressHandler, CancellationToken, Task> work)
    {
        try
        {
            await Task.Run(async () =>
            {
                _watch.Start();
                SetState(DownloadTaskState.Downloading);
                await work(Report, _cts.Token);
                SetState(_cts.IsCancellationRequested ? DownloadTaskState.Canceled : DownloadTaskState.Completed);
                if (!_cts.IsCancellationRequested) Post(() => ProgressPercent = 100);
            });
        }
        catch (OperationCanceledException)
        {
            SetState(DownloadTaskState.Canceled);
        }
        catch (Exception ex)
        {
            SetState(DownloadTaskState.Failed);
            Post(() => Error = ex.Message);
        }
        finally
        {
            _watch.Stop();
        }
    }

    public void Cancel() => _cts.Cancel();

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
        });
    }

    private void SetState(DownloadTaskState state)
    {
        Post(() =>
        {
            State = state;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsActive));
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
