using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Launcher.Core.Download;
using Launcher.Core.Ecosystem;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Utils;

namespace Launcher.Core.Services;

/// <summary>
/// 生态下载服务：Modrinth 搜索 / 详情 / 版本匹配 / 安装到实例目录。
/// 注意：Modrinth API 强制要求 User-Agent 头，缺失返回 403。
/// </summary>
public sealed class EcosystemService
{
    private const string ApiBase = "https://api.modrinth.com/v2";

    private readonly HttpClient _http;
    private readonly DownloadService _downloads;
    private readonly string _gameDirectory;

    public EcosystemService(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/0.1");
        _downloads = downloads ?? new DownloadService();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
    }

    /// <summary>搜索（facets 按 类型|游戏版本|加载器|功能分类 过滤，offset 分页）</summary>
    /// <summary>排序方式（Modrinth search index 参数）</summary>
    public enum SortIndex { Relevance, Downloads, Follows, Newest, Updated }

    public async Task<ModrinthSearchResponse?> SearchAsync(
        ProjectType type, string? query = null, string? gameVersion = null,
        string? loader = null, string? category = null,
        SortIndex index = SortIndex.Relevance,
        int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        var facets = BuildFacets(type, gameVersion, loader, category);
        var indexName = index switch
        {
            SortIndex.Downloads => "downloads",
            SortIndex.Follows => "follows",
            SortIndex.Newest => "newest",
            SortIndex.Updated => "updated",
            _ => "relevance",
        };
        var url = $"{ApiBase}/search?query={Uri.EscapeDataString(query ?? "")}"
                  + $"&facets={Uri.EscapeDataString(facets)}&index={indexName}&limit={limit}&offset={offset}";
        return await GetJsonAsync<ModrinthSearchResponse>(url, ct);
    }

    /// <summary>项目详情</summary>
    public Task<ModrinthProjectDetail?> GetProjectAsync(string projectIdOrSlug, CancellationToken ct = default)
        => GetJsonAsync<ModrinthProjectDetail>($"{ApiBase}/project/{projectIdOrSlug}", ct);

    /// <summary>匹配最新可用版本（按游戏版本+加载器过滤后取最新）</summary>
    public async Task<ModrinthVersion?> FindBestVersionAsync(
        string projectId, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(projectId, gameVersion, loader, ct);
        return SelectBestVersion(versions);
    }

