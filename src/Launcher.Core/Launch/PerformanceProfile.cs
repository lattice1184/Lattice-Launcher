namespace Launcher.Core.Launch;

/// <summary>
/// 性能管线预设：控制 JVM 参数（内存/GC/线程）+ 进程优先级（联动，不分开设置），不动游戏内设置。
/// </summary>
public enum PerformanceProfile
{
    Low,
    Medium,
    High,
    Ultra,
}

public static class PerformanceProfiles
{
    /// <summary>按预设返回 (Xmx, Xms, GC 参数数组)。Ultra 按总内存 60% 自动。</summary>
    public static (long XmxMb, long XmsMb, string[] GcArgs) Resolve(PerformanceProfile profile, long totalMemoryMb)
    {
        return profile switch
        {
            PerformanceProfile.Low => (2048, 1024, ["-XX:+UseG1GC", "-XX:MaxGCPauseMillis=200"]),
            PerformanceProfile.Medium => (4096, 2048, ["-XX:+UseG1GC", "-XX:MaxGCPauseMillis=100"]),
            PerformanceProfile.High => (8192, 2048, ["-XX:+UseG1GC", "-XX:MaxGCPauseMillis=50", "-XX:+ParallelRefProcEnabled"]),
            PerformanceProfile.Ultra => (Math.Max(4096, (long)(totalMemoryMb * 0.6)), Math.Min(4096, (long)(totalMemoryMb * 0.3)),
                ["-XX:+UseG1GC", "-XX:MaxGCPauseMillis=50", "-XX:+ParallelRefProcEnabled", "-XX:+UseStringDeduplication"]),
            _ => (4096, 2048, ["-XX:+UseG1GC"]),
        };
    }

}
