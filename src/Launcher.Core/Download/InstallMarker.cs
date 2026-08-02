namespace Launcher.Core.Download;

/// <summary>
/// 本启动器安装标记：下载/加载器安装成功后写 versions/{id}/.yanla-installed，
/// 扫描时据此区分"本启动器安装"与"从 PCL2/官方目录扫描到"的版本。
/// </summary>
public static class InstallMarker
{
    public const string MarkerName = ".yanla-installed";

    public static string Path(string gameDirectory, string id)
        => System.IO.Path.Combine(gameDirectory, "versions", id, MarkerName);

    public static bool IsMarked(string gameDirectory, string id)
        => File.Exists(Path(gameDirectory, id));

    public static void Mark(string gameDirectory, string id)
    {
        try
        {
            var dir = System.IO.Path.Combine(gameDirectory, "versions", id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path(gameDirectory, id), "");
        }
        catch { /* 标记失败不影响安装本身 */ }
    }

    public static void Unmark(string gameDirectory, string id)
    {
        try { File.Delete(Path(gameDirectory, id)); } catch { }
    }
}
