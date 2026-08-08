using Launcher.Core.Diagnostics;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>AL10.2：下载后完整性校验——修复不得"虚假成功"，缺失如实报告</summary>
public class AutoRepairServiceTests
{
    private static VersionJson BuildVersion()
    {
        var lib = new LibraryJson("net.fabricmc:fabric-loader:0.19.3", null, null, null, null, null, null, null);
        return new VersionJson("1.21.11", "release", "net.minecraft.client.main.Main",
            null, null, null, null, [lib], null, null, null, null);
    }

    [Fact]
    public void VerifyFiles_ReportsAllMissing_ThenEmptyAfterFill()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"verify-{Guid.NewGuid():N}");
        var version = BuildVersion();

        // 全新目录：client jar + library 都缺
        var missing = AutoRepairService.VerifyFiles(version, gameDir);
        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, p => p.EndsWith($"{Path.DirectorySeparatorChar}1.21.11.jar"));
        Assert.Contains(missing, p => p.Contains("fabric-loader-0.19.3.jar"));

        // 补齐后完整
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", "1.21.11"));
        File.WriteAllText(Path.Combine(gameDir, "versions", "1.21.11", "1.21.11.jar"), "x");
        Directory.CreateDirectory(Path.Combine(gameDir, "libraries", "net", "fabricmc", "fabric-loader", "0.19.3"));
        File.WriteAllText(Path.Combine(gameDir, "libraries", "net", "fabricmc", "fabric-loader", "0.19.3", "fabric-loader-0.19.3.jar"), "x");

        Assert.Empty(AutoRepairService.VerifyFiles(version, gameDir));
    }

    /// <summary>AL11：VerifyFiles 按 OS 规则过滤——linux-only natives 库不会下载，不应误报缺失</summary>
    [Fact]
    public void VerifyFiles_SkipsOtherOsLibraries()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"verify-{Guid.NewGuid():N}");
        var libs = new List<LibraryJson>
        {
            new("net.fabricmc:fabric-loader:0.19.3", null, null, null, null, null, null, null),
            new("org.lwjgl:lwjgl-glfw:3.2.2", null, null, null, null,
                [new RuleJson("allow", new RuleOsInfo("linux", null, null), null)], null, null),
        };
        var version = new VersionJson("1.21.11", "release", "net.minecraft.client.main.Main",
            null, null, null, null, libs, null, null, null, null);

        var missing = AutoRepairService.VerifyFiles(version, gameDir);
        Assert.Equal(2, missing.Count); // client jar + fabric-loader；linux-only 库被过滤
        Assert.DoesNotContain(missing, p => p.Contains("lwjgl-glfw-3.2.2"));
    }
}
