using Launcher.Core.Ecosystem;

namespace Launcher.Core.Tests;

/// <summary>依赖解析器测试（合成数据，离线）</summary>
public class DependencyResolverTests
{
    private static ModDependencyFile MakeFile(string id, string version, int releaseType = 1,
        string[]? gameVersions = null, string[]? loaders = null,
        List<ModDependencyReference>? deps = null) => new()
    {
        Id = id,
        DisplayName = id,
        Version = version,
        GameVersions = [.. gameVersions ?? ["1.21.1"]],
        Loaders = [.. loaders ?? ["fabric"]],
        ReleaseType = releaseType,
        ReleaseDate = DateTime.UtcNow.AddDays(-1),
        RequiredDependencies = deps ?? [],
    };

    [Fact]
    public void Resolve_NoDependencies_InstallsMainOnly()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.21.1",
            TargetLoaders = ["fabric"],
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            ProjectResolver = (_, id) => id == "main"
                ? new ModDependencyProject { ProjectId = id, Source = "modrinth", Files = [MakeFile("v1", "1.0")] }
                : null,
        });

        Assert.Single(result.ToInstall);
        Assert.Equal("main", result.ToInstall[0].ProjectId);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Resolve_ChainedDependencies_InstallsAll()
    {
        // main → dep1 → dep2（两层）
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.21.1",
            TargetLoaders = ["fabric"],
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            ProjectResolver = (_, id) => id switch
            {
                "main" => new ModDependencyProject
                {
                    ProjectId = id, Source = "modrinth",
                    Files = [MakeFile("v1", "1.0", deps: [new ModDependencyReference { ProjectId = "dep1", Source = "modrinth" }])],
                },
                "dep1" => new ModDependencyProject
                {
                    ProjectId = id, Source = "modrinth",
                    Files = [MakeFile("v2", "1.0", deps: [new ModDependencyReference { ProjectId = "dep2", Source = "modrinth" }])],
                },
                "dep2" => new ModDependencyProject
                {
                    ProjectId = id, Source = "modrinth",
                    Files = [MakeFile("v3", "1.0")],
                },
                _ => null,
            },
        });

        Assert.Equal(3, result.ToInstall.Count);
        Assert.Contains(result.ToInstall, i => i.ProjectId == "dep2");
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Resolve_UnresolvableDependency_ReportsReason()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.21.1",
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "ghost", Source = "modrinth" },
            ],
            ProjectResolver = (_, _) => null,
        });

        Assert.Empty(result.ToInstall);
        Assert.Single(result.Unresolved);
        Assert.Contains("not found", result.Unresolved[0].Reason);
    }

    [Fact]
    public void Resolve_InstalledMod_Satisfied()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.21.1",
            TargetLoaders = ["fabric"],
            InstalledMods =
            [
                new InstalledModIdentity { SourceProjectId = "main", Source = "modrinth", GameVersions = ["1.21.1"], Loaders = ["fabric"] },
            ],
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            ProjectResolver = (_, _) => null,
        });

        Assert.Empty(result.ToInstall);
        Assert.Single(result.Satisfied);
    }

    [Fact]
    public void Resolve_VersionMismatch_NoCompatibleFile()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.20.4",
            TargetLoaders = ["forge"],
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            // 项目只有 1.21.1 + fabric 的文件
            ProjectResolver = (_, id) => new ModDependencyProject
            {
                ProjectId = id, Source = "modrinth",
                Files = [MakeFile("v1", "1.0", gameVersions: ["1.21.1"], loaders: ["fabric"])],
            },
        });

        Assert.Empty(result.ToInstall);
        Assert.Single(result.Unresolved);
        Assert.Contains("No compatible file", result.Unresolved[0].Reason);
    }

    [Fact]
    public void Resolve_CircularDependency_NoInfiniteLoop()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.21.1",
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "a", Source = "modrinth" },
            ],
            ProjectResolver = (_, id) => new ModDependencyProject
            {
                ProjectId = id, Source = "modrinth",
                Files = [MakeFile("v1", "1.0", deps: [new ModDependencyReference { ProjectId = id == "a" ? "b" : "a", Source = "modrinth" }])],
            },
        });

        Assert.Equal(2, result.ToInstall.Count); // a 和 b 各一次，无死循环
    }

    // ---------- 8-19 补 2：年份号（26.2）/空 target 依赖解析放宽（精确匹配只在传统 1.x 生效） ----------

    [Fact]
    public void Resolve_YearFormatTarget_SelectsNewestWithoutExactMatch()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "26.2",
            TargetLoaders = ["fabric"],
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            // 文件只标传统版本（1.20.1/1.21.1）——26.2 永不精确匹配 → 放宽按 loader+最新选
            ProjectResolver = (_, id) => new ModDependencyProject
            {
                ProjectId = id, Source = "modrinth",
                Files = [MakeFile("old", "1.0", gameVersions: ["1.20.1"], loaders: ["fabric"]),
                         MakeFile("new", "2.0", gameVersions: ["1.21.1"], loaders: ["fabric"])],
            },
        });

        Assert.Single(result.ToInstall);
        Assert.Equal("new", result.ToInstall[0].File.Id);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Resolve_YearFormatTarget_LoaderStillRespected()
    {
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "26.2",
            TargetLoaders = ["forge"],   // 26.2 放宽版本但 loader 过滤仍生效
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            ProjectResolver = (_, id) => new ModDependencyProject
            {
                ProjectId = id, Source = "modrinth",
                Files = [MakeFile("v1", "1.0", gameVersions: ["1.21.1"], loaders: ["fabric"])],
            },
        });

        Assert.Empty(result.ToInstall);
        Assert.Single(result.Unresolved);
        Assert.Contains("No compatible file", result.Unresolved[0].Reason);
    }

    [Fact]
    public void Resolve_EmptyTargetVersion_SelectsNewest()
    {
        // 无实例（整合包）场景 target=""——既有 bug：空串也永不匹配 → 依赖全失败；放宽后按 loader+最新选
        var resolver = new ModDependencyResolver();
        var result = resolver.Resolve(new ModDependencyRequest
        {
            TargetMinecraftVersion = "",
            TargetLoaders = ["fabric"],
            RequiredDependencies =
            [
                new ModDependencyReference { ProjectId = "main", Source = "modrinth" },
            ],
            ProjectResolver = (_, id) => new ModDependencyProject
            {
                ProjectId = id, Source = "modrinth",
                Files = [MakeFile("old", "1.0", gameVersions: ["1.20.1"], loaders: ["fabric"]),
                         MakeFile("new", "2.0", gameVersions: ["1.21.1"], loaders: ["fabric"])],
            },
        });

        Assert.Single(result.ToInstall);
        Assert.Equal("new", result.ToInstall[0].File.Id);
        Assert.Empty(result.Unresolved);
    }
}
