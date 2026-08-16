using Launcher.Core.Ecosystem;

namespace Launcher.Core.Tests;

/// <summary>生态站点目录约束测试（8-16 批次 51：内置导航清单的完整性/合法性）</summary>
public class SiteCatalogTests
{
    [Fact]
    public void EveryCategory_HasAtLeast3Sites()
    {
        foreach (var category in SiteCatalog.Categories)
        {
            var count = SiteCatalog.ByCategory(category).Count;
            Assert.True(count >= 3, $"类别「{category}」只有 {count} 个站点（应 ≥3）");
        }
    }

    [Fact]
    public void AllUrls_AreHttpsAbsolute_AndUnique_AndNoSelfReference()
    {
        var urls = SiteCatalog.Sites.Select(s => s.Url).ToList();
        foreach (var url in urls)
        {
            Assert.True(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase), $"非 https：{url}");
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _), $"非法 URL：{url}");
        }
        Assert.Equal(urls.Count, urls.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // 无重复
        Assert.DoesNotContain(urls, u => u.Contains("mcnav.net", StringComparison.OrdinalIgnoreCase)); // 防自引用
    }

    [Fact]
    public void AllSites_HaveNameAndDescription()
    {
        foreach (var site in SiteCatalog.Sites)
        {
            Assert.False(string.IsNullOrWhiteSpace(site.Name), $"缺少名称：{site.Url}");
            Assert.False(string.IsNullOrWhiteSpace(site.Description), $"缺少简介：{site.Url}");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("全部")]
    public void ByCategory_All_ReturnsEverything(string? category)
        => Assert.Equal(SiteCatalog.Sites.Count, SiteCatalog.ByCategory(category).Count);

    [Fact]
    public void ByCategory_FiltersCorrectly()
    {
        var servers = SiteCatalog.ByCategory("服务端");
        Assert.True(servers.Count >= 3);
        Assert.All(servers, s => Assert.Equal("服务端", s.Category));
    }

    [Fact]
    public void ByCategory_Unknown_ReturnsEmpty()
        => Assert.Empty(SiteCatalog.ByCategory("不存在的类别"));

    [Fact]
    public void KeySites_ArePresent()
    {
        // 关键代表站点存在性（防未来误删）
        var urls = string.Join("\n", SiteCatalog.Sites.Select(s => s.Url.ToLowerInvariant()));
        foreach (var key in new[]
        {
            "mcmod.cn", "modrinth.com", "curseforge.com", "littleskin.cn", "zh.minecraft.wiki",
            "cfpa.site", "chunkbase.com", "irisshaders.dev", "mcsmanager.com", "pcl2", "mcbbs.co",
        })
            Assert.Contains(key, urls, StringComparison.Ordinal);
    }

    [Fact]
    public void Categories_AreExactlyTheEleven()
    {
        Assert.Equal(11, SiteCatalog.Categories.Count);
        Assert.Equal("官网", SiteCatalog.Categories[0]);
        Assert.Equal("面板", SiteCatalog.Categories[^1]);
    }
}
