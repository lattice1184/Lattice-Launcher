namespace Launcher.Core;

/// <summary>
/// 全局统一状态（8-22 工程化步骤 1）：所有模块从同一处读取「当前实例根目录」和「当前选中版本」，
/// 不再各自从 VM/设置/实例对象取（数据不一致的根源）。
/// - InstanceRoot：启动器安装根（.minecraft），初始化写入 GameDirectory.InstallDir()
/// - CurrentVersionId：当前选中版本（主页版本下拉是全局权威，HomeViewModel 同步写入）
/// Core 层模块（修复/日志/校验）直接读这里，不依赖 App 层 VM。
/// </summary>
public static class AppState
{
    private static readonly object Gate = new();

    /// <summary>当前实例根目录（本启动器安装根）。初始化后不变；切换实例目录走设置变更</summary>
    public static string InstanceRoot { get; private set; } = "";

    /// <summary>当前选中版本 ID（如 fabric-loader-0.19.3-26.1.2；未选 = 空）</summary>
    public static string CurrentVersionId { get; private set; } = "";

    /// <summary>启动器初始化时写入实例根（App 启动处调用一次）</summary>
    public static void InitInstanceRoot(string root)
    {
        lock (Gate) { if (string.IsNullOrEmpty(InstanceRoot)) InstanceRoot = root; }
    }

    /// <summary>主页版本切换时同步（全局权威 = 主页版本下拉）</summary>
    public static void SetCurrentVersion(string? versionId)
    {
        lock (Gate) { CurrentVersionId = versionId ?? ""; }
    }
}
