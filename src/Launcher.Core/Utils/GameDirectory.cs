namespace Launcher.Core.Utils;

/// <summary>
/// 游戏目录探测：优先使用实际存在的 .minecraft（PCL 启动器把游戏目录放在启动器目录下）。
/// </summary>
public static class GameDirectory
{
    public static string Detect()
    {
        // 标准位置
        var standard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        if (Directory.Exists(Path.Combine(standard, "versions"))) return standard;

        // PCL 启动器位置：Downloads/PCL*/PCL 正式版*/.minecraft/versions
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            foreach (var dir in Directory.EnumerateDirectories(downloads, "PCL*"))
            {
                var candidate = Path.Combine(dir, ".minecraft");
                if (Directory.Exists(Path.Combine(candidate, "versions"))) return candidate;
            }
        }
        return standard;
    }
}
