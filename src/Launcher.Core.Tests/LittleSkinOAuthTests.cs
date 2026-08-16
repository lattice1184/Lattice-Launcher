using System.Net;
using Launcher.Core.Account;

namespace Launcher.Core.Tests;

/// <summary>LittleSkin 设备码流测试（8-16 批次 51：发起/轮询/刷新 + 人话错误）</summary>
public class LittleSkinOAuthTests
{
    private const string ClientId = "test-client-1";

    private static readonly HttpClient OkHttp = new(new SequenceStub(HttpStatusCode.OK, """{"error":"authorization_pending"}"""));
    private static readonly HttpClient ErrHttp = new(new SequenceStub(HttpStatusCode.OK, """{"error":"invalid_client","error_description":"Client was not found or not whitelisted"}"""));

    // ---------- 发起设备码 ----------

    [Fact]
    public async Task StartDeviceCode_ParsesSession_AndSendsScope()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """
            {"user_code":"ABCD-EFGH","device_code":"dev-123","verification_uri":"https://open.littleskin.cn/oauth/link",
             "verification_uri_complete":"https://open.littleskin.cn/oauth/link?user_code=ABCD-EFGH","interval":5,"expires_in":600}
            """);
        var session = await LittleSkinOAuth.StartDeviceCodeAsync(new HttpClient(handler), ClientId, CancellationToken.None);

        Assert.Equal("ABCD-EFGH", session.UserCode);
        Assert.Equal("dev-123", session.DeviceCode);
        Assert.Contains("ABCD-EFGH", session.VerificationUriComplete);
        Assert.Equal(5, session.IntervalSec);
        Assert.Equal(600, session.ExpiresInSec);
        var body = handler.Bodies[0];
        Assert.Contains("client_id=" + ClientId, body);
        Assert.Contains("scope=" + LittleSkinOAuth.ConnectScope.Replace(" ", "+"), body); // FormUrlEncoded 空格 → +（含 offline_access）
        Assert.Equal(LittleSkinOAuth.DeviceCodeUrl, new Uri(handler.Uris[0]).GetLeftPart(System.UriPartial.Path));
    }

    [Fact]
    public async Task StartDeviceCode_MissingOptional_AppliesDefaults()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"user_code":"AB12","device_code":"d"}""");
        var session = await LittleSkinOAuth.StartDeviceCodeAsync(new HttpClient(handler), ClientId, CancellationToken.None);
        Assert.Equal(5, session.IntervalSec);   // 缺省 5s
        Assert.Equal(900, session.ExpiresInSec); // 缺省 900s
    }

    [Fact]
    public async Task StartDeviceCode_InvalidClient_HumanError()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleSkinOAuth.StartDeviceCodeAsync(ErrHttp, ClientId, CancellationToken.None));
        Assert.Contains("client_id", ex.Message);
    }

    [Fact]
    public async Task StartDeviceCode_OtherError_CarriesCode()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleSkinOAuth.StartDeviceCodeAsync(new HttpClient(new SequenceStub(HttpStatusCode.OK, """{"error":"internal","error_description":"boom"}""")), ClientId, CancellationToken.None));
        Assert.Contains("internal", ex.Message);
    }

    // ---------- 轮询 ----------

    [Fact]
    public async Task Poll_PendingThenSuccess_ReturnsTokens_AndTickFired()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"error":"authorization_pending"}""", """{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600}""");
        var ticks = new List<string>();
        var session = NewSession(ExpiresInSec: 600);
        var tokens = await LittleSkinOAuth.PollDeviceCodeAsync(new HttpClient(handler), ClientId, session, ticks.Add, CancellationToken.None);

        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Equal("rt-1", tokens.RefreshToken);
        Assert.NotEmpty(ticks); // 等待状态回调触发过
        Assert.Contains("grant_type=urn:ietf:params:oauth:grant-type:device_code", handler.Bodies[1].Replace("%3A", ":")); // 冒号被编码，归一化后断言
        Assert.Contains("device_code=dev-1", handler.Bodies[1]);
    }

    [Fact]
    public async Task Poll_AccessDenied_HumanError()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"error":"access_denied","error_description":"denied"}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleSkinOAuth.PollDeviceCodeAsync(new HttpClient(handler), ClientId, NewSession(600), null, CancellationToken.None));
        Assert.Contains("拒绝", ex.Message);
    }

    [Fact]
    public async Task Poll_ExpiredToken_HumanError()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"error":"expired_token"}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleSkinOAuth.PollDeviceCodeAsync(new HttpClient(handler), ClientId, NewSession(600), null, CancellationToken.None));
        Assert.Contains("过期", ex.Message);
    }

    [Fact]
    public async Task Poll_Timeout_ThrowsTimeout()
    {
        // ExpiresInSec=1：第一轮请求后 delay 结束即超时
        var handler = new SequenceStub(HttpStatusCode.OK, """{"error":"authorization_pending"}""");
        await Assert.ThrowsAsync<TimeoutException>(() =>
            LittleSkinOAuth.PollDeviceCodeAsync(new HttpClient(handler), ClientId, NewSession(ExpiresInSec: 1), null, CancellationToken.None));
    }

    [Fact]
    public async Task Poll_Cancel_ThrowsCancellation()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"error":"authorization_pending"}""");
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LittleSkinOAuth.PollDeviceCodeAsync(new HttpClient(handler), ClientId, NewSession(600), null, cts.Token));
    }

    // ---------- 刷新 ----------

    [Fact]
    public async Task Refresh_Success_ReturnsNewTokens()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"access_token":"at-2","refresh_token":"rt-2","expires_in":7200}""");
        var tokens = await LittleSkinOAuth.RefreshAsync(new HttpClient(handler), ClientId, "old-rt", CancellationToken.None);
        Assert.Equal("at-2", tokens.AccessToken);
        Assert.Equal("rt-2", tokens.RefreshToken);
        Assert.Contains("refresh_token=old-rt", handler.Bodies[0]);
    }

    [Fact]
    public async Task Refresh_NoNewRefreshToken_KeepsOld()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"access_token":"at-3"}""");
        var tokens = await LittleSkinOAuth.RefreshAsync(new HttpClient(handler), ClientId, "old-rt", CancellationToken.None);
        Assert.Equal("old-rt", tokens.RefreshToken); // 轮换缺失 → 沿用
    }

    [Fact]
    public async Task Refresh_InvalidGrant_HumanError()
    {
        var handler = new SequenceStub(HttpStatusCode.OK, """{"error":"invalid_grant","error_description":"refresh token expired"}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleSkinOAuth.RefreshAsync(new HttpClient(handler), ClientId, "dead-rt", CancellationToken.None));
        Assert.Contains("重新连接", ex.Message);
    }

    // ---------- 工具 ----------

    private static LittleSkinOAuth.DeviceCodeSession NewSession(int ExpiresInSec)
        => new("CODE", "dev-1", "https://open.littleskin.cn/oauth/link", "https://open.littleskin.cn/oauth/link?user_code=CODE", 3, ExpiresInSec);

    /// <summary>按序回放 JSON 响应（队列空 → 默认 authorization_pending）；记录 URI + body</summary>
    private sealed class SequenceStub : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();
        private readonly HttpStatusCode _status;
        public List<string> Uris { get; } = [];
        public List<string> Bodies { get; } = [];

        public SequenceStub(HttpStatusCode status, params string[] responses)
        {
            _status = status;
            foreach (var r in responses) _responses.Enqueue(r);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri?.ToString() ?? "");
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responses.Count > 0 ? _responses.Dequeue() : """{"error":"authorization_pending"}"""),
            };
        }
    }
}
