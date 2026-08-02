using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Launcher.Core.Launch;
using Launcher.Core.Model.Loader;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

namespace Launcher.Core.Download;

/// <summary>
/// 加载器下载源（四家）：
/// - Fabric / Quilt：meta API 直装（profile json 继承原版 → 写版本目录 → 全量下载，无进程）；
/// - Forge / NeoForge：官方安装器 jar + 安装器进程（--installClient）。
/// 前置条件：目标原版版本已安装（inheritsFrom 链需要父版本 JSON 在磁盘上）。
/// </summary>
public sealed class LoaderService
{
    private const string FabricMeta = "https://meta.fabricmc.net/v2/versions/loader";
    private const string QuiltMeta = "https://meta.quiltmc.org/v3/versions/loader";
    private const string ForgePromos = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string ForgeInstallerBase = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string NeoForgeInstallerBase = "https://maven.neoforged.net/releases/net/neoforged/neoforge";

    private readonly HttpClient _http;
    private readonly DownloadService _downloads;
    private readonly string _gameDirectory;

    public LoaderService(HttpClient? http = null, DownloadService? downloads = null, string? gameDirectory = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YanKa-Launcher/0.1");
        _downloads = downloads ?? new DownloadService();
        _gameDirectory = gameDirectory ?? GameDirectory.Detect();
    }

    // ---------- 版本列表 ----------

    public async Task<List<LoaderMetaVersion>> GetLoaderVersionsAsync(LoaderKind kind, string mcVersion, CancellationToken ct)
    {
        return kind switch
        {
            LoaderKind.Fabric => await GetFabricVersionsAsync(mcVersion, ct),
            LoaderKind.Quilt => await GetQuiltVersionsAsync(mcVersion, ct),
            LoaderKind.NeoForge => await GetNeoForgeVersionsAsync(mcVersion, ct),
            _ => await GetForgeVersionsAsync(mcVersion, ct),
        };
    }

