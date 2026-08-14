using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>版本级启动配置：读写 / 合并优先级 / 清除</summary>
public class VersionConfigServiceTests
{
    private static (string GameDir, string Id) TempVersion()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vcfg-{Guid.NewGuid():N}");
        var id = "1.21.1";
        Directory.CreateDirectory(Path.Combine(dir, "versions", id));
        return (dir, id);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip()
    {
        var (dir, id) = TempVersion();
        try
        {
            var cfg = new VersionConfig { MemoryMb = 8192, JavaPath = @"C:\java\jdk21\bin\java.exe" };
            VersionConfigService.Save(dir, id, cfg);
            var loaded = VersionConfigService.Load(dir, id);
            Assert.Equal(8192, loaded.MemoryMb);
            Assert.Equal(@"C:\java\jdk21\bin\java.exe", loaded.JavaPath);
            Assert.True(loaded.HasOverrides);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_Missing_AllNull()
    {
        var (dir, id) = TempVersion();
        try
        {
            var cfg = VersionConfigService.Load(dir, id);
            Assert.Null(cfg.MemoryMb);
            Assert.Null(cfg.JavaPath);
            Assert.False(cfg.HasOverrides);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Merge_VersionOverridesGlobal()
    {
        var (dir, id) = TempVersion();
        try
        {
            var global = new LauncherSettings { MemoryMb = 4096, JavaPath = null, ExtraJvmArgs = "-Dx=1" };
            var cfg = new VersionConfig { MemoryMb = 2048 };
            VersionConfigService.Save(dir, id, cfg);

            var (mem, java, args) = VersionConfigService.Merge(dir, id, global);
            Assert.Equal(2048, mem);            // 版本级覆盖
            Assert.Null(java);                  // 版本级 null → 全局
            Assert.Equal("-Dx=1", args);        // 版本级 null → 全局
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Reset_RemovesOverrides()
    {
        var (dir, id) = TempVersion();
        try
        {
            VersionConfigService.Save(dir, id, new VersionConfig { MemoryMb = 2048 });
            VersionConfigService.Reset(dir, id);
            Assert.False(VersionConfigService.Load(dir, id).HasOverrides);
            Assert.False(File.Exists(Path.Combine(dir, "versions", id, ".yanla-config.json")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
