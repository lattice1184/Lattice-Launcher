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
    private readonly McmodSearchService _mcmod;
    private readonly string _cacheDir;

    public EcosystemService(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null,
        McmodSearchService? mcmod = null, string? cacheDir = null)
    {
        _http = http ?? HttpClientPool.Create();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/0.1");
        _downloads = downloads ?? new DownloadService();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
        _mcmod = mcmod ?? new McmodSearchService();
        // 8-16 批次 53：缓存目录可注入（测试隔离——磁盘缓存跨测试共享会污染请求计数断言）
        _cacheDir = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "cache");
    }

    /// <summary>
    /// 中文搜索（AL63）：MC百科汉化链路——中文 → mcmod 条目 → 解 Modrinth slug → 项目详情 → 搜索结果。
    /// 无分页（mcmod 搜索不分页；结果上限 10）。中文查询走此路，英文查询走 SearchAsync。
    /// </summary>
    public async Task<ModrinthSearchResponse?> SearchChineseAsync(
        ProjectType type, string query, CancellationToken ct = default)
    {
        var slugs = await _mcmod.SearchSlugsAsync(query, maxResults: 10, ct);
        if (slugs.Count == 0) return new ModrinthSearchResponse([], 0, 0, 10);
        var hits = new List<ModrinthSearchHit>();
        var typeName = type.ToString().ToLowerInvariant();
        foreach (var (slug, _) in slugs)
        {
            try
            {
                var detail = await GetProjectAsync(slug, ct);
                if (detail is null || !detail.ProjectType.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                hits.Add(new ModrinthSearchHit(detail.Id, detail.ProjectType, detail.Slug,
                    "", detail.Title, detail.Description, detail.Categories, null, detail.Versions,
                    detail.IconUrl, detail.Downloads, detail.Follows, detail.DateCreated, detail.DateModified,
                    null));
            }
            catch { /* 单条失败跳过——链路本身有兜底 */ }
        }
        return new ModrinthSearchResponse(hits, hits.Count, 0, 10);
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
        return await GetJsonAsyncCached<ModrinthSearchResponse>(url, ct);
    }

    /// <summary>搜索响应磁盘缓存（5 分钟 TTL）：切页/重复搜索不重复打 API——模组页首屏慢的元凶之一</summary>
    private async Task<T?> GetJsonAsyncCached<T>(string url, CancellationToken ct) where T : class
    {
        var key = "eco-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..16];
        var cachePath = Path.Combine(_cacheDir, key + ".json");
        try
        {
            if (File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < TimeSpan.FromMinutes(5))
                return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(cachePath, ct));
        }
        catch { /* 缓存损坏忽略 */ }
        var result = await GetJsonAsync<T>(url, ct);
        if (result is not null)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(result), ct);
            }
            catch { /* 缓存写入失败不影响结果 */ }
        }
        return result;
    }

    /// <summary>项目详情（8-16 批次 53：走 5 分钟磁盘缓存——依赖名前查询/详情页重复打开不再重复打 API）</summary>
    public Task<ModrinthProjectDetail?> GetProjectAsync(string projectIdOrSlug, CancellationToken ct = default)
        => GetJsonAsyncCached<ModrinthProjectDetail>($"{ApiBase}/project/{projectIdOrSlug}", ct);

    /// <summary>匹配最新可用版本（按游戏版本+加载器过滤后取最新）</summary>
    public async Task<ModrinthVersion?> FindBestVersionAsync(
        string projectId, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(projectId, gameVersion, loader, ct);
        return SelectBestVersion(versions);
    }

    /// <summary>版本列表（手动选择用，懒加载）。8-19：年份号（26.2）Modrinth versions API 不认（search facet 认、versions 参数不认）
    /// → 空结果自动去 gameVersion 重查一次（保留 loader；传统 1.x 空结果不降级——真实语义）</summary>
    public async Task<List<ModrinthVersion>> GetVersionsAsync(
        string projectId, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
    {
        var list = await GetVersionsCoreAsync(projectId, gameVersion, loader, ct);
        if (list.Count == 0 && IsYearFormatVersion(gameVersion))
            list = await GetVersionsCoreAsync(projectId, null, loader, ct);
        return list;
    }

    private async Task<List<ModrinthVersion>> GetVersionsCoreAsync(
        string projectId, string? gameVersion, string? loader, CancellationToken ct)
    {
        var query = new List<string>();
        if (gameVersion is not null)
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}");
        if (loader is not null)
            query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");
        var url = $"{ApiBase}/project/{projectId}/version"
                  + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        // 8-16 批次 53：版本列表走 5 分钟缓存——安装流程主文件/依赖/手动选择会重复查同一版本列表
        // （api.modrinth.com 国内直连实测 8.6s/次，缓存后重复查询秒回）
        var list = await GetJsonAsyncCached<List<ModrinthVersion>>(url, ct);
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

        // 依赖显示名：项目标题 + 一句话说明（用户能看懂装的是什么——如 AANobbMI 是 Iris 的渲染 API 库）。
        // 8-16 批次 53：串行 → 并行（门 4）——api.modrinth.com 国内 8.6s/次，串行 5 个依赖 = 43s 干等
        var names = new List<string>(result.ToInstall.Count);
        var lockObj = new object();
        using var gate = new SemaphoreSlim(4);
        var tasks = new List<Task>();
        foreach (var dep in result.ToInstall.Take(5))
        {
            tasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var detail = await GetProjectAsync(dep.ProjectId, ct);
                    string label;
                    if (detail is null) { label = dep.ProjectId; }
                    else
                    {
                        var hint = detail.Description;
                        if (hint is { Length: > 28 }) hint = hint[..28] + "…";
                        label = string.IsNullOrEmpty(hint) ? detail.Title : $"{detail.Title}——{hint}";
                    }
                    lock (lockObj) names.Add(label);
                }
                catch { lock (lockObj) names.Add(dep.ProjectId); }
                finally { gate.Release(); }
            }, ct));
        }
        await Task.WhenAll(tasks);
        return names;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// 安装主文件 + 解析并递归安装全部必需依赖（PCL2 式一键安装体验）。
    /// ctx 非空时主文件与每个依赖各成一个组子任务（下载中心可见、可暂停/重试）；
    /// 依赖并行安装（门 4，与 CF 侧一致）。
    /// </summary>
    public async Task<DependencyInstallReport> InstallWithDependenciesAsync(
        string projectId, ModrinthVersion version, string instanceId, ProjectType type,
        string? gameVersion, string? loader,
        DownloadProgressHandler? progress = null, CancellationToken ct = default, string? gameDirOverride = null,
        DownloadGroupContext? ctx = null)
    {
        var report = new DependencyInstallReport();

        // 1. 主文件
        try
        {
            var mainPath = await InstallOneAsync(ctx, $"主文件 {version.Name}", 0,
                (p, c) => InstallAsync(projectId, version, instanceId, type, p, c, gameDirOverride), ct);
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

        // 3. 依赖并行安装（依赖均为 MOD 类型，装到实例 mods 目录；结果收集加锁——多线程写 report）
        using var gate = new SemaphoreSlim(4);
        var depTasks = new List<Task>();
        foreach (var dep in result.ToInstall)
        {
            if (ct.IsCancellationRequested) break;
            depTasks.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var versions = await GetVersionsAsync(dep.ProjectId, gameVersion, loader, ct);
                    var depVersion = versions.FirstOrDefault(v => v.Id == dep.File.Id);
                    if (depVersion is null)
                    {
                        lock (report) report.Failed.Add(new FailedDependency(dep.ProjectId, "依赖版本已不存在"));
                        return;
                    }
                    var path = await InstallOneAsync(ctx, $"依赖 {depVersion.Name}", 0,
                        (p, c) => InstallAsync(dep.ProjectId, depVersion, instanceId, ProjectType.Mod, p, c, gameDirOverride), ct);
                    lock (report) report.Installed.Add(new InstalledDependency(dep.ProjectId, depVersion.Id, path));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* 组取消：其余任务一并终止 */ }
                catch (Exception ex)
                {
                    lock (report) report.Failed.Add(new FailedDependency(dep.ProjectId, ex.Message));
                }
                finally { gate.Release(); }
            }, ct));
        }
        await Task.WhenAll(depTasks);

        // 4. 未解析依赖
        foreach (var un in result.Unresolved)
            report.Failed.Add(new FailedDependency(un.ProjectId, un.Reason));

        return report;
    }

    /// <summary>安装单文件：有组上下文 → 子任务（下载中心可见）；否则直接装（测试/叶子调用兼容）</summary>
    private async Task<string> InstallOneAsync(DownloadGroupContext? ctx, string name, long weight,
        Func<DownloadProgressHandler, CancellationToken, Task<string>> work, CancellationToken ct)
    {
        if (ctx is null) return await work(null!, ct);
        string? path = null;
        var child = ctx.AddChild(name, weight, async (p, c) => { path = await work(p, c); });
        await child.Completion.WaitAsync(ct);
        return path ?? throw new InvalidOperationException($"{name} 未产生文件");
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

    /// <summary>安装子目录；整合包返回 null（走 downloads/modpacks）。
    /// 8-16 批次 54：数据包 → datapacks（1.13+ 全局数据包目录，所有世界生效）</summary>
    public static string? ResolveSubDir(ProjectType type) => type switch
    {
        ProjectType.Mod => "mods",
        ProjectType.Resourcepack => "resourcepacks",
        ProjectType.Shader => "shaderpacks",
        ProjectType.Datapack => "datapacks",
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

    /// <summary>8-19：PCL 年份号版本（26.2/26.10/99.1——`^\d{2}\.\d+`，非 1.x 传统格式）。
    /// 年份号在 CF/Modrinth 文件版本（1.21.6 格式）中永不匹配——空结果必为假阴性 → 允许降级/放宽；
    /// 传统 1.x 的空结果是真实语义，绝不降级（否则 1.21.6 实例会高亮 1.20.1 版本装崩）。</summary>
    public static bool IsYearFormatVersion(string? version)
        => !string.IsNullOrEmpty(version) && Regex.IsMatch(version, @"^\d{2}\.\d+");

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

    /// <summary>
    /// 游戏版本语义比较：点分数字逐段比（26.2 &gt; 1.21.6、1.21.10 &gt; 1.21.6——字符串序会判反）；
    /// 非数字段回落序号比较。2026 起版本号用 YY.M 新格式，1.x 与 26.x 混排必须走语义序。
    /// </summary>
    public static int CompareGameVersions(string? x, string? y)
    {
        var xp = (x ?? "").Split('.');
        var yp = (y ?? "").Split('.');
        for (var i = 0; i < Math.Min(xp.Length, yp.Length); i++)
        {
            if (int.TryParse(xp[i], out var xn) && int.TryParse(yp[i], out var yn))
            {
                if (xn != yn) return xn.CompareTo(yn);
            }
            else
            {
                var c = string.Compare(xp[i], yp[i], StringComparison.Ordinal);
                if (c != 0) return c;
            }
        }
        return xp.Length.CompareTo(yp.Length);
    }

    /// <summary>
    /// 选最新版本：过滤无文件项；release &gt; beta &gt; alpha &gt; null 优先（快照/预发布不抢正式版——
    /// 8-13 真机：26.2 的 beta 日期最新被选中，用户装正式版却匹配到快照），同级 featured 优先，
    /// 其次 date_published 降序。null 排最后与依赖解析器 NormalizeReleaseType 一致。
    /// </summary>
    public static ModrinthVersion? SelectBestVersion(IEnumerable<ModrinthVersion> versions)
        => versions.Where(v => v.Files is { Count: > 0 })
                   .OrderBy(v => ReleaseRank(v.VersionType))
                   .ThenByDescending(v => v.Featured ?? false)
                   .ThenByDescending(v => v.DatePublished)
                   .FirstOrDefault();

    /// <summary>Modrinth version_type 排名（release=0 beta=1 alpha=2 null=3——未知信任度最低）</summary>
    public static int ReleaseRank(string? type) => type switch
    {
        "release" => 0,
        "beta" => 1,
        "alpha" => 2,
        _ => 3,
    };

    /// <summary>选主文件：Primary 优先，否则第一个</summary>
    public static ModrinthVersionFile? PickPrimaryFile(List<ModrinthVersionFile>? files)
    {
        if (files is null || files.Count == 0) return null;
        return files.FirstOrDefault(f => f.Primary) ?? files[0];
    }

    // ---------- 中文搜索重排（A 修复：Modrinth relevance 对中文把「描述子串匹配」当强相关，
    // 「字幕高亮」因描述含「自定义」排第一 → 客户端按匹配质量稳定重排） ----------

    /// <summary>Query 含 CJK（中文搜索）→ 源 relevance 不可靠，需重排；纯英文信任源排序</summary>
    public static bool IsChineseQuery(string? q)
        => !string.IsNullOrEmpty(q) && q.Any(c => c is >= '一' and <= '鿿');

    /// <summary>匹配分：标题包含 query=3（强相关），描述/摘要包含=2（弱相关），无=0</summary>
    public static int MatchScore(string title, string description, string query)
    {
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        if (description.Contains(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return 0;
    }

    /// <summary>按匹配分降序稳定重排（同分保持源顺序）；非中文 query 调用方不应调用</summary>
    public static List<T> ReorderMatches<T>(IEnumerable<T> items, string? query,
        Func<T, string> titleOf, Func<T, string> descriptionOf)
        => [.. items.OrderByDescending(x => MatchScore(titleOf(x), descriptionOf(x), query ?? ""))];
}
