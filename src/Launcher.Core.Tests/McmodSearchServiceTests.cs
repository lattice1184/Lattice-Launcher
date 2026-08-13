using Launcher.Core.Services;

namespace Launcher.Core.Tests;

/// <summary>MC百科中文搜索链路解析（AL63）：搜索结果页条目 / 详情页双层 base64 解 slug / 中文判定</summary>
public class McmodSearchServiceTests
{
    [Fact]
    public void ParseSearchResults_ExtractsClassIdAndTitle()
    {
        // 真实页面结构（08-11 实测抓取格式）
        var html = """
            <div class="b"><a target="_blank" href="https://www.mcmod.cn/class/9090.html">遗落荒野 (Missing Wilds)</a></div>
            <div class="b"><a target="_blank" href="https://www.mcmod.cn/class/1234.html">钠 (Sodium)</a></div>
            """;
        var entries = McmodSearchService.ParseSearchResults(html);

        Assert.Equal(2, entries.Count);
        Assert.Equal("9090", entries[0].ClassId);
        Assert.Contains("遗落荒野", entries[0].Title);
        Assert.Equal("1234", entries[1].ClassId);
    }

    [Fact]
    public void DecodeModrinthSlug_ExtractsSlugFromBase64Link()
    {
        // 真实页面结构：href="//link.mcmod.cn/target/{base64(完整 URL)}"
        // base64("https://modrinth.com/mod/missing-wilds") = aHR0cHM6Ly9tb2RyaW50aC5jb20vbW9kL21pc3Npbmctd2lsZHM=
        var html = """
            <li><a data-toggle="tooltip" data-original-title="Modrinth" target="_blank" rel="nofollow noreferrer" target="_blank"
                href="//link.mcmod.cn/target/aHR0cHM6Ly9tb2RyaW50aC5jb20vbW9kL21pc3Npbmctd2lsZHM=">
                <svg class="common-mcicon common-linkicon common-linkicon-modrinth"></svg></a></li>
            """;

        var slug = McmodSearchService.DecodeModrinthSlug(html);

        Assert.Equal("missing-wilds", slug);
    }

    [Fact]
    public void DecodeModrinthSlug_NoModrinthLink_ReturnsNull()
    {
        // 只有 CurseForge 链接（base64("https://www.curseforge.com/minecraft/mc-mods/sodium")）
        var html = """
            <a data-original-title="CurseForge" href="//link.mcmod.cn/target/aHR0cHM6Ly93d3cuY3Vyc2Vmb3JnZS5jb20vbWluZWNyYWZ0L21jLW1vZHMvc29kaXVt">
            <svg class="common-linkicon-curseforge"></svg></a>
            """;
        Assert.Null(McmodSearchService.DecodeModrinthSlug(html));
    }

    [Theory]
    [InlineData("遗落荒野", true)]
    [InlineData("missing wilds", false)]
    [InlineData("  ", false)]
    [InlineData("钠", true)]
    public void ContainsChinese_DetectsCjk(string query, bool expected)
        => Assert.Equal(expected, McmodSearchService.ContainsChinese(query));
}
