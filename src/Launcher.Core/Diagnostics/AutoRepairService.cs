using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Launch;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Diagnostics;

/// <summary>
/// 自修复执行层（AL9）：按诊断命中的 FixKind 执行修复。
/// Redownload → VersionInstaller 幂等补全重下（缺失才下，走下载队列可见进度）；
/// ReExtractNatives → 删 natives 目录后从库 jar 重新解压（清残留 dll）。
/// </summary>
public sealed class AutoRepairService
{
    /// <summary>版本文件补全重下（client jar + libraries + assets + log4j，幂等差量）。返回任务完成状态描述。</summary>
    public static async Task<string> FixRedownloadAsync(string versionId, string gameDir)
    {
        var installer = new VersionInstaller(gameDirectory: gameDir);
        var version = await installer.GetOrFetchVersionJsonAsync(versionId, null, CancellationToken.None);
        var task = DownloadManager.Instance.EnqueueGroup($"自动修复 {versionId}",
            (ctx, ct) => installer.InstallAsync(version, ctx, ct));
        await task.Completion;
        return task.State == DownloadTaskState.Completed ? "补全完成" : $"补全未完成（{task.State}）";
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
