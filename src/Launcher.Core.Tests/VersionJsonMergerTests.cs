using System.Text.Json;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>inheritsFrom 链合并：子优先 / 库去重 / arguments 替换 / 下载继承 / 环与缺失保护（离线合成 JSON）</summary>
public class VersionJsonMergerTests
{
    private static VersionJson Parse(string json) => JsonSerializer.Deserialize<VersionJson>(json)!;

    private const string VanillaJson = """
        {"id":"1.20","mainClass":"net.minecraft.client.main.Main",
         "libraries":[{"name":"a:a:1.0"}],
         "arguments":{"game":["--b","--c"],"jvm":["-Db=2"]},
         "downloads":{"client":{"url":"https://mc/x.jar","size":100}}}
        """;

    [Fact]
    public void Merge_MainClassChildWins_ElseInherits()
    {
        var child = Parse("""{"id":"1.20-f","inheritsFrom":"1.20","mainClass":"knot.KnotClient"}""");
        var parent = Parse(VanillaJson);

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal("knot.KnotClient", merged.MainClass); // 子优先
        Assert.Equal("net.minecraft.client.main.Main", VersionJsonMerger.Merge(Parse("""{"id":"1.20-f","inheritsFrom":"1.20"}"""), parent).MainClass); // 缺则继承
    }

    [Fact]
    public void Merge_LibrariesDedupeByName_ChildWins()
    {
        // 同名（完整 Maven 坐标）条目：子覆盖父（以 sha 区分）；父独有条目保留
        var child = Parse("""{"id":"1.20-f","inheritsFrom":"1.20","libraries":[{"name":"a:a:1.0","downloads":{"artifact":{"url":"https://mc/a2.jar","sha1":"child-sha","size":50}}},{"name":"forge:forge:50"}]}""");
        var parent = Parse(VanillaJson);

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.Equal(2, merged.Libraries!.Count);
        var a = merged.Libraries.Single(l => l.Name == "a:a:1.0");
        Assert.Equal("child-sha", a.Downloads!.Artifact!.Sha1); // 子覆盖父
        Assert.Contains(merged.Libraries, l => l.Name == "forge:forge:50");
        Assert.DoesNotContain(merged.Libraries, l => l.Name == "a:a:2.0");
    }

    [Fact]
    public void Merge_ArgumentsParentFirstChildAppend()
    {
        var child = Parse("""{"id":"1.20-f","inheritsFrom":"1.20","arguments":{"game":["--a"],"jvm":["-Da=1"]}}""");
        var parent = Parse(VanillaJson);

        var merged = VersionJsonMerger.Merge(child, parent);

        // 父在前、子追加：fabric profile 的 game 常为 []，必须回退父版参数（--assetsDir/--assetIndex 等），否则资源链断裂
        Assert.Equal(["--b", "--c", "--a"], merged.Arguments!.Game!.Select(e => e.GetString()));
        Assert.Equal(["-Db=2", "-Da=1"], merged.Arguments.Jvm!.Select(e => e.GetString()));
    }

    [Fact]
    public void Merge_DownloadsInheritedWhenChildLacks()
    {
        var child = Parse("""{"id":"1.20-f","inheritsFrom":"1.20"}""");
        var parent = Parse(VanillaJson);

        var merged = VersionJsonMerger.Merge(child, parent);

        Assert.NotNull(merged.Downloads?.Client);
        Assert.Equal(100, merged.Downloads.Client.Size);
    }

    [Fact]
    public void ResolveChain_TwoLevels_FullyMerged()
    {
        var leaf = Parse("""{"id":"1.20.4-forge-50","inheritsFrom":"1.20.4","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","libraries":[{"name":"forge:forge:50"}]}""");
        var mid = Parse("""{"id":"1.20.4","inheritsFrom":"1.20","libraries":[{"name":"b:b:2.0"}]}""");
        var root = Parse(VanillaJson);
        var store = new Dictionary<string, VersionJson> { ["1.20.4"] = mid, ["1.20"] = root };

        var merged = VersionJsonMerger.ResolveChain(leaf, id => store.GetValueOrDefault(id));

        Assert.Null(merged.InheritsFrom); // 全链成功 → 清空
        Assert.Equal("cpw.mods.bootstraplauncher.BootstrapLauncher", merged.MainClass);
        Assert.Equal(3, merged.Libraries!.Count); // a:a:1.0 + b:b:2.0 + forge:forge:50
        Assert.NotNull(merged.Downloads?.Client); // 沿链继承
    }

    [Fact]
    public void ResolveChain_Cycle_StopsWithoutHang()
    {
        var leaf = Parse("""{"id":"a","inheritsFrom":"b"}""");
        var b = Parse("""{"id":"b","inheritsFrom":"a"}""");
        var store = new Dictionary<string, VersionJson> { ["b"] = b, ["a"] = leaf };

        var merged = VersionJsonMerger.ResolveChain(leaf, id => store.GetValueOrDefault(id));

        Assert.NotNull(merged.InheritsFrom); // 环 → 未完全解析，保留标记
    }

    [Fact]
    public void ResolveChain_MissingParent_PartialResultKeepsFlag()
    {
        var leaf = Parse("""{"id":"1.20-forge","inheritsFrom":"1.20","mainClass":"forge.Launcher"}""");

        var merged = VersionJsonMerger.ResolveChain(leaf, _ => null);

        Assert.Equal("1.20", merged.InheritsFrom); // 保留未解析标记
        Assert.Equal("forge.Launcher", merged.MainClass);
    }
}
