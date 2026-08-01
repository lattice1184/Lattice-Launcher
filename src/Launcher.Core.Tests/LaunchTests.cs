using System.IO;
using System.Text.Json;
using Launcher.Core.Launch;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>启动参数组装测试（真实 version.json，离线环境）</summary>
public class LaunchTests
{
    private static VersionJson Load(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "versions", $"{id}.json");
        return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(path))!;
    }

    private static JavaArgumentsBuilder.LaunchProfile Build(string id, long memoryMb = 4096)
    {
        var builder = new JavaArgumentsBuilder();
        return builder.Build(Load(id), @"C:\mc", @"C:\java\bin\java.exe",
            "YanKa", "00000000-0000-0000-0000-000000000000", "token", memoryMb);
    }

    [Fact]
    public void Build_ModernVersion_AssemblesJvmAndGameArgs()
    {
        var p = Build("1.21.1");
        // JVM 参数
        Assert.Contains("-Xmx4096m", p.JvmArgs);
        Assert.Contains(p.MainClass, "net.minecraft.client.main.Main");
        // classpath 含 client jar 与 libraries
        Assert.Contains(@"C:\mc\versions\1.21.1\1.21.1.jar", p.ClassPath);
        Assert.Contains(@"org\lwjgl\lwjgl", p.ClassPath);
        // 游戏参数
        Assert.Contains("--username", p.GameArgs);
        Assert.Contains("YanKa", p.GameArgs);
        Assert.Contains("--gameDir", p.GameArgs);
        Assert.Contains(@"C:/mc", p.GameArgs);
        Assert.Contains("--assetIndex", p.GameArgs);
    }

    [Fact]
    public void AutoChinese_MergesLangIntoOptions()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ac-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllLines(Path.Combine(dir, "options.txt"),
                ["fov:1.0", "graphics:fast", "lang:en_us"]);
            AutoChinese.Apply(dir);
            var lines = File.ReadAllLines(Path.Combine(dir, "options.txt"));
            Assert.Contains("lang:zh_cn", lines);
            Assert.Contains("fov:1.0", lines); // 其他键保留
            Assert.DoesNotContain(lines, l => l == "lang:en_us");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Build_LegacyVersion_ReplacesTokens()
    {
        var p = Build("1.8.9");
        Assert.Contains("YanKa", p.GameArgs); // ${auth_player_name} 已替换
        Assert.Contains("--username", p.GameArgs);
        Assert.DoesNotContain(p.GameArgs, a => a.Contains("${auth_player_name}"));
    }

    [Fact]
    public void Build_RespectsRules_NativeWindowsInClasspath()
    {
        var p = Build("1.12.2");
        Assert.Contains("natives-windows", p.ClassPath);
        Assert.DoesNotContain(p.ClassPath, "natives-linux");
    }

    [Fact]
    public void PerformanceProfiles_ResolveSensibleValues()
    {
        var (xmx, xms, gc) = PerformanceProfiles.Resolve(PerformanceProfile.High, 16 * 1024);
        Assert.Equal(8192, xmx);
        Assert.True(gc.Length > 0);

        var (ultraXmx, _, _) = PerformanceProfiles.Resolve(PerformanceProfile.Ultra, 16 * 1024);
        Assert.True(ultraXmx > 8192, "Ultra 应按总内存 60% 分配");
    }
}
