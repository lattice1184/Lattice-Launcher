namespace Launcher.Core.Utils;

/// <summary>
/// Maven 坐标工具：org.lwjgl:lwjgl:3.3.1 → org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar
/// </summary>
public static class MavenPath
{
    /// <summary>
    /// 解析 Maven 坐标：group:artifact:version[:classifier]
    /// </summary>
    public static (string Group, string Artifact, string Version, string? Classifier) Parse(string name)
    {
        var parts = name.Split(':');
        var result = parts.Length switch
        {
            3 => (Group: parts[0], Artifact: parts[1], Version: parts[2], Classifier: (string?)null),
            4 => (Group: parts[0], Artifact: parts[1], Version: parts[2], Classifier: parts[3]),
            _ => throw new ArgumentException($"非法 Maven 坐标: {name}"),
        };
        // 防御：拒绝路径穿越片段（坐标最终拼入文件路径）
        foreach (var seg in new[] { result.Group, result.Artifact, result.Version })
        {
            if (seg.Contains("..") || seg.Contains('/') || seg.Contains('\\'))
                throw new ArgumentException($"非法 Maven 坐标（含路径分隔符）: {name}");
        }
        return result;
    }

    /// <summary>
    /// 相对路径（不含文件名），如 org/lwjgl/lwjgl/3.3.1/
    /// </summary>
    public static string DirectoryPath(string name)
    {
        var (group, artifact, version, _) = Parse(name);
        return Path.Combine(group.Replace('.', '/'), artifact, version);
    }

    /// <summary>
    /// 文件名，如 lwjgl-3.3.1.jar 或 lwjgl-3.3.1-natives-windows.jar
    /// </summary>
    public static string FileName(string name)
    {
        var (_, artifact, version, classifier) = Parse(name);
        var suffix = classifier is null ? version : $"{version}-{classifier}";
        return $"{artifact}-{suffix}.jar";
    }

    /// <summary>
    /// 完整相对路径，如 org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar
    /// </summary>
    public static string FullPath(string name) => Path.Combine(DirectoryPath(name), FileName(name));
}
