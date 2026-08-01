namespace Launcher.Core.Launch;

/// <summary>
/// Java 自动选配：按版本要求的 JDK 大版本选择本机可用 Java。
/// 探测 PCL 缓存的 runtime（AppData\Roaming\.minecraft\runtime）+ PATH 兜底。
/// </summary>
public static class JavaSelector
{
    private static readonly (string Name, int Major)[] Runtimes =
    [
        ("java-runtime-delta", 21),
        ("java-runtime-beta", 17),
        ("java-runtime-alpha", 16),
        ("java-runtime-epsilon", 25),
        ("jre-legacy", 8),
    ];

    /// <summary>选择 Java 可执行文件路径；找不到时返回 "java"（PATH 兜底）</summary>
    public static string Pick(int? requiredMajor)
    {
        // 1. 精确匹配版本要求的 runtime
        if (requiredMajor is { } major)
        {
            var best = Runtimes
                .Where(r => r.Major <= major)
                .OrderByDescending(r => r.Major)
                .FirstOrDefault();
            if (best.Name is not null)
            {
                var exact = FindRuntime(best.Name);
                if (exact is not null) return exact;
            }
        }

        // 2. 任何可用 runtime（优先 21）
        foreach (var (name, _) in Runtimes)
        {
            var exe = FindRuntime(name);
            if (exe is not null) return exe;
        }

        // 3. PATH 兜底
        return "java";
    }

    private static string? FindRuntime(string name)
    {
        var runtimeBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime");
        // 官方启动器布局与简化布局两种形态
        var candidates = new[]
        {
            Path.Combine(runtimeBase, name, "windows-x64", name, "bin", "java.exe"),
            Path.Combine(runtimeBase, name, "bin", "java.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
