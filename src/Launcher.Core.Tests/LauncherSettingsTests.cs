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
}
