using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>
/// 8-19 第二批：ResolveInstallPath 落点跟随版本隔离设置（四象限）——
/// 修复旧「目录存在性 = 隔离」启发式：隔离开时 mod 装进实例目录（游戏不读）与隔离关时写根（游戏不读）双向错位。
/// </summary>
public class EcosystemPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eco-path-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly bool _prevIsolation;

    public EcosystemPathTests()
    {
        Directory.CreateDirectory(_dir);
        _prevIsolation = LauncherSettings.Current.VersionIsolation;
    }

    public void Dispose()
    {
        LauncherSettings.Current.VersionIsolation = _prevIsolation;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ModPath_FollowsIsolationSetting(bool isolation, bool instanceDirExists)
    {
        LauncherSettings.Current.VersionIsolation = isolation;
        var instanceDir = Path.Combine(_dir, "versions", "1.21.1-Fabric");
        if (instanceDirExists) Directory.CreateDirectory(instanceDir);

        var result = EcosystemService.ResolveInstallPath(_dir, "1.21.1-Fabric", ProjectType.Mod);

        var expected = isolation
            ? Path.Combine(instanceDir, "mods")
            : Path.Combine(_dir, "mods");
        Assert.Equal(expected, result);
        // 隔离开且目录缺失：落点目录被创建（安装可写）
        if (isolation && !instanceDirExists)
            Assert.True(Directory.Exists(instanceDir));
    }

    [Fact]
    public void Modpack_AlwaysDownloadsFolder()
    {
        var result = EcosystemService.ResolveInstallPath(_dir, "pack-1", ProjectType.Modpack);
        Assert.Equal(Path.Combine(_dir, "downloads", "modpacks"), result);
    }
}
