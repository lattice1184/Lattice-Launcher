using Launcher.Core.Launch;

namespace Launcher.Core.Tests;

/// <summary>性能档位：JVM 参数预设（进程优先级已独立设置，见 LauncherSettingsTests）</summary>
public class PerformanceProfilesTests
{
    [Fact]
    public void Resolve_ProvidesGcArgs()
    {
        var (xmx, xms, gc) = PerformanceProfiles.Resolve(PerformanceProfile.Medium, 16384);
        Assert.Equal(4096, xmx);
        Assert.Equal(2048, xms);
        Assert.Contains("-XX:+UseG1GC", gc);
    }
}
