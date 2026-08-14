using Launcher.App.ViewModels;
using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.App.Services;

/// <summary>
/// 整合包导入统一入口（AL47）：版本页按钮 / 窗口拖拽 / 在线下载完成 三处共用。
/// 解析 → 确认框 → 全局下载中心组任务（进度/暂停/重试现成）→ 完成 Toast + 版本页刷新选中。
/// </summary>
public static class ModpackImportFlow
{
    public static async void StartAsync(string zipPath)
    {
        var owner = DialogService.MainWindow();
        try
        {
            var info = ModpackImporter.Parse(zipPath, out var reason);
            if (info is null)
            {
                NotificationService.Error(reason ?? "不支持的整合包格式");
                return;
            }
            if (owner is not null && !await DialogService.Confirm(owner,
                    BuildConfirmText(info), "导入整合包", "导入", "取消"))
                return;

            ModpackImportReport? report = null;
            var task = DownloadManager.Instance.EnqueueGroup($"导入整合包 {info.VersionId}", async (ctx, ct) =>
            {
                report = await new ModpackInstaller()
                    .ImportAsync(zipPath, GameDirectory.InstallDir(), ctx, ct);
            });
            // 自动跳下载板块；完成后跳回版本页并选中新实例
            MainViewModel.Current?.NavigateToDownloadQueue("version");
            await task.Completion;
            if (task.State == DownloadTaskState.Completed && report is not null)
            {
                var skip = report.ModsSkipped > 0
                    ? $"（跳过 {report.ModsSkipped} 项：{string.Join("、", report.Skipped.Take(3).Select(s => s.Name))}）"
                    : "";
                NotificationService.Success($"整合包已导入：{report.PackId}{skip}");
                if (MainViewModel.Current is { } main)
                {
                    await main.Versions.LoadAsync();
                    main.Versions.SelectById(report.PackId);
                }
            }
            else
            {
                NotificationService.Error(task.Error ?? "导入失败");
            }
        }
        catch (Exception ex)
        {
            NotificationService.Error($"导入失败: {ex.Message}");
        }
    }

    private static string BuildConfirmText(ModpackImportInfo info)
    {
        var lines = new List<string>
        {
            $"整合包：{info.VersionId}",
            $"Minecraft：{info.McVersion}",
        };
        if (info.Loader is not null)
            lines.Add($"加载器：{info.Loader}{(info.LoaderVersion is null ? "" : $" {info.LoaderVersion}")}");
        lines.Add(info.Format == ModpackFormat.Modrinth
            ? $"模组：{info.FileCount} 个（在线下载）"
            : $"文件：{info.FileCount} 个");
        lines.Add("");
        lines.Add("导入会创建能启动的版本实例，并下载原版与加载器文件。文件有几百 MB。");
        return string.Join("\n", lines);
    }
}
