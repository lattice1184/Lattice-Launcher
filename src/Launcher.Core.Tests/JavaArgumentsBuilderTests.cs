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

    private static JavaArgumentsBuilder.LaunchProfile Build(VersionJson version, string gameDir = @"C:\mc", bool? versionIsolation = null)
        => new JavaArgumentsBuilder().Build(version, gameDir, @"C:\java\bin\java.exe",
            "YanKa", "00000000-0000-0000-0000-000000000000", "token", 4096, versionIsolation: versionIsolation);

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
    public void VersionIsolation_GameDirectoryPointsToVersionDir()
    {
        var p = Build(Load("1.21.1"), @"C:\mc", versionIsolation: true);

        Assert.Contains("--gameDir", p.GameArgs);
        Assert.Contains("C:/mc/versions/1.21.1", p.GameArgs);   // 隔离：game_directory → versions/{id}
        // assets 保持绝对指向共享目录
        Assert.Contains("--assetsDir", p.GameArgs);
        Assert.Contains("C:/mc/assets", p.GameArgs);
    }

    [Fact]
    public void NoIsolation_GameDirectoryIsRoot()
    {
        var p = Build(Load("1.21.1"), @"C:\mc", versionIsolation: false);

        Assert.Contains("C:/mc", p.GameArgs);
        Assert.DoesNotContain(p.GameArgs, a => a.Contains("versions/1.21.1"));
    }

    /// <summary>26.2 形态：jvm 参数 java.library.path 带 /java 子目录后缀 + natives-windows/arm64 变体。
    /// 修复：java.library.path 统一根目录（/java 后缀去重）、arm64 变体不误判为 natives。</summary>
    [Fact]
    public void Modern262_NativesJavaSubdir_DeduplicatedToRoot()
    {
        var json = """
            {
              "id":"26.2","type":"release","mainClass":"net.minecraft.client.main.Main",
              "arguments":{"jvm":["-Djava.library.path=${natives_directory}/java"]},
              "libraries":[
                {"name":"org.lwjgl:lwjgl:3.4.1","downloads":{"artifact":{"url":"https://x/l.jar","size":5}}},
                {"name":"org.lwjgl:lwjgl:3.4.1:natives-windows","downloads":{"artifact":{"url":"https://x/l-natives.jar","size":5}}},
                {"name":"org.lwjgl:lwjgl:3.4.1:natives-windows-arm64","downloads":{"artifact":{"url":"https://x/l-arm64.jar","size":5}}}
              ]
            }
            """;
        var p = Build(JsonSerializer.Deserialize<VersionJson>(json)!, @"C:\mc", versionIsolation: false);

        // java.library.path 恰好一条且指向 natives 根（JSON 的 /java 后缀被去重）
        Assert.Single(p.JvmArgs, a => a.StartsWith("-Djava.library.path="));
        var libPath = p.JvmArgs.First(a => a.StartsWith("-Djava.library.path="));
        Assert.DoesNotContain("/java", libPath);
        Assert.Contains(@"C:\mc\versions\26.2\26.2-natives", libPath);
        // natives-windows 不进 classpath（新版只解压）；arm64 变体按普通库进 classpath（精确匹配生效）
        Assert.DoesNotContain(p.ClassPath, "natives-windows.jar");
        Assert.Contains("arm64", p.ClassPath);
    }

    [Fact]
    public void ForgeStyle_MissingParent_Throws()
    {
        var child = new VersionJson("1.16.5-forge-36.2.0", "release", "forge.Launcher", null, null,
            null, null, null, null, null, null, "1.16.5");

        var ex = Assert.Throws<FileNotFoundException>(() => Build(child, @"C:\mc-empty"));

        Assert.Contains("1.16.5", ex.Message);
    }

    /// <summary>AK：PCL/第三方安装器 profile 库无 downloads 字段——按 maven 坐标推导进 classpath
    /// （旧逻辑只认 downloads.artifact，fabric-loader 链整个跳过 → KnotClient ClassNotFoundException）</summary>
    [Fact]
    public void LibraryWithoutDownloads_IncludedByMavenPath()
    {
        var json = """
            {
              "id":"1.21.6-fabric-0.19.3","type":"release","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
              "libraries":[
                {"name":"net.fabricmc:fabric-loader:0.19.3","url":"https://maven.fabricmc.net/"},
                {"name":"net.fabricmc:sponge-mixin:0.17.3+mixin.0.8.7","url":"https://maven.fabricmc.net/"}
              ]
            }
            """;
        var p = Build(JsonSerializer.Deserialize<VersionJson>(json)!, @"C:\mc", versionIsolation: false);

        Assert.Contains(@"net\fabricmc\fabric-loader\0.19.3\fabric-loader-0.19.3.jar", p.ClassPath);
        Assert.Contains(@"net\fabricmc\sponge-mixin\0.17.3+mixin.0.8.7\sponge-mixin-0.17.3+mixin.0.8.7.jar", p.ClassPath);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", p.MainClass);
    }

    /// <summary>AK：混搭 profile（部分带 downloads）——两类库都在 classpath（带 downloads 行为不回归）</summary>
    [Fact]
    public void MixedLibraries_BothIncluded()
    {
        var json = """
            {
              "id":"mixed","type":"release","mainClass":"net.minecraft.client.main.Main",
              "libraries":[
                {"name":"org.lwjgl:lwjgl:3.4.1","downloads":{"artifact":{"url":"https://x/l.jar","size":5}}},
                {"name":"net.fabricmc:fabric-loader:0.19.3","url":"https://maven.fabricmc.net/"}
              ]
            }
            """;
        var p = Build(JsonSerializer.Deserialize<VersionJson>(json)!, @"C:\mc", versionIsolation: false);

        Assert.Contains(@"org\lwjgl\lwjgl\3.4.1\lwjgl-3.4.1.jar", p.ClassPath);
        Assert.Contains(@"net\fabricmc\fabric-loader\0.19.3\fabric-loader-0.19.3.jar", p.ClassPath);
    }
}
