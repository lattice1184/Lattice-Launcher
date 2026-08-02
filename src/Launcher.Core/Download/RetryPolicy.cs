namespace Launcher.Core.Download;

/// <summary>
/// 指数退避重试（Polly 风格，无第三方依赖）：1s×2ⁿ 上限 30s。
/// 默认重试 HttpRequestException 与校验失败（InvalidDataException）；取消一律直抛。
/// </summary>
public static class RetryPolicy
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    public static TimeSpan Backoff(int attemptIndex)
        => TimeSpan.FromSeconds(Math.Min(1L << attemptIndex, (long)MaxBackoff.TotalSeconds));

    /// <summary>重试执行；attempt(attemptIndex) 从 0 开始；全部失败抛最后一次异常</summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, Task<T>> attempt,
        int maxAttempts,
        Func<Exception, bool>? shouldRetry = null,
        Func<int, TimeSpan>? backoff = null,
        CancellationToken ct = default)
    {
        shouldRetry ??= static ex => ex is HttpRequestException or InvalidDataException;
        backoff ??= Backoff;
        Exception? last = null;

        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                return await attempt(i).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (shouldRetry(ex))
            {
                last = ex;
                if (i < maxAttempts - 1)
                {
                    var delay = backoff(i);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                throw; // 不可重试 → 原样传播
            }
        }
        throw last ?? new InvalidOperationException("重试执行失败");
    }
}
