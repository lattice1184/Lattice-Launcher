using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Download;

/// <summary>
/// 游戏本体安装：取/缓存版本 JSON（versions/{id}/{id}.json）→ 编排全量下载。
/// 版本 id 拼入路径前净化（拒绝 .. 与分隔符）。
/// </summary>
public sealed class VersionInstaller
{
    private readonly DownloadService _downloads;
    private readonly HttpClient _http;
    private readonly string _gameDirectory;

    public VersionInstaller(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null)
    {
        _http = http ?? new HttpClient();
        _downloads = downloads ?? new DownloadService();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
    }

    /// <summary>优先读磁盘缓存 versions/{id}/{id}.json；缺失时从清单地址拉取并写入（一次性缓存）</summary>
    public async Task<VersionJson> GetOrFetchVersionJsonAsync(string id, string? manifestUrl, CancellationToken ct)
    {
        var safeId = SafeId(id);
        var jsonPath = Path.Combine(_gameDirectory, "versions", safeId, $"{safeId}.json");

        if (File.Exists(jsonPath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(jsonPath, ct));
                if (cached is not null) return cached;
            }
            catch (Exception) { /* 损坏则重新拉取 */ }
        }

        if (string.IsNullOrEmpty(manifestUrl))
            throw new InvalidOperationException($"版本 {id} 缺少清单下载地址");

        var json = await _http.GetStringAsync(manifestUrl, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        await File.WriteAllTextAsync(jsonPath, json, ct);
        return JsonSerializer.Deserialize<VersionJson>(json)
            ?? throw new InvalidDataException($"版本 JSON 解析失败: {id}");
    }

    /// <summary>全量安装（client jar / libraries / assets / logging），进度经 DownloadProgressHandler 上报</summary>
    public Task InstallAsync(VersionJson version, DownloadProgressHandler? progress, CancellationToken ct)
        => _downloads.DownloadVersionAsync(version, progress, ct);

    /// <summary>路径安全化：拒绝 .. 与分隔符（与启动管道一致）</summary>
    public static string SafeId(string id) => id.Replace("..", "").Replace('/', '_').Replace('\\', '_');
}
