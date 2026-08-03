using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>启动器设置：默认值 / 读写 / 坏 JSON 回退</summary>
public class LauncherSettingsTests
{
    [Fact]
    public void Defaults_VersionIsolationOn()
    {
        var s = new LauncherSettings();
        Assert.True(s.VersionIsolation);
        Assert.Null(s.GameDirectory);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new LauncherSettings { GameDirectory = @"C:\Users\test\YanKa Launcher\.minecraft", VersionIsolation = false };
            s.Save(path);

            var loaded = LauncherSettings.Load(path);
            Assert.Equal(@"C:\Users\test\YanKa Launcher\.minecraft", loaded.GameDirectory);
            Assert.False(loaded.VersionIsolation);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_BrokenJson_FallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not valid json !!!");
            var loaded = LauncherSettings.Load(path);
            Assert.True(loaded.VersionIsolation);
            Assert.Null(loaded.GameDirectory);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_Defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        var loaded = LauncherSettings.Load(path);
        Assert.NotNull(loaded);
    }

    [Fact]
    public void Defaults_LaunchFields()
    {
        var s = new LauncherSettings();
        Assert.Equal(4096, s.MemoryMb);
        Assert.Null(s.JavaPath);
        Assert.Null(s.ExtraJvmArgs);
        Assert.True(s.AutoChineseEnabled);
        Assert.True(s.MirrorFallbackEnabled);
        Assert.Equal(0, s.MaxConcurrentDownloads);
    }

    [Fact]
    public void SaveAndLoad_LaunchFields_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new LauncherSettings
            {
                MemoryMb = 8192,
                JavaPath = @"C:\Program Files\Java\jdk-21in\java.exe",
                ExtraJvmArgs = "-Dxxx=1 -Xss2m",
                AutoChineseEnabled = false,
                MirrorFallbackEnabled = false,
                MaxConcurrentDownloads = 12,
            };
            s.Save(path);

            var loaded = LauncherSettings.Load(path);
            Assert.Equal(8192, loaded.MemoryMb);
            Assert.Equal(@"C:\Program Files\Java\jdk-21in\java.exe", loaded.JavaPath);
            Assert.Equal("-Dxxx=1 -Xss2m", loaded.ExtraJvmArgs);
            Assert.False(loaded.AutoChineseEnabled);
            Assert.False(loaded.MirrorFallbackEnabled);
            Assert.Equal(12, loaded.MaxConcurrentDownloads);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Defaults_DownloadTierFields()
    {
        var s = new LauncherSettings();
        Assert.Equal(DownloadTier.Low, s.DownloadTier);
        Assert.Equal(0, s.ChunkCount);
        Assert.Equal(0, s.BufferSize);
        Assert.Equal("", s.CurseForgeApiKey);
    }

    [Fact]
    public void SaveAndLoad_DownloadTierFields_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new LauncherSettings
            {
                DownloadTier = DownloadTier.High,
                ChunkCount = 12,
                BufferSize = 163840,
                CurseForgeApiKey = "cf-key-abc",
            };
            s.Save(path);

            var loaded = LauncherSettings.Load(path);
            Assert.Equal(DownloadTier.High, loaded.DownloadTier);
            Assert.Equal(12, loaded.ChunkCount);
            Assert.Equal(163840, loaded.BufferSize);
            Assert.Equal("cf-key-abc", loaded.CurseForgeApiKey);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
