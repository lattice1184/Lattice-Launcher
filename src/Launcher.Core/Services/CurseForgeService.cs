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
/// 依赖 x-api-key（设置页 CurseForgeApiKey 或环境变量 CURSEFORGE_API_KEY）；
/// 未配置时 IsEnabled=false（搜索返回空）。key 由 LauncherSettings 经 DPAPI 加密落盘（Secrets）。
/// 限流参考：官方 key 约 50 请求/30 秒，勿做深分页。
/// </summary>
public sealed class CurseForgeService
{
    public const string ApiBase = "https://api.curseforge.com/v1";
    private const int GameId = 432; // Minecraft

    private readonly string _apiBase;
    private readonly HttpClient _http;
    private readonly DownloadService _downloads;
    private readonly string _gameDirectory;
    private readonly string? _apiKeyOverride;

    /// <summary>是否启用 = 当前生效 key 非空（每次读设置/环境变量——设置页改 key 即时生效，无需重启）。
    /// key 由主进程 DPAPI 加密存设置（Secrets.Protect），不落明文磁盘。</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(EffectiveKey());

    /// <summary>当前生效 key：构造注入优先（null = 动态读设置/环境变量；空字符串 = 显式禁用），否则每次读设置（不再构造时缓存）</summary>
    private string? EffectiveKey() =>
        _apiKeyOverride is not null ? _apiKeyOverride : ResolveApiKey();

    public CurseForgeService(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null,
        string? apiBase = null)
        : this(null, http, downloads, gameDirectory, apiBase) // null = 动态读设置/环境变量
    {
    }

    /// <summary>测试注入用：显式 key（null = 动态读设置；空字符串 = 禁用）；apiBase = 本地代理地址（key 由代理注入）</summary>
    public CurseForgeService(string? apiKey, HttpClient? http = null, DownloadService? downloads = null,
        string? gameDirectory = null, string? apiBase = null)
    {
        _apiKeyOverride = apiKey;
        _apiBase = apiBase ?? ApiBase;
        _http = http ?? HttpClientPool.Create();
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
        // 8-19 降级：CF 不认的版本号（26.2 年份格式）→ 400 或 200+空 → 自动不带版本重试（显示全部）
        // （无搜索词时 0 结果只可能是版本过滤所致；带搜索词 0 结果大概率词不匹配，不降级不误导）
        var (page, dropped) = await WithVersionFallbackAsync(gameVersion,
            gv => SearchCoreAsync(type, query, gv, sort, limit, index, ct),
            p => p is null || (string.IsNullOrEmpty(query) && p.Projects.Count == 0));
        return page is null ? null : new CurseForgeSearchPage(page.Projects, page.TotalCount, dropped);
    }

    private async Task<CurseForgeSearchPage?> SearchCoreAsync(
        ProjectType type, string? query, string? gameVersion, SortIndex sort, int limit, int index, CancellationToken ct)
    {
        var url = ToApiBase(BuildSearchUrl(type, query, gameVersion, sort, limit, index));
        var response = await GetJsonAsync<CurseforgeSearchResponse>(url, ct);
        if (response is null) return null;
        return new CurseForgeSearchPage(response.data ?? [], response.pagination?.totalCount ?? response.data?.Count ?? 0);
    }

    /// <summary>8-19 版本参数降级：CF 对非法 gameVersion 返回 400 **或 200+空列表**（26.2 年份号实测：files 返回空、search 忽略）
    /// → 自动不带版本重试一次（防循环：最多 2 请求）。isEmpty 判断结果是否为空（files 空 / 无搜索词时搜索 0 结果）</summary>
    private async Task<(T? Value, bool Dropped)> WithVersionFallbackAsync<T>(
        string? gameVersion, Func<string?, Task<T?>> call, Func<T?, bool>? isEmpty = null)
    {
        if (gameVersion is null) return (await call(null), false);
        try
        {
            var value = await call(gameVersion);
            if (isEmpty?.Invoke(value) == true)
                return (await call(null), true);
            return (value, false);
        }
        catch (CurseForgeApiException ex) when (ex.CfStatusCode == 400)
        {
            return (await call(null), true);
        }
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
            var url = ToApiBase(BuildSearchUrl(ProjectType.Mod, null, null, SortIndex.Relevance, 1, 0));
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
        var response = await GetJsonAsync<CurseforgeProjectResponse>($"{_apiBase}/mods/{modId}", ct);
        return response?.data;
    }

