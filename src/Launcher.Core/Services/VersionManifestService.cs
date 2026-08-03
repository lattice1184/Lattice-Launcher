using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Services;

/// <summary>
/// 版本清单服务：拉取 Mojang 官方 manifest、磁盘缓存、合并本地已安装版本。
/// </summary>
public sealed class VersionManifestService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;

    /// <summary>解析后的版本条目（已安装标记 + 官方清单合并）</summary>
    public IReadOnlyList<GameVersionEntry> Entries => _entries;
    private List<GameVersionEntry> _entries = [];

    public VersionManifestService(HttpClient? http = null, string? gameDirectory = null)
    {
        _http = http ?? new HttpClient();
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "cache");
    }

    /// <summary>
    /// 拉取并合并版本清单。force=true 时忽略磁盘缓存强制刷新。
    /// 已安装判定跨所有扫描源（自建目录 + PCL/官方等已有环境），条目记录版本所在目录。
    /// </summary>
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

        var json = await _http.GetStringAsync(ManifestUrl, ct);
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
                if (File.Exists(Path.Combine(d, $"{id}.json"))) installed.TryAdd(id, dir);
            }
        }
        return installed;
    }

    /// <summary>合并后的条目（含已安装标记 + 所在目录，未安装为 ""）</summary>
    public sealed record GameVersionEntry(
        string Id,
        string Type,
        bool Installed,
        DateTime ReleaseTime,
        string? ManifestUrl,
        string GameDirectory);
}
