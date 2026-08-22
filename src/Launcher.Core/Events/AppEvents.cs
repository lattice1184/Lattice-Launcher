namespace Launcher.Core.Events;

/// <summary>
/// 全局事件总线（8-22 工程化步骤 3）：模块间不直接调用，发消息解耦。
/// 下载/启动/修复模块完成时 Publish，UI（进度/弹窗）与日志订阅响应。
/// 轻量实现：内存委托，无第三方依赖；同步分发（订阅者不应阻塞——写日志/UI Post 都轻）。
/// </summary>
public static class AppEvents
{
    private static readonly Dictionary<Type, List<Delegate>> Handlers = new();
    private static readonly object Gate = new();

    /// <summary>发布事件（订阅者同步收到；订阅者异常捕获隔离，不影响发布者）</summary>
    public static void Publish<T>(T evt)
    {
        Delegate[]? snapshot = null;
        lock (Gate)
        {
            if (Handlers.TryGetValue(typeof(T), out var list)) snapshot = list.ToArray();
        }
        if (snapshot is null) return;
        foreach (var d in snapshot)
        {
            try { ((Action<T>)d)(evt); }
            catch { /* 单订阅者异常不扩散（日志写入失败等） */ }
        }
    }

    /// <summary>订阅事件（返回解除委托；重复订阅幂等——先移除再添加）</summary>
    public static IDisposable Subscribe<T>(Action<T> handler)
    {
        lock (Gate)
        {
            if (!Handlers.TryGetValue(typeof(T), out var list)) { list = new List<Delegate>(); Handlers[typeof(T)] = list; }
            list.Remove(handler);
            list.Add(handler);
        }
        return new Subscription(() => { lock (Gate) { if (Handlers.TryGetValue(typeof(T), out var l)) l.Remove(handler); } });
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsub;
        public Subscription(Action unsub) => _unsub = unsub;
        public void Dispose() => _unsub();
    }
}

// ---------- 事件类型 ----------

/// <summary>下载任务完成（含目标路径——日志/弹窗定位用）</summary>
public sealed record DownloadCompletedEvent(string TaskId, string FileName, string TargetPath, string Status, DateTime CompletedAt);

/// <summary>下载任务失败（含错误信息）</summary>
public sealed record DownloadFailedEvent(string TaskId, string FileName, string Error, DateTime CompletedAt);

/// <summary>启动开始</summary>
public sealed record LaunchStartedEvent(string VersionId, DateTime StartedAt);

/// <summary>启动结束（ExitCode 0=正常；非 0 由启动器终止）</summary>
public sealed record LaunchCompletedEvent(string VersionId, int ExitCode, DateTime CompletedAt);

/// <summary>启动失败</summary>
public sealed record LaunchFailedEvent(string VersionId, string Error, DateTime CompletedAt);

/// <summary>自动修复完成</summary>
public sealed record RepairCompletedEvent(string VersionId, int FileCount, DateTime CompletedAt);

/// <summary>自动修复失败</summary>
public sealed record RepairFailedEvent(string VersionId, string Error, DateTime CompletedAt);
