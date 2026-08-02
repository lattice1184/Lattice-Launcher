namespace Launcher.Core.Utils;

/// <summary>
/// 游戏目录（.minecraft）解析，PCL2 式：优先启动器自建目录 Downloads\YanKa Launcher\.minecraft，
/// 其次探测已有环境（PCL 启动器 / AppData 标准位），最后回退自建目录。
/// </summary>
public static class GameDirectory
{
    /// <summary>启动器自建根（PCL2 式：Downloads\YanKa Launcher\.minecraft）</summary>
    public static string OwnDefault()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Path.Combine(downloads, "YanKa Launcher", ".minecraft");
    }

    public static string Detect()
    {
        // ① 设置文件指定路径（用户自配）
        if (LauncherSettings.Current.GameDirectory is { } custom
            && Directory.Exists(Path.Combine(custom, "versions")))
        {
            return custom;
        }

        // ② 自建目录（已有版本下载）
        var own = OwnDefault();
        if (Directory.Exists(Path.Combine(own, "versions"))
            && Directory.EnumerateDirectories(Path.Combine(own, "versions")).Any())
        {
            return own;
        }

        // ③ 已有环境：AppData 标准位
        var standard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        if (Directory.Exists(Path.Combine(standard, "versions"))
            && Directory.EnumerateDirectories(Path.Combine(standard, "versions")).Any())
        {
            return standard;
        }

        // ④ PCL 启动器位置：Downloads/PCL*/.minecraft/versions
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            foreach (var dir in Directory.EnumerateDirectories(downloads, "PCL*"))
            {
                var candidate = Path.Combine(dir, ".minecraft");
                if (Directory.Exists(Path.Combine(candidate, "versions"))) return candidate;
            }
        }

        // ⑤ 全新安装：自建目录
        return own;
    }

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