    /// <summary>Fabric：meta.fabricmc.net/v2/versions/loader/{mc}（最新在前，stable 优先展示）</summary>
    private async Task<List<LoaderMetaVersion>> GetFabricVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var list = await GetJsonAsync<List<FabricMetaEntry>>($"{FabricMeta}/{mcVersion}", ct) ?? [];
        return list.Select(e => new LoaderMetaVersion(e.Loader?.Version ?? "", e.Loader?.Stable == true))
                   .Where(m => m.Version.Length > 0).ToList();
    }

    /// <summary>Quilt：meta.quiltmc.org/v3/versions/loader/{mc}（无 stable 字段，无 -beta/-alpha 视为稳定）</summary>
    private async Task<List<LoaderMetaVersion>> GetQuiltVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var list = await GetJsonAsync<List<FabricMetaEntry>>($"{QuiltMeta}/{mcVersion}", ct) ?? [];
        return list.Select(e => new LoaderMetaVersion(e.Loader?.Version ?? "",
                   e.Loader?.Version is { } v && !v.Contains('-'))).Where(m => m.Version.Length > 0).ToList();
    }

    /// <summary>
    /// Forge：promotions_slim.json 的 {mc}-recommended / {mc}-latest。
    /// 注：maven.minecraftforge.net 的 maven-metadata 已 404（Reposilite），promos 缺失即无可用版本。
    /// </summary>
    private async Task<List<LoaderMetaVersion>> GetForgeVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var promos = await GetJsonAsync<ForgePromotions>(ForgePromos, ct);
        var list = new List<LoaderMetaVersion>();
        var recommended = promos?.Promos?.GetValueOrDefault($"{mcVersion}-recommended");
        var latest = promos?.Promos?.GetValueOrDefault($"{mcVersion}-latest");
        if (recommended is not null) list.Add(new LoaderMetaVersion(recommended, true));
        if (latest is not null && latest != recommended) list.Add(new LoaderMetaVersion(latest, false));
        return list;
    }

    /// <summary>
    /// NeoForge：meta.neoforged.net v1 端点当前不可达，走 maven 元数据按版本前缀筛选
    /// （NeoForge 版本 = MC 版本去掉 "1." 前缀：1.21.1 → 21.1.x；安装器 URL 用完整版本号，无 mc 前缀）。
    /// </summary>
    private async Task<List<LoaderMetaVersion>> GetNeoForgeVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var prefix = mcVersion.StartsWith("1.", StringComparison.Ordinal) ? mcVersion[2..] + "." : mcVersion + ".";
        var xml = await _http.GetStringAsync(NeoForgeMetadata, ct);
        var doc = XDocument.Parse(xml);
        return doc.Descendants("version").Select(v => v.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => new LoaderMetaVersion(v, IsStableNeoForge(v)))
            .OrderByDescending(v => v.Version, new VersionComparer())
            .ThenByDescending(v => v.IsStable)
            .ToList();
    }

    private static bool IsStableNeoForge(string v)
        => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase)
           && !v.Contains("-alpha", StringComparison.OrdinalIgnoreCase)
           && !v.Contains("-rc", StringComparison.OrdinalIgnoreCase);

    // ---------- 安装计划 ----------

    public async Task<LoaderInstallPlan> CreatePlanAsync(LoaderKind kind, string mcVersion, string? loaderVersion, CancellationToken ct)
    {
        switch (kind)
        {
            case LoaderKind.Fabric:
            {
                var lv = loaderVersion ?? await PickFirstAsync(GetFabricVersionsAsync(mcVersion, ct), "该版本暂无 Fabric 加载器");
                return new LoaderInstallPlan(kind, mcVersion, lv, $"{FabricMeta}/{mcVersion}/{lv}/profile/json", null, null, null);
            }
            case LoaderKind.Quilt:
            {
                var lv = loaderVersion ?? await PickFirstAsync(GetQuiltVersionsAsync(mcVersion, ct), "该版本不支持 Quilt（1.18.2+）");
                return new LoaderInstallPlan(kind, mcVersion, lv, $"{QuiltMeta}/{mcVersion}/{lv}/profile/json", null, null, null);
            }
            case LoaderKind.NeoForge:
            {
                var lv = loaderVersion ?? await PickFirstAsync(GetNeoForgeVersionsAsync(mcVersion, ct), "该版本暂无 NeoForge 版本");
                return new LoaderInstallPlan(kind, mcVersion, lv, null,
                    $"{NeoForgeInstallerBase}/{lv}/neoforge-{lv}-installer.jar", null, null);
            }
            default:
            {
                var lv = loaderVersion ?? await PickFirstAsync(GetForgeVersionsAsync(mcVersion, ct), "该版本暂无 Forge 版本");
                return new LoaderInstallPlan(kind, mcVersion, lv, null,
                    $"{ForgeInstallerBase}/{mcVersion}-{lv}/forge-{mcVersion}-{lv}-installer.jar", null, null);
            }
        }
    }

    private static async Task<string> PickFirstAsync(Task<List<LoaderMetaVersion>> versionsTask, string emptyMessage)
    {
        var list = await versionsTask;
        return list.FirstOrDefault()?.Version ?? throw new InvalidOperationException(emptyMessage);
    }

    // ---------- 安装 ----------

    /// <summary>安装（旧展平路径，兼容测试与旧调用）</summary>
    public Task InstallAsync(LoaderInstallPlan plan, DownloadProgressHandler? progress, CancellationToken ct)
        => InstallCoreAsync(plan, progress, null, ct);

    /// <summary>安装（组任务路径：加载器配置/安装器为子任务，版本下载并入同一组）</summary>
    public Task InstallAsync(LoaderInstallPlan plan, DownloadGroupContext ctx, CancellationToken ct)
        => InstallCoreAsync(plan, null, ctx, ct);

    private async Task InstallCoreAsync(LoaderInstallPlan plan, DownloadProgressHandler? progress,
        DownloadGroupContext? ctx, CancellationToken ct)
    {
        switch (plan.Kind)
        {
            case LoaderKind.Fabric:
            case LoaderKind.Quilt:
                await InstallMetaAsync(plan, progress, ctx, ct);
                break;
            default:
                await InstallInstallerAsync(plan, progress, ctx, ct);
                break;
        }
    }

    /// <summary>Fabric/Quilt：拉 profile json（inheritsFrom 原版）→ 写版本目录 → 全量下载（链解析下载 client jar 与库）</summary>
    private async Task InstallMetaAsync(LoaderInstallPlan plan, DownloadProgressHandler? progress,
        DownloadGroupContext? ctx, CancellationToken ct)
    {
        RequireVanilla(plan.McVersion);

        // 组路径：配置为子任务（weight=0 不定进度），版本下载并入同一组
        if (ctx is not null)
        {
            VersionJson? version = null;
            await ctx.AddChild($"加载器配置 {plan.Kind}", 0, async (p, c) =>
            {
                p(new DownloadProgress("查询加载器版本", null, 0, 0, 0));
                var json = await _http.GetStringAsync(plan.ProfileJsonUrl!, c);
                version = JsonSerializer.Deserialize<VersionJson>(json)
                    ?? throw new InvalidDataException("加载器版本 JSON 解析失败");
                var id = VersionInstaller.SafeId(version.Id);
                var versionDir = Path.Combine(_gameDirectory, "versions", id);
                Directory.CreateDirectory(versionDir);
                await File.WriteAllTextAsync(Path.Combine(versionDir, $"{id}.json"), json, c);
                p(new DownloadProgress("加载器配置完成", null, 0, 0, 100));
            }).Completion;

            await _downloads.DownloadVersionAsync(version!, ctx, null, ct);
            return;
        }

        progress?.Invoke(new DownloadProgress("查询加载器版本", null, 0, 0, 0));

        var json = await _http.GetStringAsync(plan.ProfileJsonUrl!, ct);
        var legacyVersion = JsonSerializer.Deserialize<VersionJson>(json)
            ?? throw new InvalidDataException("加载器版本 JSON 解析失败");
        var id = VersionInstaller.SafeId(legacyVersion.Id);
        var versionDir = Path.Combine(_gameDirectory, "versions", id);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, $"{id}.json"), json, ct);

        progress?.Invoke(new DownloadProgress($"下载 {plan.Kind} 加载器与库文件", null, 0, 0, 0));
        await _downloads.DownloadVersionAsync(legacyVersion, null, progress, ct);
    }

    /// <summary>Forge/NeoForge：下载官方安装器 → 安装器进程 --installClient 写入版本目录</summary>
    private async Task InstallInstallerAsync(LoaderInstallPlan plan, DownloadProgressHandler? progress,
        DownloadGroupContext? ctx, CancellationToken ct)
    {
        RequireVanilla(plan.McVersion);

        var installerDir = Path.Combine(_gameDirectory, "installers");
        Directory.CreateDirectory(installerDir);
        var installerPath = Path.Combine(installerDir, Path.GetFileName(new Uri(plan.InstallerUrl!).LocalPath));

        // 组路径：安装器下载 + 运行各为一个子任务（运行阶段输出行实时进 Stage）
        if (ctx is not null)
        {
            await ctx.AddChild($"安装器 {Path.GetFileName(installerPath)}", plan.InstallerSize ?? 0, (p, c) =>
                _downloads.DownloadFileAsync(plan.InstallerUrl!, installerPath, plan.InstallerSha1, plan.InstallerSize, p, c)).Completion;

            await ctx.AddChild($"运行 {plan.Kind} 安装器", 0, async (p, c) =>
            {
                var java = JavaSelector.Pick(null);
                var exitCode = await InstallerProcess.RunAsync(java,
                    ["-jar", installerPath, "--installClient", _gameDirectory],
                    line => p(new DownloadProgress(line, null, 0, 0, 0)), c);
                if (exitCode != 0)
                    throw new InvalidOperationException($"安装器执行失败（退出码 {exitCode}），请查看安装器输出");
            }).Completion;
            return;
        }

        progress?.Invoke(new DownloadProgress("下载安装器", Path.GetFileName(installerPath), 0, 0, 0));
        await _downloads.DownloadFileAsync(plan.InstallerUrl!, installerPath, plan.InstallerSha1, plan.InstallerSize, progress, ct);

        progress?.Invoke(new DownloadProgress($"运行 {plan.Kind} 安装器", null, 0, 0, 0));
        var java = JavaSelector.Pick(null);
        var exitCode = await InstallerProcess.RunAsync(java,
            ["-jar", installerPath, "--installClient", _gameDirectory],
            line => progress?.Invoke(new DownloadProgress(line, null, 0, 0, 0)), ct);
        if (exitCode != 0)
            throw new InvalidOperationException($"安装器执行失败（退出码 {exitCode}），请查看安装器输出");
    }

    /// <summary>加载器版本 JSON 通过 inheritsFrom 继承原版，父版本必须已安装</summary>
    private void RequireVanilla(string mcVersion)
    {
        var jsonPath = Path.Combine(_gameDirectory, "versions", mcVersion, $"{mcVersion}.json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"请先在版本页安装原版 {mcVersion}，再安装加载器");
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>数字感知版本比较（21.1.110 &gt; 21.1.99；-beta 后缀靠 IsStable 排序）</summary>
    private sealed class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            var xp = x!.Split(['.', '-']);
            var yp = y!.Split(['.', '-']);
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
    }
}
