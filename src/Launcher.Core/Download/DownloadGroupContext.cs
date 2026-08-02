namespace Launcher.Core.Download;

/// <summary>
/// 组任务上下文：编排器（VersionDownloadPipeline / LoaderService 等）用它创建子任务。
/// AddChild 立即启动子任务（挂到父 Children 并订阅聚合）；weight=0 表示进度不确定（UI 显示不定条）。
/// </summary>
public sealed class DownloadGroupContext
{
    private readonly DownloadTask _parent;
    private readonly SynchronizationContext? _ui;

    internal List<DownloadTask> Children { get; } = [];

    internal DownloadGroupContext(DownloadTask parent, SynchronizationContext? ui)
    {
        _parent = parent;
        _ui = ui;
    }

    /// <summary>创建并启动子任务；weight 为预估字节（0 = 不确定进度）</summary>
    public DownloadTask AddChild(string name, long weight, Func<DownloadProgressHandler, CancellationToken, Task> work)
    {
        var child = new DownloadTask(name, work, _ui) { Weight = weight };
        Children.Add(child);
        _parent.AttachChild(child);
        return child;
    }
}
