using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Launch;
using Launcher.Core.Model.Mojang;
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

    /// <summary>校验版本文件完整性：client jar + 全部 libraries 本地存在；返回缺失清单（空 = 完整）</summary>
    public static List<string> VerifyFiles(VersionJson merged, string gameDir)
    {
        var missing = new List<string>();
        var clientPath = Path.Combine(gameDir, "versions", merged.Id, $"{merged.Id}.jar");
        if (!File.Exists(clientPath)) missing.Add(clientPath);
        var librariesDir = Path.Combine(gameDir, "libraries");
        foreach (var lib in merged.Libraries ?? [])
        {
            var p = Path.Combine(librariesDir, MavenPath.FullPath(lib.Name));
            if (!File.Exists(p)) missing.Add(p);
        }
        return missing;
    }

    /// <summary>读磁盘父版本 json（inheritsFrom 链解析用）；缺失/损坏返回 null</summary>
    private static VersionJson? LoadParentJson(string gameDir, string id)
    {
        var p = Path.Combine(gameDir, "versions", id, $"{id}.json");
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(p)); }
        catch { return null; }
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
