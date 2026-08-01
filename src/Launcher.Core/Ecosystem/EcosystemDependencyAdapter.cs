using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.Core.Ecosystem;

/// <summary>
/// ModDependencyResolver 与 Modrinth API 的适配：ProjectResolver 从 Modrinth 拉项目版本并映射为依赖模型。
/// </summary>
public static class EcosystemDependencyAdapter
{
    /// <summary>创建 ProjectResolver（同步签名，内部同步等待——依赖数量少，可接受）</summary>
    public static Func<string, string, ModDependencyProject?> CreateResolver(
        EcosystemService eco, string? gameVersion, string? loader)
    {
        return (source, projectId) =>
        {
            try
            {
                var versions = eco.GetVersionsAsync(projectId, gameVersion, loader)
                    .GetAwaiter().GetResult();
                if (versions.Count == 0) return null;
                return new ModDependencyProject
                {
                    ProjectId = projectId,
                    Source = source,
                    Files = versions.Select(ToFile).ToList(),
                };
            }
            catch
            {
                return null;
            }
        };
    }

    private static ModDependencyFile ToFile(ModrinthVersion v) => new()
    {
        Id = v.Id,
        DisplayName = v.Name,
        Version = v.VersionNumber,
        GameVersions = v.GameVersions ?? [],
        Loaders = v.Loaders ?? [],
        ReleaseType = v.VersionType switch
        {
            "release" => 1,
            "beta" => 2,
            _ => 3,
        },
        ReleaseDate = v.DatePublished,
        RequiredDependencies = (v.Dependencies ?? [])
            .Where(d => d.DependencyType == "required" && d.ProjectId is not null)
            .Select(d => new ModDependencyReference
            {
                ProjectId = d.ProjectId!,
                Source = "modrinth",
                IsRequired = true,
            })
            .ToList(),
    };

    /// <summary>把 Modrinth 版本的依赖提取为请求输入</summary>
    public static List<ModDependencyReference> ToDependencyReferences(ModrinthVersion version) =>
        (version.Dependencies ?? [])
            .Where(d => d.DependencyType == "required" && d.ProjectId is not null)
            .Select(d => new ModDependencyReference
            {
                ProjectId = d.ProjectId!,
                Source = "modrinth",
                IsRequired = true,
            })
            .ToList();
}
