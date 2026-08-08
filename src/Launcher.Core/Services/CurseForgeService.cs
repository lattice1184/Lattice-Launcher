using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Ecosystem;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Utils;
using PCL.Core.Minecraft.ResourceProject.Curseforge;

namespace Launcher.Core.Services;

/// <summary>
/// 生态下载服务（CurseForge 源）：搜索 / 详情 / 文件匹配 / 安装到实例目录。
/// 依赖 x-api-key（设置页 CurseForgeApiKey 或环境变量 CURSEFORGE_API_KEY）；未配置时 IsEnabled=false（搜索返回空）。
/// 限流参考：官方 key 约 50 请求/30 秒，勿做深分页。
/// </summary>
public sealed class CurseForgeService
{
    private const string ApiBase = "https://api.curseforge.com/v1";
    private const int GameId = 432; // Minecraft

    private readonly HttpClient _http;
    private readonly DownloadService _downloads;
    private readonly string _gameDirectory;
    private readonly string? _apiKeyOverride;

    /// <summary>是否启用（动态：每次读设置/环境变量——设置页改 key 即时生效，无需重启）</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(EffectiveKey());

    /// <summary>当前生效 key：构造注入优先（null = 动态读设置/环境变量；空字符串 = 显式禁用），否则每次读设置（不再构造时缓存）</summary>
    private string? EffectiveKey() =>
        _apiKeyOverride is not null ? _apiKeyOverride : ResolveApiKey();

    public CurseForgeService(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null)
        : this(null, http, downloads, gameDirectory) // null = 动态读设置/环境变量
    {
    }

    /// <summary>测试注入用：显式 key（null = 动态读设置；空字符串 = 禁用）</summary>
    public CurseForgeService(string? apiKey, HttpClient? http = null, DownloadService? downloads = null,
        string? gameDirectory = null)
    {
        _apiKeyOverride = apiKey;
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/0.1");
        _downloads = downloads ?? new DownloadService();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
    }

    /// <summary>Key 解析：设置页优先，回退环境变量；空 = 禁用</summary>
    public static string? ResolveApiKey(LauncherSettings? s = null)
    {
        var fromSettings = (s ?? LauncherSettings.Current).CurseForgeApiKey;
        if (!string.IsNullOrWhiteSpace(fromSettings)) return fromSettings.Trim();
        return Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
    }

    /// <summary>排序方式（CF sortField 为 1 基：1=Featured 2=Popularity 3=LastUpdated 6=TotalDownloads 11=ReleasedDate）</summary>
    public enum SortIndex { Relevance, Downloads, Newest, Updated }

    public static int SortFieldFor(SortIndex index) => index switch
    {
        SortIndex.Downloads => 6,   // TotalDownloads
        SortIndex.Newest => 11,     // ReleasedDate
        SortIndex.Updated => 3,     // LastUpdated
        _ => 1,                     // Featured ≈ 相关度
    };

    /// <summary>类型 → CF classId（mod=6 / modpack=4471 / resourcepack=12 / shader=6552）</summary>
    public static int ClassIdFor(ProjectType type) => type switch
    {
        ProjectType.Modpack => 4471,
        ProjectType.Resourcepack => 12,
        ProjectType.Shader => 6552,
        _ => 6,
    };

    /// <summary>搜索（classId 按类型过滤；gameVersion 字符串精确匹配；index 分页）</summary>
    public async Task<CurseForgeSearchPage?> SearchAsync(
        ProjectType type, string? query = null, string? gameVersion = null,
        SortIndex sort = SortIndex.Relevance,
        int limit = 20, int index = 0, CancellationToken ct = default)
    {
        if (!IsEnabled) return null;
        var url = BuildSearchUrl(type, query, gameVersion, sort, limit, index);
        var response = await GetJsonAsync<CurseforgeSearchResponse>(url, ct);
        if (response is null) return null;
        return new CurseForgeSearchPage(response.data ?? [], response.pagination?.totalCount ?? response.data?.Count ?? 0);
    }

