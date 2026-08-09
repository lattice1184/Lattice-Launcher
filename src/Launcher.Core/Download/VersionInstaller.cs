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
            // AL42 补：缓存命中也要打预取标记——旧版本（无 AL42 时）写下的缓存没有 .prefetched，
            // 不补的话删除加载器版本时清理判定失败，留下幽灵条目（真机 08-09 第 2 轮循环测试发现）。
            // 守卫：已正式安装（.yanla-installed）的版本永不误加——真机 08-09 自动修复流程
            // 读已装版本 json 命中缓存 → 误打 .prefetched → 版本页把它当「预取残留」隐藏（26.2 消失根因）
            if (!InstallMarker.IsMarked(_gameDirectory, safeId))
                InstallMarker.MarkPrefetched(_gameDirectory, safeId);
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
        // AL42：预取 json 打标记——仅供加载器继承用，版本页不显示该条目
        //（下载「1.21.10 + Fabric」后不再出现分开的「1.21.10 缺文件」；正式安装完成时 Mark 会移除）
        // 守卫同缓存命中分支：json 被删后重拉也不覆盖已安装语义
        if (!InstallMarker.IsMarked(_gameDirectory, safeId))
            InstallMarker.MarkPrefetched(_gameDirectory, safeId);
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

    /// <summary>
    /// AL41/AL42 删除完整性：沿 inheritsFrom 链清理「预取残留」的父版本目录。
    /// 下载「1.21.10 + Fabric」只装合并的 fabric 版本，原版 json 是预取（供继承，带 .prefetched 标记）——
    /// 删 fabric 后原版残留 → 版本页出现删不掉的「缺文件」幽灵条目（真机 08-09：删 1.21.10 (Fabric) 后 1.21.10 红字）。
    /// 判定：父版本带 .prefetched 标记（预取专用）+ 不被其他版本引用 → 删；正式安装（.yanla-installed）
    /// 与无标记残件（下载中断，需保留可修）不碰。
    /// </summary>
    public static void CleanupOrphanParents(string gameDir, string versionId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { versionId };
        var current = versionId;
        while (true)
        {
            var jsonPath = Path.Combine(gameDir, "versions", current, $"{current}.json");
            if (!File.Exists(jsonPath)) break;
            try
            {
                var v = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(jsonPath));
                var parent = v?.InheritsFrom;
                if (string.IsNullOrEmpty(parent) || !seen.Add(parent)) break;
                var parentDir = Path.Combine(gameDir, "versions", parent);
                var parentJson = Path.Combine(parentDir, $"{parent}.json");
                // 目录或 json 缺失即链断
                if (!Directory.Exists(parentDir) || !File.Exists(parentJson)) break;
                // 只清预取残留：正式安装（标记）与无标记残件（可修）不碰
                // 守卫：双标记（.yanla-installed + .prefetched 误打残留）的已装版本绝不删
                if (InstallMarker.IsMarked(gameDir, parent) || !InstallMarker.IsPrefetched(gameDir, parent)) break;
                // 预取残留但还被其他版本引用（多 fabric 版本共享同一原版）→ 不删
                if (IsReferencedByOthers(gameDir, parent, seen)) break;
                Directory.Delete(parentDir, true);
                current = parent;
            }
            catch { break; } // 单版损坏不阻断删除流程
        }
    }

    /// <summary>除 seen（自身链）外，是否还有其他版本的 json 以 parent 为父</summary>
    private static bool IsReferencedByOthers(string gameDir, string parent, HashSet<string> seen)
    {
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir)) return false;
        foreach (var d in Directory.EnumerateDirectories(versionsDir))
        {
            var id = Path.GetFileName(d);
            if (seen.Contains(id)) continue;
            var p = Path.Combine(d, $"{id}.json");
            if (!File.Exists(p)) continue;
            try
            {
                var v = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(p));
                if (string.Equals(v?.InheritsFrom, parent, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { /* 损坏 json 跳过 */ }
        }
        return false;
    }
}
