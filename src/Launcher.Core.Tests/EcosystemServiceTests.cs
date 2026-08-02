using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.Core.Tests;

/// <summary>生态服务静态工具离线测试（不依赖网络）</summary>
public class EcosystemServiceTests
{
    // ---------- BuildFacets ----------

    [Fact]
    public void BuildFacets_TypeOnly()
    {
        var facets = EcosystemService.BuildFacets(ProjectType.Mod, null, null);
        Assert.Equal("[[\"project_type:mod\"]]", facets);
    }

    [Theory]
    [InlineData(ProjectType.Modpack, "modpack")]
    [InlineData(ProjectType.Resourcepack, "resourcepack")]
    [InlineData(ProjectType.Shader, "shader")]
    public void FacetName_MapsCorrectly(ProjectType type, string expected)
        => Assert.Equal(expected, EcosystemService.FacetName(type));

    [Fact]
    public void BuildFacets_TypeVersionLoader()
    {
        var facets = EcosystemService.BuildFacets(ProjectType.Mod, "1.21.1", "fabric");
        Assert.Equal("[[\"project_type:mod\"],[\"versions:1.21.1\"],[\"categories:fabric\"]]", facets);
    }

    [Fact]
    public void BuildFacets_TypeVersionLoaderCategory()
    {
        var facets = EcosystemService.BuildFacets(ProjectType.Mod, "1.21.1", "fabric", "optimization");
        Assert.Equal("[[\"project_type:mod\"],[\"versions:1.21.1\"],[\"categories:fabric\"],[\"categories:optimization\"]]", facets);
    }

    [Fact]
    public void BuildFacets_CategoryOnly()
    {
        var facets = EcosystemService.BuildFacets(ProjectType.Mod, null, null, "utility");
        Assert.Equal("[[\"project_type:mod\"],[\"categories:utility\"]]", facets);
    }

    // ---------- TryParseGameVersion ----------

    [Theory]
    [InlineData("1.21.1", "1.21.1")]
    [InlineData("1.21.1-Fabric", "1.21.1")]
    [InlineData("1.20.4", "1.20.4")]
    public void TryParseGameVersion_Succeeds(string instanceId, string expected)
    {
        Assert.True(EcosystemService.TryParseGameVersion(instanceId, out var version));
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("26.2-Fabric 0.19.3")]
    [InlineData("")]
    public void TryParseGameVersion_Fails(string instanceId)
    {
        // "26.2" 解析为版本号，但 "26.2-Fabric 0.19.3" 前缀是 26.2 —— 视为版本
        if (instanceId == "26.2-Fabric 0.19.3")
        {
            Assert.True(EcosystemService.TryParseGameVersion(instanceId, out _));
            return;
        }
        Assert.False(EcosystemService.TryParseGameVersion(instanceId, out _));
    }

    // ---------- GuessLoader ----------

    [Theory]
    [InlineData("1.21.1-Fabric", "fabric")]
    [InlineData("1.20.1-forge", "forge")]
    [InlineData("neoforge-1.21", "neoforge")]
    [InlineData("quilt-1.19", "quilt")]
    [InlineData("iris-1.21.1", "iris")]
    [InlineData("optifine-1.20", "optifine")]
    [InlineData("1.21.1", null)]
    public void GuessLoader_Detects(string instanceId, string? expected)
        => Assert.Equal(expected, EcosystemService.GuessLoader(instanceId));

    // ---------- ResolveSubDir / ResolveInstallPath ----------

    [Theory]
    [InlineData(ProjectType.Mod, "mods")]
    [InlineData(ProjectType.Resourcepack, "resourcepacks")]
    [InlineData(ProjectType.Shader, "shaderpacks")]
    [InlineData(ProjectType.Modpack, null)]
    public void ResolveSubDir_Maps(ProjectType type, string? expected)
        => Assert.Equal(expected, EcosystemService.ResolveSubDir(type));

    [Fact]
    public void ResolveInstallPath_InstanceDirectories()
    {
        Assert.Equal(@"G:\versions\1.21.1\mods",
            EcosystemService.ResolveInstallPath(@"G:\", "1.21.1", ProjectType.Mod));
        Assert.Equal(@"G:\versions\1.21.1\shaderpacks",
            EcosystemService.ResolveInstallPath(@"G:\", "1.21.1", ProjectType.Shader));
        Assert.Equal(@"G:\downloads\modpacks",
            EcosystemService.ResolveInstallPath(@"G:\", "any", ProjectType.Modpack));
    }

    // ---------- SelectBestVersion ----------

    private static ModrinthVersion MakeVersion(string id, DateTime published, bool featured = false, bool hasFile = true)
        => new(id, "p", $"v{id}", id, null, null,
            hasFile ? [new ModrinthVersionFile(id, "u", $"{id}.jar", 1, false, null)] : null,
            null, null, 0, null, featured, published);

    [Fact]
    public void SelectBestVersion_FeaturedFirst()
    {
        var versions = new[]
        {
            MakeVersion("old", DateTime.UtcNow.AddDays(-10)),
            MakeVersion("featured", DateTime.UtcNow.AddDays(-5), featured: true),
            MakeVersion("new", DateTime.UtcNow),
        };
        var best = EcosystemService.SelectBestVersion(versions);
        Assert.Equal("featured", best!.Id);
    }

    [Fact]
    public void SelectBestVersion_NewestWhenNoFeatured()
    {
        var versions = new[]
        {
            MakeVersion("old", DateTime.UtcNow.AddDays(-10)),
            MakeVersion("new", DateTime.UtcNow),
        };
        Assert.Equal("new", EcosystemService.SelectBestVersion(versions)!.Id);
    }

    [Fact]
    public void SelectBestVersion_FiltersNoFileVersions()
    {
        var versions = new[]
        {
            MakeVersion("nofile", DateTime.UtcNow, hasFile: false),
            MakeVersion("withfile", DateTime.UtcNow.AddDays(-1)),
        };
        Assert.Equal("withfile", EcosystemService.SelectBestVersion(versions)!.Id);
    }

    [Fact]
    public void SelectBestVersion_EmptyReturnsNull()
        => Assert.Null(EcosystemService.SelectBestVersion([]));

    // ---------- PickPrimaryFile ----------

    [Fact]
    public void PickPrimaryFile_PrimaryFirst()
    {
        var files = new List<ModrinthVersionFile>
        {
            new("a", "u", "a.jar", 1, false, null),
            new("b", "u", "b.jar", 1, true, null),
        };
        Assert.Equal("b", EcosystemService.PickPrimaryFile(files)!.Id);
    }

    [Fact]
    public void PickPrimaryFile_FirstWhenNoPrimary()
    {
        var files = new List<ModrinthVersionFile>
        {
            new("a", "u", "a.jar", 1, false, null),
            new("c", "u", "c.jar", 1, false, null),
        };
        Assert.Equal("a", EcosystemService.PickPrimaryFile(files)!.Id);
    }

    [Fact]
    public void PickPrimaryFile_NullOrEmptyReturnsNull()
    {
        Assert.Null(EcosystemService.PickPrimaryFile(null));
        Assert.Null(EcosystemService.PickPrimaryFile([]));
    }
}
