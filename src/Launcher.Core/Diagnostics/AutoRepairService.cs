using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Launch;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Server;
using Launcher.Core.Utils;

namespace Launcher.Core.Diagnostics;

/// <summary>
/// 自修复执行层（AL9）：按诊断命中的 FixKind 执行修复。
/// Redownload → VersionInstaller 幂等补全重下（缺失才下，走下载队列可见进度）；
/// ReExtractNatives → 删 natives 目录后从库 jar 重新解压（清残留 dll）。
/// </summary>
public sealed class AutoRepairService
{
    /// <summary>
    /// 版本文件补全重下（client jar + libraries + assets + log4j，幂等差量）。
    /// AL10：① 判定用 TerminalState（State 经 UI Post 异步生效，Completion 同步——读 State 读到旧值
    /// Downloading 误判失败，2026-08-05 日志实证）② inheritsFrom 父 json 缺失先递归补父
    /// ③ 下载用合并版本（client jar URL/全部 libraries 继承父链——覆盖加载器 profile 无 downloads 的结构）。
    /// </summary>
    public static async Task<string> FixRedownloadAsync(string versionId, string gameDir, int depth = 0)
    {
        var installer = new VersionInstaller(gameDirectory: gameDir);
        var version = await installer.GetOrFetchVersionJsonAsync(versionId, null, CancellationToken.None);
        // 父版本补全：父 json 缺失 → 递归先补（深度上限防环）
        if (depth < 3 && version.InheritsFrom is { } parentId
            && !File.Exists(Path.Combine(gameDir, "versions", parentId, $"{parentId}.json")))
        {
            try { await FixRedownloadAsync(parentId, gameDir, depth + 1); }
            catch { /* 补父失败不阻断主修复（主下载的 merged 链可能已覆盖） */ }
        }
        var merged = VersionJsonMerger.ResolveChain(version, id => LoadParentJson(gameDir, id));
        // AL31 修复快路径：先诊断缺失清单——0 缺失直接返回，不排下载队列（修复时"缺 0 个也全流程跑"是
        // 慢的体感来源之一；诊断本身是本地磁盘遍历，秒级）
        var preMissing = VerifyFiles(merged, gameDir, version.InheritsFrom);
        if (preMissing.Count == 0) return "文件已完整，无需修复";
        var task = DownloadManager.Instance.EnqueueGroup($"自动修复 {versionId}",
            (ctx, ct) => installer.InstallAsync(merged, ctx, ct));
        await task.Completion;
        if (task.TerminalState != DownloadTaskState.Completed)
            throw new InvalidOperationException($"补全未完成（{task.TerminalState}）");
        // AL10.2：下载后校验文件完整性——修复不得"虚假成功"（下载列表曾静默跳过 url 形式库），缺失如实报告
        var missing = VerifyFiles(merged, gameDir);
        if (missing.Count > 0)
            throw new InvalidOperationException($"补全后仍缺 {missing.Count} 个文件（首例：{missing[0]}）");
        return "补全完成";
    }

    /// <summary>校验版本文件完整性：client jar + 本 OS 实际需要的 libraries 本地存在；返回缺失清单（空 = 完整）。
    /// AL11：按 OS 规则过滤——Linux/Mac natives 库不会下载，不纳入校验，否则误报"仍缺 N 个文件"假失败。
    /// AL29 真机修正：① client jar 落盘兼容两种语义——下载器落子版本目录（H6），官方安装器落父版本目录
    /// （Forge 1.21.10 真机实测 30MB jar 在 versions/{父id}/{父id}.jar）；② artifact url 为空的库是"继承引用"
    /// （forge 的 client classifier 库 url=""，安装器标记 Invalid 跳过），无实体文件，不校验。</summary>
    public static List<string> VerifyFiles(VersionJson merged, string gameDir, string? clientParentId = null)
    {
        var missing = new List<string>();
        var clientPath = Path.Combine(gameDir, "versions", merged.Id, $"{merged.Id}.jar");
        if (!File.Exists(clientPath) && (clientParentId is null
            || !File.Exists(Path.Combine(gameDir, "versions", clientParentId, $"{clientParentId}.jar"))))
            missing.Add(clientPath);
        var librariesDir = Path.Combine(gameDir, "libraries");
        var resolver = new RulesResolver();
        foreach (var lib in merged.Libraries ?? [])
        {
            if (!resolver.IsAllowed(lib.Rules)) continue; // 非本 OS/特性不满足的库不下载 → 不校验
            var artifact = lib.Downloads?.Artifact;
            if (artifact is not null && string.IsNullOrEmpty(artifact.Url)) continue; // 继承引用，无实体文件
            var p = Path.Combine(librariesDir, MavenPath.FullPath(lib.Name));
            if (!File.Exists(p)) missing.Add(p);
        }
        return missing;
    }

    /// <summary>
    /// 沿 inheritsFrom 链合并后校验版本文件完整性（AL29 H5/H6 共用）。
    /// 父 json 缺失时链保留 InheritsFrom → 只校验子版本自身（此时启动路径会抛
    /// ParentVersionMissingException，见 JavaArgumentsBuilder）。
    /// </summary>
    public static List<string> VerifyVersion(VersionJson version, string gameDir)
    {
        var merged = version.InheritsFrom is null ? version
            : VersionJsonMerger.ResolveChain(version, id => LoadParentJson(gameDir, id));
        // 官方安装器把 client jar 落父版本目录（Forge 实测）→ 传父 id 作备选路径
        return VerifyFiles(merged, gameDir, version.InheritsFrom);
    }

    /// <summary>读磁盘父版本 json（inheritsFrom 链解析用）；缺失/损坏返回 null</summary>
    private static VersionJson? LoadParentJson(string gameDir, string id)
    {
        var p = Path.Combine(gameDir, "versions", id, $"{id}.json");
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(p)); }
        catch { return null; }
    }

    /// <summary>
    /// 修复服务端（开服专用）：删除现有 server.jar 后重新下载（幂等）。
    /// 客户端自修复只补客户端文件；服务端 jar 缺失/损坏由开服崩溃诊断（FixKind.Redownload）触发此修复。
    /// </summary>
    public static async Task<string> FixServerJarAsync(string versionId, string gameDir,
        ServerInstaller? installer = null, CancellationToken ct = default)
    {
        var dir = ServerInstaller.ServerDir(gameDir, versionId);
        var jar = Path.Combine(dir, "server.jar");
        if (File.Exists(jar))
        {
            try { File.Delete(jar); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法删除损坏的 server.jar（{ex.Message}），可手动删除后重试");
            }
        }
        await (installer ?? new ServerInstaller()).InstallAsync(versionId, gameDir, null, ct);
        return "服务端文件已重新下载";
    }

    /// <summary>重解压 natives：先递归删 natives 目录清残留，再从库 jar 提取 dll/so/dylib。返回处理描述。</summary>
    public static string FixNatives(string versionId, string gameDir)
    {
        var vjPath = Path.Combine(gameDir, "versions", versionId, $"{versionId}.json");
        if (!File.Exists(vjPath)) return $"版本 JSON 缺失：{vjPath}";
        var version = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(vjPath))
            ?? throw new InvalidDataException($"版本 JSON 解析失败: {versionId}");
        // Build 对 java/account 无磁盘访问，空串安全；只需 NativeJars/NativesDirectory
        var profile = new JavaArgumentsBuilder().Build(version, gameDir, "", "", "", "", 0);
        GameLaunchService.ExtractNatives(profile.NativeJars, profile.NativesDirectory, clearFirst: true);
        return $"已重新解压 {profile.NativeJars.Length} 个 natives 库";
    }
}