    /// <summary>文件列表（安装版本选择用，懒加载）；8-19 版本参数 400 → 自动降级返回全部文件</summary>
    public async Task<List<CurseforgeFile>> GetFilesAsync(int modId, string? gameVersion = null, CancellationToken ct = default)
    {
        var (files, _) = await GetFilesWithFallbackAsync(modId, gameVersion, ct);
        return files;
    }

    /// <summary>文件列表 + 版本过滤是否被丢弃（8-19：26.2 年份号 CF 返回 200+空 → 降级全量，Dropped=true 时调用方不得再按原版本过滤）</summary>
    public async Task<(List<CurseforgeFile> Files, bool Dropped)> GetFilesWithFallbackAsync(int modId, string? gameVersion, CancellationToken ct)
    {
        return await WithVersionFallbackAsync(gameVersion, async gv =>
        {
            var url = $"{_apiBase}/mods/{modId}/files?pageSize=50"
                      + (gv is null ? "" : $"&gameVersion={Uri.EscapeDataString(gv)}");
            var response = await GetJsonAsync<CurseforgeFilesResponse>(url, ct);
            return response?.data ?? [];
        // 8-19 补：26.2 实测 files API 返回 200+空（非 400）——空列表也降级（否则详情页误报「没有适配版本」）
        }, files => files is null || files.Count == 0);
    }

    /// <summary>单文件详情（CF API 兜底：整合包 zip 内缺 jar 时按 projectID/fileID 拉取）</summary>
    public async Task<CurseforgeFile?> GetFileAsync(int modId, int fileId, CancellationToken ct = default)
    {
        if (!IsEnabled) return null;
        var response = await GetJsonAsync<CurseforgeFileResponse>($"{_apiBase}/mods/{modId}/files/{fileId}", ct);
        return response?.data;
    }

    /// <summary>匹配最佳文件：可用 + 版本兼容优先，releaseType=1（Release）优先，fileId 降序（近似"最新"）。
    /// 8-19 版本参数降级后不能再按原 gameVersion 过滤（CF 文件 gameVersions 不含 26.2——否则误报「没有适配文件」）。</summary>
    public async Task<CurseforgeFile?> FindBestFileAsync(int modId, string? gameVersion = null, CancellationToken ct = default)
    {
        var (files, dropped) = await GetFilesWithFallbackAsync(modId, gameVersion, ct);
        return SelectBestFile(files, dropped ? null : gameVersion);
    }

