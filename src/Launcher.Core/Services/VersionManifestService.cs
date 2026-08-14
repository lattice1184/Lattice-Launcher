using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;
using Launcher.Core.Download;

namespace Launcher.Core.Services;

/// <summary>
/// 版本清单服务：拉取 Mojang 官方 manifest、磁盘缓存、合并本地已安装版本。
/// </summary>
public sealed class VersionManifestService
{
    /// <summary>Mojang 版本清单（AM：ServerInstaller 服务端 URL 推断复用）</summary>
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    /// <summary>BMCLAPI 清单镜像（2026-08-08 实测：200 + 273,470 字节完整清单 + 0.5s；GET 302 跳 CDN，HttpClient 自动跟随）</summary>
    public const string ManifestMirrorUrl = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";

    /// <summary>清单候选链（依序尝试，首个成功者胜出）：官方 piston-meta → BMCLAPI 镜像</summary>
    public static readonly string[] ManifestUrls = [ManifestUrl, ManifestMirrorUrl];

    /// <summary>逐候选拉取清单 JSON：官方失败自动换镜像；用户取消照常传播；全失败抛 HttpRequestException</summary>
    public static async Task<string> FetchManifestJsonAsync(HttpClient http, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var url in ManifestUrls)
        {
            try
            {
                return await http.GetStringAsync(url, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                last = ex;
            }
        }
        throw new HttpRequestException($"版本清单拉取失败（{ManifestUrls.Length} 个源均不可用）", last);
    }

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;

    /// <summary>解析后的版本条目（已安装标记 + 官方清单合并）</summary>
    public IReadOnlyList<GameVersionEntry> Entries => _entries;
    private List<GameVersionEntry> _entries = [];

