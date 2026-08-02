using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>指数退避重试：退避序列 / 重试条件 / 取消直抛 / 次数上限</summary>
public class RetryPolicyTests
{
    [Fact]
    public void Backoff_DoublesAndCapsAt30s()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), RetryPolicy.Backoff(0));
        Assert.Equal(TimeSpan.FromSeconds(2), RetryPolicy.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryPolicy.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(30), RetryPolicy.Backoff(5));  // 32 封顶 30
        Assert.Equal(TimeSpan.FromSeconds(30), RetryPolicy.Backoff(10));
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOnHttpError_ThenSucceeds()
    {
        var calls = 0;
        var result = await RetryPolicy.ExecuteAsync<int>(async _ =>
        {
            calls++;
            if (calls < 3) throw new HttpRequestException("超时");
            return 42;
        }, maxAttempts: 3, backoff: _ => TimeSpan.Zero);

        Assert.Equal(42, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesOnInvalidData_VerificationFailure()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => RetryPolicy.ExecuteAsync<int>(async _ =>
        {
            calls++;
            throw new InvalidDataException("SHA1 不匹配");
        }, maxAttempts: 3, backoff: _ => TimeSpan.Zero));

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryable_PropagatesImmediately()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => RetryPolicy.ExecuteAsync<int>(async _ =>
        {
            calls++;
            throw new InvalidOperationException("不可重试");
        }, maxAttempts: 3, backoff: _ => TimeSpan.Zero));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RetryPolicy.ExecuteAsync<int>(
            async _ => throw new OperationCanceledException(), maxAttempts: 3, ct: cts.Token));
    }
}
