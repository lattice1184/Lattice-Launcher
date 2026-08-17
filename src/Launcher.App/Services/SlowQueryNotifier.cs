namespace Launcher.App.Services;

/// <summary>
/// 慢查询提醒（AL70c）：API 查询超过阈值未完成时 Toast 提示一次——不阻断查询，
/// 只解决「卡住无反馈」错觉（api.modrinth.com 国内实测 8.6s/次）。
/// </summary>
public static class SlowQueryNotifier
{
    public static async Task<T> WatchAsync<T>(Task<T> query, string message, TimeSpan threshold)
    {
        var delay = Task.Delay(threshold);
        var done = await Task.WhenAny(query, delay);
        if (done != query)
            NotificationService.Info(message, durationMs: 5000);
        return await query; // 传播结果/异常/取消
    }
}
