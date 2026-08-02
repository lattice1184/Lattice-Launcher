using System.IO;
using System.Text.Json;
using Launcher.Core.Launch;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>JVM 参数组装：arguments.jvm 发射 / classpath 统一末尾 / inheritsFrom 链 / 缺失父版本报错</summary>
public class JavaArgumentsBuilderTests
{
    private static VersionJson Load(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "versions", $"{id}.json");
        return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(path))!;
    }

    private static JavaArgumentsBuilder.LaunchProfile Build(VersionJson version, string gameDir = @"C:\mc")
        => new JavaArgumentsBuilder().Build(version, gameDir, @"C:\java\bin\java.exe",
            "YanKa", "00000000-0000-0000-0000-000000000000", "token", 4096);

    [Fact]
    public void Modern_1_21_1_JvmArgsEmittedAndDeduped()
    {
        var p = Build(Load("1.21.1"));

        // -Djava.library.path 恰好一次（基础参数与 json 的 ${natives_directory} 去重）
        Assert.Single(p.JvmArgs, a => a.StartsWith("-Djava.library.path="));
        // classpath 统一在末尾，且无残留 ${classpath}
        Assert.Equal("-cp", p.JvmArgs[^2]);
        Assert.Equal(p.ClassPath, p.JvmArgs[^1]);
        Assert.DoesNotContain(p.JvmArgs, a => a.Contains("${classpath}"));
        Assert.Contains("-Xmx4096m", p.JvmArgs);
        Assert.Equal("net.minecraft.client.main.Main", p.MainClass);
    }

    [Fact]
    public void Legacy_1_8_9_Unchanged_ClasspathAppended()
    {
        var p = Build(Load("1.8.9"));

        Assert.Contains("-Xmx4096m", p.JvmArgs);
        Assert.Contains("YanKa", p.GameArgs);
        Assert.DoesNotContain(p.GameArgs, a => a.Contains("${auth_player_name}"));
        Assert.Equal(p.ClassPath, p.JvmArgs[^1]);
        Assert.Contains(@"1.8.9\1.8.9.jar", p.ClassPath);
    }

    [Fact]
    public void ForgeStyle_InheritsFrom_MergesParentAndEmitsModuleArgs()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "versions", "1.16.5"));
        var childDir = Path.Combine(dir, "versions", "1.16.5-forge-36.2.0");
        Directory.CreateDirectory(childDir);
        try
        {
            // 父：原版（minecraftArguments 旧式 + 基础库）
            var parent = new VersionJson("1.16.5", "release", "net.minecraft.client.main.Main", "1.16",
                new AssetIndexInfo("1.16", "https://mc/i.json", "s", 10, 100),
                null, "--username ${auth_player_name} --gameDir ${game_directory}",
                [new LibraryJson("org.lwjgl:lwjgl:3.2.2", new LibraryDownloads(
                    new DownloadFileInfo("https://mc/lwjgl.jar", "s1", 100), null), null, null, null)],
                null, null, null, null);
            // 子：Forge（inheritsFrom + bootstraplauncher jvm 参数）
            var child = new VersionJson("1.16.5-forge-36.2.0", "release",
                "cpw.mods.bootstraplauncher.BootstrapLauncher", null, null,
                new ArgumentsInfo(null, [JsonSerializer.SerializeToElement("-p"),
                    JsonSerializer.SerializeToElement("C:/bootstraplauncher.jar"),
                    JsonSerializer.SerializeToElement("--add-modules"),
                    JsonSerializer.SerializeToElement("ALL-MODULE-PATH"),
                    JsonSerializer.SerializeToElement("-Djava.library.path=${natives_directory}")]),
                null,
                [new LibraryJson("net.minecraftforge:forge:36.2.0", new LibraryDownloads(
                    new DownloadFileInfo("https://mc/forge.jar", "s2", 200), null), null, null, null)],
                null, null, null, "1.16.5");

            File.WriteAllText(Path.Combine(dir, "versions", "1.16.5", "1.16.5.json"), JsonSerializer.Serialize(parent));
            File.WriteAllText(Path.Combine(childDir, "1.16.5-forge-36.2.0.json"), JsonSerializer.Serialize(child));

            var p = Build(child, dir);

            Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", p.MainClass); // 子优先
            Assert.Contains("-p", p.JvmArgs);                                          // 模块参数已发射
            Assert.Contains("C:/bootstraplauncher.jar", p.JvmArgs);
            Assert.Contains("--add-modules", p.JvmArgs);
            Assert.Contains("ALL-MODULE-PATH", p.JvmArgs);
            Assert.Single(p.JvmArgs, a => a.StartsWith("-Djava.library.path="));       // 去重
            Assert.Contains(@"org\lwjgl\lwjgl\3.2.2\lwjgl-3.2.2.jar", p.ClassPath);      // 父库已合并
            Assert.Contains(@"net\minecraftforge\forge\36.2.0\forge-36.2.0.jar", p.ClassPath);
            Assert.DoesNotContain(p.JvmArgs, a => a.Contains("${"));
            Assert.Equal(p.ClassPath, p.JvmArgs[^1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ForgeStyle_MissingParent_Throws()
    {
        var child = new VersionJson("1.16.5-forge-36.2.0", "release", "forge.Launcher", null, null,
            null, null, null, null, null, null, "1.16.5");

        var ex = Assert.Throws<FileNotFoundException>(() => Build(child, @"C:\mc-empty"));

        Assert.Contains("1.16.5", ex.Message);
    }
}
