namespace Launcher.Core.Download;

/// <summary>
/// 本启动器安装标记：下载/加载器安装成功后写 versions/{id}/.yanla-installed，
/// 扫描时据此区分"本启动器安装"与"从 PCL2/官方目录扫描到"的版本。
/// </summary>
public static class InstallMarker
{
    public const string MarkerName = ".yanla-installed";

    /// <summary>预取标记：GetOrFetchVersionJsonAsync 拉取（仅供加载器继承）时打；正式安装完成时移除。
    /// 版本页据此隐藏「下载带加载器版本时多出来的原版条目」（真机 08-09 用户反馈混乱）。</summary>
    public const string PrefetchName = ".prefetched";

    public static string Path(string gameDirectory, string id)
        => System.IO.Path.Combine(gameDirectory, "versions", id, MarkerName);

    public static string PrefetchPath(string gameDirectory, string id)
        => System.IO.Path.Combine(gameDirectory, "versions", id, PrefetchName);

    public static bool IsMarked(string gameDirectory, string id)
        => File.Exists(Path(gameDirectory, id));

    public static bool IsPrefetched(string gameDirectory, string id)
        => File.Exists(PrefetchPath(gameDirectory, id));

    /// <summary>版本页显示判定（三扫描路径统一口径）：预取且未正式安装才隐藏。
    /// 兜底历史/未来误打的双标记残留（.prefetched + .yanla-installed）——已装版本必须显示</summary>
    public static bool ShouldShowInPage(string gameDirectory, string id)
        => !IsPrefetched(gameDirectory, id) || IsMarked(gameDirectory, id);

    public static void Mark(string gameDirectory, string id)
    {
        try
        {
            var dir = System.IO.Path.Combine(gameDirectory, "versions", id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path(gameDirectory, id), "");
            UnmarkPrefetched(gameDirectory, id); // 正式安装完成 = 不再是预取
        }
        catch { /* 标记失败不影响安装本身 */ }
    }

    public static void Unmark(string gameDirectory, string id)
    {
        try { File.Delete(Path(gameDirectory, id)); } catch { }
    }

    public static void MarkPrefetched(string gameDirectory, string id)
    {
        try
        {
            var dir = System.IO.Path.Combine(gameDirectory, "versions", id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(PrefetchPath(gameDirectory, id), "");
        }
        catch { /* 标记失败不影响预取 */ }
    }

    public static void UnmarkPrefetched(string gameDirectory, string id)
    {
        try { File.Delete(PrefetchPath(gameDirectory, id)); } catch { }
    }
}