    /// <summary>版本列表（手动选择用，懒加载）</summary>
    public async Task<List<ModrinthVersion>> GetVersionsAsync(
        string projectId, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (gameVersion is not null)
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}");
        if (loader is not null)
            query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");
        var url = $"{ApiBase}/project/{projectId}/version"
                  + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var list = await GetJsonAsync<List<ModrinthVersion>>(url, ct);
        return list ?? [];
    }

    /// <summary>
    /// 安装：下载主文件到实例目录（mods/resourcepacks/shaderpacks），整合包到 downloads/modpacks。
    /// 幂等：文件已存在且 SHA1 匹配时直接跳过。
    /// </summary>
    public async Task<string> InstallAsync(
        string projectId, ModrinthVersion version, string instanceId, ProjectType type,
        DownloadProgressHandler? progress = null, CancellationToken ct = default, string? gameDirOverride = null)
    {
        var file = PickPrimaryFile(version.Files)
            ?? throw new InvalidOperationException("该版本没有可下载文件");
        // gameDirOverride：版本来源目录（PCL/自建）——MOD 必须装进版本真实目录（AF2）
        var targetDir = ResolveInstallPath(gameDirOverride ?? _gameDirectory, instanceId, type);
        // 目标目录兜底创建（自定义实例名时 versions/{name}/mods 可能不存在——否则下载失败/落错位）
        Directory.CreateDirectory(targetDir);
        var destPath = Path.Combine(targetDir, Path.GetFileName(file.FileName));
        await _downloads.DownloadFileAsync(file.Url, destPath, file.Hashes?.Sha1, file.Size, progress, ct);
        return destPath;
    }

    /// <summary>
    /// 解析依赖树并返回前置项目显示名（安装前提示用："将安装 N 个前置：A、B"）。
    /// 最多查 5 个标题（防滥用）；查询失败回退 ProjectId。
    /// </summary>
    public async Task<List<string>> ResolveDependencyNamesAsync(
        ModrinthVersion version, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var resolver = new ModDependencyResolver();
        var request = new ModDependencyRequest
        {
            TargetMinecraftVersion = gameVersion ?? "",
            TargetLoaders = loader is null ? [] : [loader],
            RequiredDependencies = EcosystemDependencyAdapter.ToDependencyReferences(version),
            ProjectResolver = EcosystemDependencyAdapter.CreateResolver(this, gameVersion, loader),
        };
        var result = resolver.Resolve(request);

        // 依赖显示名：项目标题 + 一句话说明（用户能看懂装的是什么——如 AANobbMI 是 Iris 的渲染 API 库）
        var names = new List<string>();
        foreach (var dep in result.ToInstall.Take(5))
        {
            try
            {
                var detail = await GetProjectAsync(dep.ProjectId, ct);
                if (detail is null) { names.Add(dep.ProjectId); continue; }
                var hint = detail.Description;
                if (hint is { Length: > 28 }) hint = hint[..28] + "…";
                names.Add(string.IsNullOrEmpty(hint) ? detail.Title : $"{detail.Title}——{hint}");
            }
            catch { names.Add(dep.ProjectId); }
        }
        return names;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// 安装主文件 + 解析并递归安装全部必需依赖（PCL2 式一键安装体验）。
    /// </summary>
    public async Task<DependencyInstallReport> InstallWithDependenciesAsync(
        string projectId, ModrinthVersion version, string instanceId, ProjectType type,
        string? gameVersion, string? loader,
        DownloadProgressHandler? progress = null, CancellationToken ct = default, string? gameDirOverride = null)
    {
        var report = new DependencyInstallReport();

        // 1. 主文件
        try
        {
            var mainPath = await InstallAsync(projectId, version, instanceId, type, progress, ct, gameDirOverride);
            report.Installed.Add(new InstalledDependency(projectId, version.Id, mainPath));
        }
        catch (Exception ex)
        {
            report.Failed.Add(new FailedDependency(projectId, ex.Message));
            return report;
        }

        // 2. 解析依赖树
        var resolver = new ModDependencyResolver();
        var request = new ModDependencyRequest
        {
            TargetMinecraftVersion = gameVersion ?? "",
            TargetLoaders = loader is null ? [] : [loader],
            RequiredDependencies = EcosystemDependencyAdapter.ToDependencyReferences(version),
            ProjectResolver = EcosystemDependencyAdapter.CreateResolver(this, gameVersion, loader),
        };
        var result = resolver.Resolve(request);

        // 3. 逐个安装依赖（依赖均为 MOD 类型，装到实例 mods 目录）
        foreach (var dep in result.ToInstall)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var versions = await GetVersionsAsync(dep.ProjectId, gameVersion, loader, ct);
                var depVersion = versions.FirstOrDefault(v => v.Id == dep.File.Id);
                if (depVersion is null)
                {
                    report.Failed.Add(new FailedDependency(dep.ProjectId, "依赖版本已不存在"));
                    continue;
                }
                var path = await InstallAsync(dep.ProjectId, depVersion, instanceId, ProjectType.Mod, progress, ct, gameDirOverride);
                report.Installed.Add(new InstalledDependency(dep.ProjectId, depVersion.Id, path));
            }
            catch (Exception ex)
            {
                report.Failed.Add(new FailedDependency(dep.ProjectId, ex.Message));
            }
        }

        // 4. 未解析依赖
        foreach (var un in result.Unresolved)
            report.Failed.Add(new FailedDependency(un.ProjectId, un.Reason));

        return report;
    }

    // ---------- 静态工具（离线可单测） ----------

    /// <summary>构建 facets JSON，如 [["project_type:mod"],["versions:1.21.1"],["categories:fabric"],["categories:optimization"]]。
    /// 加载器与功能分类同用 categories 键（Modrinth 同键多值取 OR）；facets 值强制小写（API 要求）。</summary>
    public static string BuildFacets(ProjectType type, string? gameVersion, string? loader, string? category = null)
    {
        var outer = new List<string[]> { new[] { $"project_type:{FacetName(type)}" } };
        if (gameVersion is not null) outer.Add(new[] { $"versions:{gameVersion}" });
        if (loader is not null) outer.Add(new[] { $"categories:{loader.ToLowerInvariant()}" });
        if (category is not null) outer.Add(new[] { $"categories:{category.ToLowerInvariant()}" });
        return JsonSerializer.Serialize(outer);
    }

    public static string FacetName(ProjectType type) => type switch
    {
        ProjectType.Mod => "mod",
        ProjectType.Modpack => "modpack",
        ProjectType.Resourcepack => "resourcepack",
        ProjectType.Shader => "shader",
        _ => "mod",
    };

    /// <summary>安装子目录；整合包返回 null（走 downloads/modpacks）</summary>
    public static string? ResolveSubDir(ProjectType type) => type switch
    {
        ProjectType.Mod => "mods",
        ProjectType.Resourcepack => "resourcepacks",
        ProjectType.Shader => "shaderpacks",
        _ => null,
    };

    public static string ResolveInstallPath(string gameDirectory, string instanceId, ProjectType type)
    {
        if (type == ProjectType.Modpack)
            return Path.Combine(gameDirectory, "downloads", "modpacks");
        var sub = ResolveSubDir(type)!;
        // 版本隔离判定：版本目录已存在 → 装版本目录（隔离/PCL 实例）；否则装共享目录（非隔离共享 mods）
        var versionDir = Path.Combine(gameDirectory, "versions", instanceId);
        return Directory.Exists(versionDir)
            ? Path.Combine(versionDir, sub)
            : Path.Combine(gameDirectory, sub);
    }

    /// <summary>从实例名解析游戏版本：1.21.1 → true/"1.21.1"；1.21.1-Fabric → true；自定义名 → false</summary>
    public static bool TryParseGameVersion(string instanceId, out string version)
    {
        var m = Regex.Match(instanceId, @"^\d+\.\d+(\.\d+)?");
        if (m.Success) { version = m.Value; return true; }
        version = "";
        return false;
    }

    /// <summary>从实例名猜测加载器（fabric/forge/neoforge/quilt/iris/optifine），未知返回 null</summary>
    public static string? GuessLoader(string instanceId)
    {
        var lower = instanceId.ToLowerInvariant();
        foreach (var (keyword, loader) in new[]
                 {
                     ("fabric", "fabric"), ("neoforge", "neoforge"), ("forge", "forge"),
                     ("quilt", "quilt"), ("iris", "iris"), ("optifine", "optifine"),
                 })
        {
            if (lower.Contains(keyword)) return loader;
        }
        return null;
    }

    /// <summary>选最新版本：过滤无文件项，featured 优先，其次 date_published 降序</summary>
    public static ModrinthVersion? SelectBestVersion(IEnumerable<ModrinthVersion> versions)
        => versions.Where(v => v.Files is { Count: > 0 })
                   .OrderByDescending(v => v.Featured ?? false)
                   .ThenByDescending(v => v.DatePublished)
                   .FirstOrDefault();

    /// <summary>选主文件：Primary 优先，否则第一个</summary>
    public static ModrinthVersionFile? PickPrimaryFile(List<ModrinthVersionFile>? files)
    {
        if (files is null || files.Count == 0) return null;
        return files.FirstOrDefault(f => f.Primary) ?? files[0];
    }
}