    public VersionManifestService(HttpClient? http = null, string? gameDirectory = null, string? cacheDirectory = null)
    {
        // 清单是元数据小请求：15s 总超时（国内直连官方清单慢/失败时快速失败，不卡修复/刷新）
        _http = http ?? new HttpClient(HttpClientPool.SharedHandler) { Timeout = TimeSpan.FromSeconds(15) };
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "cache");
    }

    /// <summary>
    /// 拉取并合并版本清单。force=true 时忽略磁盘缓存强制刷新。
    /// 已安装判定跨所有扫描源（自建目录 + PCL/官方等已有环境），条目记录版本所在目录。
    /// </summary>
    /// <summary>按版本 id 查清单 URL（复用 24h 缓存清单；查不到返回 null）——整合包导入预取父版本 json 用</summary>
    public async Task<string?> GetVersionJsonUrlAsync(string versionId, CancellationToken ct = default)
    {
        var manifest = await LoadManifestAsync(false, ct);
        return manifest.Versions.FirstOrDefault(v => v.Id == versionId)?.Url;
    }

    public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        var manifest = await LoadManifestAsync(force, ct);
        var installed = ScanInstalledVersions();
        _entries = manifest.Versions
            .Select(v => new GameVersionEntry(
                v.Id, v.Type, installed.TryGetValue(v.Id, out var dir), v.ReleaseTime, v.Url,
                installed.TryGetValue(v.Id, out var gd) ? gd : ""))
            .OrderByDescending(v => v.ReleaseTime)
            .ToList();
    }

    private async Task<VersionManifest> LoadManifestAsync(bool force, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(_cacheDirectory, "version_manifest_v2.json");

        // TTL 24h：缓存超期后强制重新拉取（否则新发布版本永远不可见）
        if (!force && File.Exists(cachePath))
        {
            try
            {
                var info = new FileInfo(cachePath);
                if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromHours(24))
                {
                    var cached = JsonSerializer.Deserialize<VersionManifest>(await File.ReadAllTextAsync(cachePath, ct));
                    if (cached is not null && cached.Versions.Count > 0) return cached;
                }
            }
            catch (Exception) { /* 缓存损坏则重新拉取 */ }
        }

        var json = await FetchManifestJsonAsync(_http, ct);
        await File.WriteAllTextAsync(cachePath, json, ct);
        return JsonSerializer.Deserialize<VersionManifest>(json)!;
    }

    /// <summary>磁盘重扫，就地更新 Installed 标记与所在目录（版本/加载器安装完成后调用）</summary>
    public void RescanInstalled()
    {
        var installed = ScanInstalledVersions();
        _entries = _entries.Select(e => e with
        {
            Installed = installed.TryGetValue(e.Id, out var dir),
            GameDirectory = installed.TryGetValue(e.Id, out var gd) ? gd : "",
        }).ToList();
    }

    /// <summary>跨所有扫描源枚举已安装版本（id → 所在目录）</summary>
    private Dictionary<string, string> ScanInstalledVersions()
    {
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, _) in GameDirectory.ScanSourceDirs())
        {
            var versionsDir = Path.Combine(dir, "versions");
            if (!Directory.Exists(versionsDir)) continue;
            foreach (var d in Directory.EnumerateDirectories(versionsDir))
            {
                var id = Path.GetFileName(d);
                // 8-14 误标清理：非自建目录（PCL/官方扫描源）的标记文件是历史误打
                // （修复/自动修复路径写入）——顺带移除，防止「本启动器」标签显示在别人装的版本上
                if (!GameDirectory.IsOwnInstallDir(dir))
                {
                    InstallMarker.Unmark(dir, id);
                    InstallMarker.UnmarkPrefetched(dir, id);
                }
                if (IsInstalled(dir, id)) installed.TryAdd(id, dir);
            }
        }
        return installed;
    }

    /// <summary>
    /// 「已安装」权威判定：json 与 client jar 都存在（AL29 C1——原先只看 json，预取 json 的
    /// 残件版本谎报已装，启动时才报「客户端文件缺失」）。完整加载器版本（fabric 等）的
    /// client jar 沿 inheritsFrom 链落子版本目录，不受影响；只有 json 的残件在版本页仍可见可修。
    /// </summary>
    public static bool IsInstalled(string gameDir, string id)
        => File.Exists(Path.Combine(gameDir, "versions", id, $"{id}.json"))
        && File.Exists(Path.Combine(gameDir, "versions", id, $"{id}.jar"));

    /// <summary>
    /// 实例判定（MOD 安装目标）：json 存在即可——26.2 这类 Fabric 父版本的 client jar 沿
    /// inheritsFrom 链落加载器子目录，双文件同目录判定会漏掉（版本页已是 json-only 口径）。
    /// 预取残留（.prefetched 且未正式安装）排除——半成品目录不算实例。
    /// 注意：IsInstalled（json+jar）保持不动——仍是版本页 Installed 标记的权威口径。
    /// </summary>
    public static bool IsInstanceTarget(string gameDir, string id)
        => File.Exists(Path.Combine(gameDir, "versions", id, $"{id}.json"))
        && InstallMarker.ShouldShowInPage(gameDir, id);

    /// <summary>合并后的条目（含已安装标记 + 所在目录，未安装为 ""）</summary>
    public sealed record GameVersionEntry(
        string Id,
        string Type,
        bool Installed,
        DateTime ReleaseTime,
        string? ManifestUrl,
        string GameDirectory);

    /// <summary>
    /// 生态页版本筛选候选：release（非愚人节）且 &gt;= minVersion，语义降序（26.2 排最上、1.21.10 &gt; 1.21.6）。
    /// 下限 1.16：更老版本的 mod 生态早已沉寂，全列只会让下拉臃肿。纯离线（测试友好）。
    /// </summary>
    public static List<string> FilterGameVersionOptions(IEnumerable<GameVersionEntry> entries, string minVersion = "1.16")
        => entries.Where(e => e.Type == "release" && !VersionClassifier.IsAprilFools(e))
                  .Select(e => e.Id)
                  .Where(id => EcosystemService.CompareGameVersions(id, minVersion) >= 0)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderByDescending(id => id, Comparer<string>.Create(EcosystemService.CompareGameVersions))
                  .ToList();
}
