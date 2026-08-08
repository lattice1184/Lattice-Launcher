namespace Launcher.Core.Download;

/// <summary>目标路径已存在时插入 " (1)" " (2)" 递增后缀，永不覆盖已有文件。</summary>
public static class UniquePath
{
    public static string Resolve(string destPath)
    {
        if (!File.Exists(destPath)) return destPath;
        var dir = Path.GetDirectoryName(destPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(destPath);
        var ext = Path.GetExtension(destPath);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