    /// <summary>安装：下载文件到实例目录（mods/resourcepacks/shaderpacks），整合包到 downloads/modpacks。SHA1 幂等。
    /// gameDirOverride：版本来源目录（PCL/自建）——MOD 必须装进版本真实目录（AF2，与 Modrinth 侧对齐）。</summary>
    public async Task<string> InstallAsync(
        int projectId, CurseforgeFile file, string instanceId, ProjectType type,
        DownloadProgressHandler? progress = null, CancellationToken ct = default, string? gameDirOverride = null)
    {
        if (string.IsNullOrEmpty(file.downloadUrl))
            throw new InvalidOperationException("该文件没有下载地址");
        var targetDir = EcosystemService.ResolveInstallPath(gameDirOverride ?? _gameDirectory, instanceId, type);
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
    /// ctx 非空时主文件与每个依赖各成一个组子任务（下载中心可见、可暂停/重试）；
    /// 依赖并行安装（门 4——CF 限流 50 req/30s 的安全余量，原串行 10 依赖 = 20 次往返）。
    /// </summary>
    public async Task<DependencyInstallReport> InstallWithDependenciesAsync(
        int projectId, CurseforgeFile file, string instanceId, ProjectType type,
        string? gameVersion,
        DownloadProgressHandler? progress = null, CancellationToken ct = default,
        string? gameDirOverride = null, DownloadGroupContext? ctx = null)
    {
        var report = new DependencyInstallReport();
        var projectIdText = projectId.ToString();

        // 1. 主文件
        try
        {
            var mainPath = await InstallOneAsync(ctx, $"主文件 {file.fileName}", file.fileLength,
                (p, c) => InstallAsync(projectId, file, instanceId, type, p, c, gameDirOverride), ct);
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
                    if (!int.TryParse(dep.ProjectId, out var depModId))
                    {
                        lock (report) report.Failed.Add(new FailedDependency(dep.ProjectId, "无效项目 ID"));
                        return;
                    }
                    // 8-19 补：GetFilesWithFallbackAsync 带 dropped——降级后不能再用 26.2 精确过滤（同 LoadCfAsync 修复）
                    var (files, dropped) = await GetFilesWithFallbackAsync(depModId, gameVersion, ct);
                    var depFile = files.FirstOrDefault(f => f.id.ToString() == dep.File.Id)
                                  ?? SelectBestFile(files, dropped ? null : gameVersion);
                    if (depFile is null)
                    {
                        lock (report) report.Failed.Add(new FailedDependency(dep.ProjectId, "未找到兼容文件"));
                        return;
                    }
                    var path = await InstallOneAsync(ctx, $"依赖 {depFile.fileName}", depFile.fileLength,
                        (p, c) => InstallAsync(depModId, depFile, instanceId, ProjectType.Mod, p, c, gameDirOverride), ct);
                    lock (report) report.Installed.Add(new InstalledDependency(dep.ProjectId, depFile.id.ToString(), path));
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

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var key = EffectiveKey(); // 每次请求读最新 key——改 key 即时生效
        // AL50：5xx/404 瞬时故障（CloudFront 边缘抽风，实测偶发）自动重试一次——CF 官方限流是 429 不在此列
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(key))
                req.Headers.Add("x-api-key", key);
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                // 8-19 容错：CF 对非法参数（如 26.2 年份版号）返回 200 + 错误 JSON（无 data）——
                // 直接 Deserialize 抛 JsonException → UI「匹配失败」；解析 CF 错误体转可读异常，
                // 结构不符（HTML/代理页）走通用文案。注意：错误 body 也能成功反序列化成 T（data=null）
                // ——Deserialize 成功后也必须显式检查 CF 错误体（data=null 的「空结果」≠ 合法空 data=[]）
                try
                {
                    var result = JsonSerializer.Deserialize<T>(json);
                    if (TryParseCfError(json, out var code, out var msg))
                        throw new CurseForgeApiException(code, $"CurseForge 请求失败：{msg}");
                    return result;
                }
                catch (JsonException)
                {
                    if (TryParseCfError(json, out var code, out var msg))
                        throw new CurseForgeApiException(code, $"CurseForge 请求失败：{msg}");
                    throw new HttpRequestException("CurseForge 响应格式异常，请稍后重试");
                }
            }
            if (attempt == 0 && (int)resp.StatusCode is 404 or >= 500)
            {
                await Task.Delay(500, ct); // 半秒后重试一次（CF 边缘瞬时故障自愈）
                continue;
            }
            // 8-19 非 2xx 也读 body 提取 CF 错误消息（否则 400 只显示「Response status code does not indicate success」）
            try
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (TryParseCfError(body, out var code, out var msg))
                    throw new CurseForgeApiException(code, $"CurseForge 请求失败：{msg}");
            }
            catch (HttpRequestException) { throw; }
            catch { /* 读 body 失败不掩盖原错误 */ }
            resp.EnsureSuccessStatusCode(); // 其余（401/403/429/…）原样抛出
            return default;
        }
    }

    /// <summary>8-19 CF 错误体解析：camelCase {"statusCode":400,"error":...,"message":...}（与 PCL.Core 模型同款命名）</summary>
    private static bool TryParseCfError(string body, out int code, out string message)
    {
        code = 0;
        message = "";
        try
        {
            var err = JsonSerializer.Deserialize<CurseForgeError>(body);
            if (err is null || err.statusCode <= 0) return false;
            code = err.statusCode;
            message = err.message ?? err.error ?? "未知错误";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>8-19 CF 错误响应（camelCase 位置参数，直接匹配官方错误 JSON）</summary>
    private sealed record CurseForgeError(int statusCode, string? error, string? message);

    /// <summary>8-19 CF 拒绝异常（带状态码——降级重试识别 400 用；继承 HttpRequestException 保调用方兼容）</summary>
    public sealed class CurseForgeApiException(int cfStatusCode, string message) : HttpRequestException(message)
    {
        public int CfStatusCode { get; } = cfStatusCode;
    }

    /// <summary>把静态 BuildSearchUrl 生成的官方地址切到实例 base（代理模式指向本地代理；直连模式原样）</summary>
    private string ToApiBase(string url) => _apiBase == ApiBase ? url : _apiBase + url[ApiBase.Length..];

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

/// <summary>CF 单文件响应包装（/mods/{id}/files/{fileId}）</summary>
public sealed record CurseforgeFileResponse(CurseforgeFile? data);

/// <summary>CF /mods/search 响应（分页总数供 UI 分页栏）</summary>
public sealed record CurseforgeSearchPagination(int totalCount);

public sealed record CurseforgeSearchResponse(List<CurseforgeProject> data, CurseforgeSearchPagination? pagination);

/// <summary>搜索页结果（项目列表 + 总数；无分页信息时总数=当前页条数）</summary>
public sealed record CurseForgeSearchPage(List<CurseforgeProject> Projects, int TotalCount, bool VersionFilterDropped = false);
