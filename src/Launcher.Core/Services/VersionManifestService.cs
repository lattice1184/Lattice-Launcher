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
    private readonly string _gameDirectory;
    private readonly string _cacheDirectory;

    /// <summary>解析后的版本条目（已安装标记 + 官方清单合并）</summary>
    public IReadOnlyList<GameVersionEntry> Entries => _entries;
    private List<GameVersionEntry> _entries = [];

    public VersionManifestService(HttpClient? http = null, string? gameDirectory = null)
    {
        _http = http ?? new HttpClient();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "cache");
    }

    /// <summary>
    /// 拉取并合并版本清单。force=true 时忽略磁盘缓存强制刷新。
    /// </summary>
    public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        var manifest = await LoadManifestAsync(force, ct);
        var installed = ScanInstalledVersions();
        _entries = manifest.Versions
            .Select(v => new GameVersionEntry(
                v.Id, v.Type, installed.Contains(v.Id), v.ReleaseTime, v.Url))
            .OrderByDescending(v => v.ReleaseTime)
            .ToList();
    }

    private async Task<VersionManifest> LoadManifestAsync(bool force, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(_cacheDirectory, "version_manifest_v2.json");

        if (!force && File.Exists(cachePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<VersionManifest>(await File.ReadAllTextAsync(cachePath, ct));
                if (cached is not null && cached.Versions.Count > 0) return cached;
            }
            catch (Exception) { /* 缓存损坏则重新拉取 */ }
        }

        var json = await _http.GetStringAsync(ManifestUrl, ct);
        await File.WriteAllTextAsync(cachePath, json, ct);
        return JsonSerializer.Deserialize<VersionManifest>(json)!;
    }

    /// <summary>磁盘重扫，就地更新 Installed 标记（版本/加载器安装完成后调用）</summary>
    public void RescanInstalled()
    {
        var installed = ScanInstalledVersions();
        _entries = _entries.Select(e => e with { Installed = installed.Contains(e.Id) }).ToList();
    }

    private HashSet<string> ScanInstalledVersions()
    {
        var versionsDir = Path.Combine(_gameDirectory, "versions");
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(versionsDir)) return installed;
        foreach (var dir in Directory.EnumerateDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, $"{id}.json"))) installed.Add(id);
        }
        return installed;
    }

    /// <summary>合并后的条目（含已安装标记）</summary>
    public sealed record GameVersionEntry(
        string Id,
        string Type,
        bool Installed,
        DateTime ReleaseTime,
        string? ManifestUrl);
}
