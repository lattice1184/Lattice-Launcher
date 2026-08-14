using System.Net;
using System.Text;
using Launcher.Core.Account;

namespace Launcher.Core.Tests;

/// <summary>8-13 Littleskin Yggdrasil 登录：认证解析 / textures 皮肤直链 / 错误人话</summary>
public class LittleskinAuthTests
{
    [Fact]
    public async Task Authenticate_ParsesProfileAndSkinUrl()
    {
        var texturesJson = """{"textures":{"SKIN":{"url":"https://littleskin.cn/textures/abc"}}}""";
        var texturesB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(texturesJson));
        var handler = new CapturingHandler($$$"""
        {"accessToken":"ls-at","selectedProfile":{"id":"069a79f444e94726a5befca90e38aaf5","name":"Steve","properties":[{"name":"textures","value":"{{{texturesB64}}}"}]}}
        """);
        var http = new HttpClient(handler);
        var s = await LittleskinAuth.AuthenticateAsync(http, "a@b.c", "pw", CancellationToken.None);
        Assert.Equal("069a79f444e94726a5befca90e38aaf5", s.Uuid);
        Assert.Equal("Steve", s.Name);
        Assert.Equal("https://littleskin.cn/textures/abc", s.SkinUrl);
        // 请求体断言：agent（Yggdrasil 必填）/用户名/密码/clientToken（密码仅内存传输）
        var body = handler.Body!;
        Assert.Contains("\"agent\":{\"name\":\"Minecraft\",\"version\":1}", body);
        Assert.Contains("\"username\":\"a@b.c\"", body);
        Assert.Contains("\"password\":\"pw\"", body);
        Assert.Contains("clientToken", body);
    }

    [Fact]
    public async Task Authenticate_Error_ThrowsFriendly()
    {
        var handler = new CapturingHandler(
            """{"error":"ForbiddenOperationException","errorMessage":"Invalid credentials. Invalid username or password."}""");
        var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleskinAuth.AuthenticateAsync(http, "a@b.c", "bad", CancellationToken.None));
        Assert.Contains("邮箱或密码错误", ex.Message);
    }

    [Fact]
    public async Task Authenticate_NoPlayer_GivesGuidance()
    {
        // 8-13 真机：认证通过但账号没创建角色 → 专门引导文案（而不是误报邮箱密码错误）
        var handler = new CapturingHandler(
            """{"error":"ForbiddenOperationException","errorMessage":"Missing players, please create a player and try again."}""");
        var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LittleskinAuth.AuthenticateAsync(http, "a@b.c", "pw", CancellationToken.None));
        Assert.Contains("角色", ex.Message);
        Assert.DoesNotContain("邮箱或密码", ex.Message);
    }

    [Fact]
    public async Task Authenticate_NoSkin_ReturnsNullSkinUrl()
    {
        var handler = new CapturingHandler(
            """{"accessToken":"ls-at","selectedProfile":{"id":"069a79f444e94726a5befca90e38aaf5","name":"Alex"}}""");
        var http = new HttpClient(handler);
        var s = await LittleskinAuth.AuthenticateAsync(http, "a@b.c", "pw", CancellationToken.None);
        Assert.Null(s.SkinUrl); // 无皮肤属性不炸，头像走 minotar 兜底
    }

    /// <summary>单响应捕获 handler（断言请求体用）</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string? Body { get; private set; }

        public CapturingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_response) };
        }
    }
}