    /// <summary>
    /// 验证当前生效 key：调一次最小 search 请求。401/403 = 无效；其他状态码/网络错误 = 无法验证。
    /// 结果只含状态与 HTTP 码，**绝不包含 key 内容**（设置页填入后失焦即调，用于即时反馈）。
    /// </summary>
    public async Task<(bool Valid, string Message)> ValidateKeyAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return (false, "未配置 Key");
        try
        {
            var url = BuildSearchUrl(ProjectType.Mod, null, null, SortIndex.Relevance, 1, 0);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-api-key", EffectiveKey());
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return (true, "Key 有效");
            var code = (int)resp.StatusCode;
            return (false, code is 401 or 403 ? $"Key 无效（HTTP {code}）" : $"验证失败（HTTP {code}）");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "无法连接 CurseForge API，稍后再试");
        }
    }

    /// <summary>项目详情（含 logo / authors / 下载数）</summary>
    public async Task<CurseforgeProject?> GetProjectAsync(int modId, CancellationToken ct = default)
    {
        if (!IsEnabled) return null;
        var response = await GetJsonAsync<CurseforgeProjectResponse>($"{ApiBase}/mods/{modId}", ct);
        return response?.data;
    }

    /// <summary>文件列表（安装版本选择用，懒加载）</summary>
    public async Task<List<CurseforgeFile>> GetFilesAsync(int modId, string? gameVersion = null, CancellationToken ct = default)
    {
        if (!IsEnabled) return [];
        var url = $"{ApiBase}/mods/{modId}/files?pageSize=50"
                  + (gameVersion is null ? "" : $"&gameVersion={Uri.EscapeDataString(gameVersion)}");
        var response = await GetJsonAsync<CurseforgeFilesResponse>(url, ct);
        return response?.data ?? [];
    }

    /// <summary>匹配最佳文件：可用 + 版本兼容优先，releaseType=1（Release）优先，fileId 降序（近似"最新"）</summary>
    public async Task<CurseforgeFile?> FindBestFileAsync(int modId, string? gameVersion = null, CancellationToken ct = default)
    {
        var files = await GetFilesAsync(modId, gameVersion, ct);
        return SelectBestFile(files, gameVersion);
    }

    /// <summary>安装：下载文件到实例目录（mods/resourcepacks/shaderpacks），整合包到 downloads/modpacks。SHA1 幂等。</summary>
    public async Task<string> InstallAsync(
        int projectId, CurseforgeFile file, string instanceId, ProjectType type,
        DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(file.downloadUrl))
            throw new InvalidOperationException("该文件没有下载地址");
        var targetDir = EcosystemService.ResolveInstallPath(_gameDirectory, instanceId, type);
        var destPath = Path.Combine(targetDir, Path.GetFileName(file.fileName));
        var sha1 = file.hashes?.algo == 1 ? file.hashes.value : null; // CF algo: 1=SHA1 2=MD5
        await _downloads.DownloadFileAsync(ApplyCdnPrefix(file.downloadUrl), destPath, sha1, file.fileLength, progress, ct);
        return destPath;
    }

    /// <summary>CF 文件 CDN 加速：设置里可配置镜像/代理前缀替换官方 edge.forgecdn.net（国内直连慢）。
    /// 每次读设置（改前缀即时生效）；前缀为空或 URL 非官方域名时原样返回。</summary>
    private static string ApplyCdnPrefix(string url)
    {
        const string official = "https://edge.forgecdn.net/";
        var prefix = LauncherSettings.Current.CurseForgeCdnPrefix?.Trim();
        if (string.IsNullOrEmpty(prefix) || !url.StartsWith(official)) return url;
        return prefix.TrimEnd('/') + "/" + url[official.Length..];
    }

    /// <summary>
    /// 安装主文件 + 解析并递归安装全部必需依赖（PCL2 式一键安装体验）。
    /// 依赖按解析器选定的文件安装；取不到时回退最佳文件。
    /// </summary>
    public async Task<DependencyInstallReport> InstallWithDependenciesAsync(
        int projectId, CurseforgeFile file, string instanceId, ProjectType type,
        string? gameVersion,
        DownloadProgressHandler? progress = null, CancellationToken ct = default)
    {
        var report = new DependencyInstallReport();
        var projectIdText = projectId.ToString();

        // 1. 主文件
        try
        {
            var mainPath = await InstallAsync(projectId, file, instanceId, type, progress, ct);
            report.Installed.Add(new InstalledDependency(projectIdText, file.id.ToString(), mainPath));
        }
        catch (Exception ex)
        {
            report.Failed.Add(new FailedDependency(projectIdText, ex.Message));
            return report;
        }

        // 2. 解析依赖树
        var resolver = new ModDependencyResolver();
        var request = new ModDependencyRequest
        {
            TargetMinecraftVersion = gameVersion ?? "",
            RequiredDependencies = EcosystemDependencyAdapter.ToDependencyReferences(file),
            ProjectResolver = EcosystemDependencyAdapter.CreateResolver(this, gameVersion),
        };
        var result = resolver.Resolve(request);

        // 3. 逐个安装依赖（依赖均为 MOD 类型，装到实例 mods 目录）
        foreach (var dep in result.ToInstall)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!int.TryParse(dep.ProjectId, out var depModId))
                {
                    report.Failed.Add(new FailedDependency(dep.ProjectId, "无效项目 ID"));
                    continue;
                }
                var files = await GetFilesAsync(depModId, gameVersion, ct);
                var depFile = files.FirstOrDefault(f => f.id.ToString() == dep.File.Id)
                              ?? SelectBestFile(files, gameVersion);
                if (depFile is null)
                {
                    report.Failed.Add(new FailedDependency(dep.ProjectId, "未找到兼容文件"));
                    continue;
                }
                var path = await InstallAsync(depModId, depFile, instanceId, ProjectType.Mod, progress, ct);
                report.Installed.Add(new InstalledDependency(dep.ProjectId, depFile.id.ToString(), path));
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

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var key = EffectiveKey(); // 每次请求读最新 key——改 key 即时生效
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.Add("x-api-key", key);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json);
    }

    // ---------- 静态工具（离线可单测） ----------

    public static string BuildSearchUrl(ProjectType type, string? query, string? gameVersion, SortIndex sort, int limit, int index)
    {
        var url = $"{ApiBase}/mods/search?gameId={GameId}&classId={ClassIdFor(type)}";
        if (!string.IsNullOrEmpty(query))
            url += $"&searchFilter={Uri.EscapeDataString(query)}";
        if (gameVersion is not null)
            url += $"&gameVersion={Uri.EscapeDataString(gameVersion)}";
        url += $"&sortField={SortFieldFor(sort)}&sortOrder=desc&index={index}&pageSize={limit}";
        return url;
    }

    /// <summary>选最佳文件：可用 + 有下载地址；版本兼容优先（未知版本集合放行）；Release(1) 优先；fileId 降序</summary>
    public static CurseforgeFile? SelectBestFile(IEnumerable<CurseforgeFile> files, string? gameVersion = null)
    {
        var pool = files.Where(f => f.isAvailable && !string.IsNullOrEmpty(f.downloadUrl));
        if (gameVersion is not null)
            pool = pool.Where(f => f.gameVersions is null || f.gameVersions.Count == 0 || f.gameVersions.Contains(gameVersion));
        return pool.OrderByDescending(f => f.releaseType == 1)
                   .ThenByDescending(f => f.id)
                   .FirstOrDefault();
    }
}

/// <summary>CF /files 响应包装（PCL.Core 缺 files 响应类型，本地补）</summary>
public sealed record CurseforgeFilesResponse(List<CurseforgeFile> data);

/// <summary>CF /mods/search 响应（分页总数供 UI 分页栏）</summary>
public sealed record CurseforgeSearchPagination(int totalCount);

public sealed record CurseforgeSearchResponse(List<CurseforgeProject> data, CurseforgeSearchPagination? pagination);

/// <summary>搜索页结果（项目列表 + 总数；无分页信息时总数=当前页条数）</summary>
public sealed record CurseForgeSearchPage(List<CurseforgeProject> Projects, int TotalCount);
