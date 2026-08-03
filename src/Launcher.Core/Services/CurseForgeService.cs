using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Download;
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

    /// <summary>已配置 API Key；false = CF 源禁用（UI 提示"未配置 API Key"）</summary>
    public bool IsEnabled { get; }

    public CurseForgeService(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null)
        : this(ResolveApiKey(), http, downloads, gameDirectory)
    {
    }

    /// <summary>测试注入用：显式 key（null/空 = 禁用）</summary>
    public CurseForgeService(string? apiKey, HttpClient? http = null, DownloadService? downloads = null,
        string? gameDirectory = null)
    {
        IsEnabled = !string.IsNullOrWhiteSpace(apiKey);
        _http = http ?? new HttpClient();
        if (IsEnabled)
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
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
    public async Task<List<CurseforgeProject>> SearchAsync(
        ProjectType type, string? query = null, string? gameVersion = null,
        SortIndex sort = SortIndex.Relevance,
        int limit = 20, int index = 0, CancellationToken ct = default)
    {
        if (!IsEnabled) return [];
        var url = BuildSearchUrl(type, query, gameVersion, sort, limit, index);
        var response = await GetJsonAsync<CurseforgeProjectsResponse>(url, ct);
        return response?.data ?? [];
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
        await _downloads.DownloadFileAsync(file.downloadUrl, destPath, sha1, file.fileLength, progress, ct);
        return destPath;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(url, ct);
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
