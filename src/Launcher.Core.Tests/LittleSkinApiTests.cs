using System.Net;
using System.Text.Json;
using Launcher.Core.Account;

namespace Launcher.Core.Tests;

/// <summary>LittleSkin 开放 API 客户端测试（8-16 批次 51：衣柜/角色/应用皮肤 + Bearer/重试/人话错误）</summary>
public class LittleSkinApiTests
{
    private static LittleSkinApi Api(ApiStub handler, string? token = "tok-1")
        => new(new HttpClient(handler), () => token);

    [Fact]
    public async Task GetCloset_ParsesFields_AndHasMoreOnFullPage()
    {
        var json = JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(1, 20).Select(i => new
            {
                tid = i, name = $"皮肤{i}", type = "steve", hash = $"h{i}", size = 1024,
                uploader = "u", @public = true, upload_at = "2026-08-01T10:00:00+08:00",
            }),
        });
        var handler = new ApiStub(json);
        var page = await Api(handler).GetClosetAsync("skin", 1, CancellationToken.None);

        Assert.Equal(20, page.Items.Count);
        Assert.True(page.HasMore); // 满页（PageSize=20）→ 有下一页
        var first = page.Items[0];
        Assert.Equal(1, first.Tid);
        Assert.Equal("皮肤1", first.Name);
        Assert.Equal("steve", first.Type);
        Assert.Equal("h1", first.Hash);
        Assert.True(first.Public);
        Assert.Contains("Bearer tok-1", handler.Headers[0]);
        Assert.Contains("/closet?category=skin&page=1", handler.Uris[0]);
    }

    [Fact]
    public async Task GetCloset_PartialPage_HasMoreFalse()
    {
        var json = JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(1, 5).Select(i => new
            {
                tid = i, name = $"n{i}", type = "alex", hash = $"h{i}", size = 1,
                uploader = "u", @public = false,
            }),
        });
        var page = await Api(new ApiStub(json)).GetClosetAsync("skin", 1, CancellationToken.None);
        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetPlayers_Parses()
    {
        var handler = new ApiStub("""{"data":[{"pid":7,"name":"Steve"},{"pid":8,"name":"Alex"}]}""");
        var players = await Api(handler).GetPlayersAsync(CancellationToken.None);
        Assert.Equal(2, players.Count);
        Assert.Equal(7, players[0].Pid);
        Assert.Equal("Alex", players[1].Name);
    }

    [Fact]
    public async Task ApplySkin_PutMethod_BearerAndJsonBody()
    {
        var handler = new ApiStub("""{"code":0}""");
        await Api(handler).ApplySkinAsync(7, 123, CancellationToken.None);
        Assert.Equal(HttpMethod.Put, handler.Methods[0]);
        Assert.Equal("""{"skin":123}""", handler.Bodies[0]);
        Assert.Contains("Bearer tok-1", handler.Headers[0]);
        Assert.Contains("/players/7/textures", handler.Uris[0]);
    }

    [Fact]
    public async Task GetCloset_401_HumanError()
    {
        var handler = new ApiStub("""{"message":"Unauthenticated"}""", HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<LittleSkinApi.UnauthorizedException>(() =>
            Api(handler).GetClosetAsync("skin", 1, CancellationToken.None));
        Assert.Contains("授权已过期", ex.Message);
    }

    [Fact]
    public async Task GetCloset_5xx_RetriesOnce()
    {
        // 第一次 500 → 半秒后重试 → 成功；断言请求计数 2
        var handler = new ApiStub("""{"data":[]}""", HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        await Api(handler).GetClosetAsync("skin", 1, CancellationToken.None);
        Assert.Equal(2, handler.Uris.Count);
    }

    [Fact]
    public async Task ApplySkin_NoToken_HumanError()
    {
        var handler = new ApiStub("""{"data":[]}""");
        var api = Api(handler, token: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => api.ApplySkinAsync(1, 2, CancellationToken.None));
        Assert.Contains("未连接", ex.Message);
    }

    [Fact]
    public void StaticUrls_WellFormed()
    {
        Assert.Equal("https://littleskin.cn/preview/42", LittleSkinApi.PreviewUrl(42));
        // 8-19 SkinFileUrl 已删（/skin/{name}.png 实测 404 死路径，纹理走 yggdrasil profile 解析）
    }

    /// <summary>状态码序列 + 记录请求的 stub（队列空用最后一个；默认 200）</summary>
    private sealed class ApiStub : HttpMessageHandler
    {
        private readonly string _body;
        private readonly Queue<HttpStatusCode> _statuses;
        public List<string> Uris { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<string> Headers { get; } = [];
        public List<HttpMethod> Methods { get; } = [];

        public ApiStub(string body, params HttpStatusCode[] statuses)
        {
            _body = body;
            _statuses = new Queue<HttpStatusCode>(statuses.Length > 0 ? statuses : [HttpStatusCode.OK]);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri?.ToString() ?? "");
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            Headers.Add(request.Headers.TryGetValues("Authorization", out var v) ? string.Join(",", v) : "");
            Methods.Add(request.Method);
            var status = _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
            return new HttpResponseMessage(status) { Content = new StringContent(_body) };
        }
    }
}
