using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Diagnostics;
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

    /// <summary>全量安装（client jar / libraries / assets / logging），进度经 DownloadProgressHandler 上报（旧展平路径）</summary>
    public Task InstallAsync(VersionJson version, DownloadProgressHandler? progress, CancellationToken ct)
        => InstallCoreAsync(version, ctx: null, progress, ct);

    /// <summary>全量安装（组任务路径：阶段全并行 + 文件级子任务）</summary>
    public Task InstallAsync(VersionJson version, DownloadGroupContext ctx, CancellationToken ct)
        => InstallCoreAsync(version, ctx, progress: null, ct);

    /// <summary>
    /// 事务化安装：下载 → 先校验、后打完整安装标记（半装版本不带标记）；
    /// 任一步失败删除本次新建的 client jar——json 留作缓存（重试免拉取），libraries 共享目录不删。
    /// 半装态消失后「已安装」判定（json+jar）恢复诚实，不再"显示已安装→启动才报缺文件"。
    /// </summary>
    private async Task InstallCoreAsync(VersionJson version, DownloadGroupContext? ctx,
        DownloadProgressHandler? progress, CancellationToken ct)
    {
        try
        {
            await _downloads.DownloadVersionAsync(version, ctx, progress, ct);
            VerifyInstalled(version);
            InstallMarker.Mark(_gameDirectory, version.Id); // 完整安装后才打标记
        }
        catch
        {
            // 半装清理：本次新建的 client jar 删掉（安装前本不存在，删除幂等安全）
            try { File.Delete(Path.Combine(_gameDirectory, "versions", version.Id, $"{version.Id}.jar")); } catch { }
            throw;
        }
    }

    /// <summary>
    /// AL29 H6：安装后完整性校验——下载完成必须 == 文件完整，不得「虚假成功」
    /// （下载列表曾静默跳过 url 形式库；缺失如实报错，由修复路径补全）。
    /// 父 json 缺失时链保留 → 只校验子版本自身文件。
    /// </summary>
    private void VerifyInstalled(VersionJson version)
    {
        var missing = AutoRepairService.VerifyVersion(version, _gameDirectory);
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"安装完成但校验失败：缺 {missing.Count} 个文件（首例：{missing[0]}）。可重新下载补全");
    }

    /// <summary>路径安全化：拒绝 .. 与分隔符（与启动管道一致）</summary>
    public static string SafeId(string id) => id.Replace("..", "").Replace('/', '_').Replace('\\', '_');
}
