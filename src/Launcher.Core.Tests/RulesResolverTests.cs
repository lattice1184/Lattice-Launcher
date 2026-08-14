using System.IO;
using System.Text.Json;
using Launcher.Core.Launch;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>
/// Rules 求值逻辑 + 真实版本 JSON 的 natives 过滤验证（windows 环境）
/// </summary>
public class RulesResolverTests
{
    private static RulesResolver WindowsResolver() => new()
    {
        OsName = "windows",
        OsVersion = "Windows 11 Pro for Workstations 10.0.26200",
        Arch = "x64",
    };

    private static VersionJson Load(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "versions", $"{id}.json");
        return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(path))!;
    }

    [Fact]
    public void NoRules_IsAllowed()
    {
        var r = WindowsResolver();
        Assert.True(r.IsAllowed(null));
        Assert.True(r.IsAllowed([]));
    }

    [Fact]
    public void OsNameRule_MatchesWindows()
    {
        var r = WindowsResolver();
        Assert.True(r.IsAllowed([new RuleJson("allow", new RuleOsInfo("windows", null, null), null)]));
        Assert.False(r.IsAllowed([new RuleJson("allow", new RuleOsInfo("osx", null, null), null)]));
        Assert.False(r.IsAllowed([new RuleJson("allow", new RuleOsInfo("linux", null, null), null)]));
    }

    [Fact]
    public void LastMatchingRule_Wins()
    {
        var r = WindowsResolver();
        var rules = new List<RuleJson>
        {
            new("allow", new RuleOsInfo("windows", null, null), null),
            new("disallow", new RuleOsInfo("windows", "10\\.0\\.26.*", null), null),
        };
        // 版本 10.0.26200 匹配 disallow 正则 → 最终为 disallow
        Assert.False(r.IsAllowed(rules));
    }

    [Fact]
    public void FeatureRule_NotPresent_IsNotAllowed()
    {
        var r = WindowsResolver();
        Assert.False(r.IsAllowed([new RuleJson("allow", null, new Dictionary<string, bool> { ["has_custom_resolution"] = true })]));
    }

    [Fact]
    public void Real_1211_NativeClassifiers_FilterByOs()
    {
        var v = Load("1.21.1");
        // 1.21.1 的 natives 以独立 library 条目存在（org.lwjgl:lwjgl-stb:natives-windows 等）
        var lwjgl = v.Libraries!.First(l => l.Name.StartsWith("org.lwjgl:lwjgl:"));
        Assert.True(lwjgl.Name.Contains("org.lwjgl:lwjgl:"));
        var windowsNatives = v.Libraries.Count(l => l.Name.Contains("natives-windows", StringComparison.OrdinalIgnoreCase));
        Assert.True(windowsNatives > 3, $"应有多个 natives-windows 条目，实际 {windowsNatives}");
    }

    [Fact]
    public void Real_1211_NativeRules_AllowWindows_RejectLinux()
    {
        var v = Load("1.21.1");
        var r = WindowsResolver();
        // 1.21.1 的 lwjgl natives 条目以独立 library（带 rules）出现，如 org.lwjgl:lwjgl-xxx:natives-windows
        var winNative = v.Libraries!.First(l => l.Name.Contains("natives-windows", StringComparison.OrdinalIgnoreCase));
        Assert.True(r.IsAllowed(winNative.Rules));

        var linuxNative = v.Libraries!.First(l => l.Name.Contains("natives-linux", StringComparison.OrdinalIgnoreCase));
        Assert.False(r.IsAllowed(linuxNative.Rules));
    }

    [Fact]
    public void Real_1122_NativesField_WindowsClassifierAllowed()
    {
        var v = Load("1.12.2");
        var r = WindowsResolver();
        // 1.12.2 形态：natives 字段在 lwjgl-platform 条目上，classifier 键 = natives[os]
        var platform = v.Libraries!.First(l => l.Name.Contains("lwjgl-platform", StringComparison.OrdinalIgnoreCase)
                                               && l.Name.Contains("2.9.4", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("natives-windows", platform.Natives!["windows"]);

        // 整条 rules 求值（allow + disallow osx）→ windows 放行、osx 排除
        Assert.True(r.IsAllowed(platform.Rules));
        var macResolver = new RulesResolver { OsName = "osx" };
        Assert.False(macResolver.IsAllowed(platform.Rules));

        // classifier 选择：按 natives 映射取键
        var classifierKey = platform.Natives["windows"];
        Assert.True(platform.Downloads!.Classifiers!.ContainsKey(classifierKey));
    }

    [Fact]
    public void Real_189_ParsesAndResolves()
    {
        var v = Load("1.8.9");
        var r = WindowsResolver();
        // 1.8.9 的 natives 字段在 lwjgl-platform 条目上
        var lwjglPlatform = v.Libraries!.First(l => l.Name.Contains("lwjgl-platform", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("natives-windows", lwjglPlatform.Natives!["windows"]);
    }
}
