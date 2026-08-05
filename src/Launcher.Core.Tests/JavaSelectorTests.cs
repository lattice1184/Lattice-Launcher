using Launcher.Core.Launch;

namespace Launcher.Core.Tests;

/// <summary>AL10.2：Java 选型纯逻辑——版本要求是最低 Java，选 ≥ 要求且最接近的</summary>
public class JavaSelectorTests
{
    private static JavaSelector.JavaInstall J(int major, string name) => new(name, major);

    [Fact]
    public void BestMatch_SelectsClosestAtOrAboveRequirement()
    {
        var list = new[] { J(17, "a"), J(21, "b"), J(25, "c") };
        Assert.Equal("c", JavaSelector.BestMatch(list, 25)); // 精确匹配
        Assert.Equal("b", JavaSelector.BestMatch(list, 21)); // 精确匹配
        Assert.Equal("a", JavaSelector.BestMatch(list, 8));  // 需求低 → 最低可用（向后兼容）
        Assert.Null(JavaSelector.BestMatch(list, 30));       // 本机无满足版本
    }

    [Fact]
    public void BestMatch_NoRequirement_TakesHighest()
        => Assert.Equal("c", JavaSelector.BestMatch([J(17, "a"), J(21, "b"), J(25, "c")], null));

    [Fact]
    public void BestMatch_Empty_FallsBackToJava()
        => Assert.Equal("java", JavaSelector.BestMatch(Array.Empty<JavaSelector.JavaInstall>(), 21));
}
