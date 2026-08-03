namespace Launcher.Core.Utils;

/// <summary>游戏目录来源（启动列表标识用）</summary>
public enum GameDirectorySource { OwnDefault, Standard, Pcl, Custom }

/// <summary>
/// 游戏目录（.minecraft）解析，PCL2 式：
/// 安装目标（下载/安装落点）永远是启动器自建目录 Downloads\YanKa Launcher\.minecraft（或用户自配）；
/// PCL / 官方等已有环境的目录只作为"扫描源"（版本可见可启动，但不接收新安装）。
/// </summary>
public static class GameDirectory
{
    /// <summary>来源中文标签（"本启动器"/"PCL2"/"官方"/"自配"）</summary>
    public static string SourceLabel(GameDirectorySource source) => source switch
    {
        GameDirectorySource.OwnDefault => "本启动器",
        GameDirectorySource.Pcl => "PCL2",
        GameDirectorySource.Standard => "官方",
        GameDirectorySource.Custom => "自配",
        _ => "",
    };

    /// <summary>自建目录候选（C 盘 Downloads 历史位 + D 盘位）——扫描源用，换盘后旧版本仍可见</summary>
    private static IEnumerable<string> OwnCandidates()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "YanKa Launcher", ".minecraft");
        if (Directory.Exists("D:\\")) yield return Path.Combine("D:\\", "YanKa Launcher", ".minecraft");
    }

    /// <summary>启动器自建根（优先 D 盘 D:\YanKa Launcher\.minecraft；无 D 盘回退 C 盘 Downloads 历史位）</summary>
    public static string OwnDefault()
    {
        if (Directory.Exists("D:\\")) return Path.Combine("D:\\", "YanKa Launcher", ".minecraft");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "YanKa Launcher", ".minecraft");
    }

    /// <summary>安装目标目录（下载/安装落点）：用户自配 ?? 启动器自建。永不探测已有环境。</summary>
    public static string InstallDir()
    {
        if (LauncherSettings.Current.GameDirectory is { } custom) return custom;
        return OwnDefault();
    }

    /// <summary>安装目标来源（标签："本启动器"/"自配"）</summary>
    public static GameDirectorySource DetectSource()
        => LauncherSettings.Current.GameDirectory is null ? GameDirectorySource.OwnDefault : GameDirectorySource.Custom;

    /// <summary>兼容入口：当前安装目标（历史调用点：下载/安装/默认启动目录）</summary>
    public static string Detect() => InstallDir();

    /// <summary>
    /// 版本发现扫描源：安装目标 + 已有环境（AppData 标准位 / Downloads/PCL*），按序去重。
    /// 已安装版本的显示与启动来自这些目录；新下载安装只进 InstallDir。
    /// </summary>
    public static List<(string Dir, GameDirectorySource Source)> ScanSourceDirs()
    {
        var list = new List<(string Dir, GameDirectorySource Source)>();
        void Add(string dir, GameDirectorySource source)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (list.Any(x => string.Equals(x.Dir, dir, StringComparison.OrdinalIgnoreCase))) return;
            if (Directory.Exists(Path.Combine(dir, "versions"))) list.Add((dir, source));
        }

        Add(InstallDir(), DetectSource());

        // 自建目录历史位置（跨盘扫描：C 盘旧位 / D 盘新位，换盘后旧版本仍可见）
        foreach (var candidate in OwnCandidates())
            Add(candidate, GameDirectorySource.OwnDefault);

        var standard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        Add(standard, GameDirectorySource.Standard);

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            foreach (var dir in Directory.EnumerateDirectories(downloads, "PCL*"))
                Add(Path.Combine(dir, ".minecraft"), GameDirectorySource.Pcl);
        }
        return list;
    }

    /// <summary>由目录反查来源（标签用）</summary>
    public static GameDirectorySource SourceOf(string dir)
        => ScanSourceDirs().FirstOrDefault(x => string.Equals(x.Dir, dir, StringComparison.OrdinalIgnoreCase)).Source;

    /// <summary>确保自建目录结构存在（启动时调用一次；空目录也算已创建）</summary>
    public static void EnsureDefault()
    {
        var dir = OwnDefault();
        foreach (var sub in new[] { "versions", "libraries", "assets", "assets/indexes", "assets/objects" })
        {
            try { Directory.CreateDirectory(Path.Combine(dir, sub)); } catch { }
        }
    }
}
