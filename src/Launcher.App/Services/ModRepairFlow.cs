using Avalonia.Controls;
using Launcher.Core.Diagnostics;
using Launcher.Core.Download;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.App.Services;

/// <summary>
/// 模组缺失自愈统一入口（AL57）：扫描实例日志 → 有缺失则确认框 → 下载中心补全 → 结果 Toast。
/// CrashReportWindow 一键修复 / 启动失败自动修复 / 版本页手动修复 三处共用。
/// </summary>
public static class ModRepairFlow
{
    /// <summary>扫描实例日志并补全缺失前置；返回是否检测到缺失（处理与否无关）。
    /// requireConfirm=false（启动失败自动修复路径）：无人值守直接补全，不弹框——避免与崩溃窗模态冲突，
    /// 且符合「自动」语义（用户要求的就是全自动）。主动路径（修复按钮）保留确认。</summary>
    public static async Task<bool> TryRepairAsync(string gameDir, string instanceId, Window? owner, bool requireConfirm = true)
    {
        var missing = ModRepairService.ScanInstanceLogs(gameDir, instanceId);
        if (missing.Count == 0) return false;
        var list = string.Join("、", missing.Take(5)) + (missing.Count > 5 ? "…" : "");
        if (requireConfirm)
        {
            if (owner is null || !await DialogService.Confirm(owner,
                    $"你缺了前置模组：{list}。要自动从 Modrinth 补全吗？", "模组自动修复", "自动补全", "暂不"))
                return true;
        }

        var repair = new ModRepairService();
        ModRepairReport? rpt = null;
        var gv = EcosystemService.TryParseGameVersion(instanceId, out var v) ? v : null;
        var loader = EcosystemService.GuessLoader(instanceId);
        var task = DownloadManager.Instance.EnqueueGroup($"修复模组 {instanceId}", async (ctx, ct) =>
        {
            rpt = await repair.RepairAsync(missing, gameDir, instanceId, gv, loader, ctx, ct);
        });
        await task.Completion;
        if (task.State != DownloadTaskState.Completed) return true;
        if (rpt is { Repaired.Count: > 0 })
            NotificationService.Success(
                $"已补全 {rpt.Repaired.Count} 个缺失前置：{string.Join("、", rpt.Repaired.Take(3))}" +
                (rpt.Repaired.Count > 3 ? "…" : ""), 4500);
        if (rpt is { Failed.Count: > 0 })
            NotificationService.Error($"补全失败：{string.Join("、", rpt.Failed.Select(f => $"{f.ModId}（{f.Reason}）"))}");
        return true;
    }
}
