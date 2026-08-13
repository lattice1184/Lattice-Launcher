namespace Launcher.Core.Download;

/// <summary>
/// 组任务上下文：编排器（VersionDownloadPipeline / LoaderService 等）用它创建子任务。
/// AddChild 立即启动子任务（挂到父 Children 并订阅聚合）；weight=0 表示进度不确定（UI 显示不定条）。
/// FirstFailure：任一子任务终态失败时完成——组任务用它「首败早退」，不等全部子任务跑完
/// （BUGS#4/#5 复核仍存在：旧 WhenAll 等 2000 个 assets 全下完才报失败，卡「正在完成」）。
/// </summary>
public sealed class DownloadGroupContext
{
    private readonly DownloadTask _parent;
    private readonly SynchronizationContext? _ui;
    private readonly TaskCompletionSource _firstFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal List<DownloadTask> Children { get; } = [];

    /// <summary>首个子任务失败信号（无失败则永不完成——调用方与 WhenAll 竞速）</summary>
    internal Task FirstFailure => _firstFailure.Task;

    internal DownloadGroupContext(DownloadTask parent, SynchronizationContext? ui)
    {
        _parent = parent;
        _ui = ui;
    }

    /// <summary>创建并启动子任务；weight 为预估字节（0 = 不确定进度）</summary>
    public DownloadTask AddChild(string name, long weight, Func<DownloadProgressHandler, CancellationToken, Task> work)
    {
        var child = new DownloadTask(name, work, _ui) { Weight = weight, IsGroupChild = true };
        Children.Add(child);
        _parent.AttachChild(child);
        // 首败信号：终态失败立即上报（子任务失败即组失败路径，不等其余兄弟——BUGS#4 级联取消失效的根治）
        child.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(child.State) && child.State == DownloadTaskState.Failed)
                _firstFailure.TrySetResult();
        };
        return child;
    }

    /// <summary>更新组任务 Stage（AL62 质检文案——「正在完成…」阶段显示真实状态）</summary>
    public void SetStage(string stage) => _parent.SetStage(stage);
}
