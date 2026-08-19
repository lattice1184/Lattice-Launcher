namespace Launcher.App.Services;

/// <summary>
/// 慢查询提醒（AL70c）：API 查询超过阈值未完成时 Toast 提示一次——不阻断查询，
/// 只解决「卡住无反馈」错觉（api.modrinth.com 国内实测 8.6s/次）。
/// 8-19 第二批：会话级冷却——同一文案 60s 内只弹一次（多个并行查询同时慢只提示一条，
/// 其余由 NotificationService 折叠兜底）。
/// </summary>
public static class SlowQueryNotifier
{
    /// <summary>message → 上次弹 toast 的 TickCount64（会话级冷却）</summary>
    private static readonly Dictionary<string, long> _lastShown = new();
    private const long CooldownMs = 60_000;

    public static async Task<T> WatchAsync<T>(Task<T> query, string message, TimeSpan threshold)
    {
        var delay = Task.Delay(threshold);
        var done = await Task.WhenAny(query, delay);
        if (done != query)
        {
            var now = Environment.TickCount64;
            if (_lastShown.TryGetValue(message, out var last) && now - last < CooldownMs)
            {
                // 冷却中：不弹（折叠与冷却双层抑制，同批慢查询只提示一次）
            }
            else
            {
                _lastShown[message] = now;
                NotificationService.Info(message, durationMs: 5000);
            }
        }
        return await query; // 传播结果/异常/取消
    }
}
