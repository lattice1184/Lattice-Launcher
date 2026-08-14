# -*- coding: utf-8 -*-
# 8-19 CurseForgeServiceTests 扩展：handler 加 RequestUrls/RouteJsonFull/RouteStatusWithBody + 8 个容错/降级测试
path = "src/Launcher.Core.Tests/CurseForgeServiceTests.cs"
src = open(path, encoding="utf-8").read()

old_handler = """        public readonly List<string> Requests = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];

        public void RouteJson(string hostPath, string json) => _routes[hostPath] = (200, Encoding.UTF8.GetBytes(json));
        public void RouteBytes(string hostPath, byte[] body) => _routes[hostPath] = (200, body);
        public void RouteStatus(string hostPath, int status) => _routes[hostPath] = (status, []);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            var hasKey = request.Headers.TryGetValues("x-api-key", out var values);
            Requests.Add($"{request.Method} {key} x-api-key={(hasKey ? values!.First() : "(none)")}");
            if (_routes.TryGetValue(key, out var route))
            {
                return Task.FromResult(route.Status == 200
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(route.Body) }
                    : new HttpResponseMessage((HttpStatusCode)route.Status));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }"""

new_handler = """        public readonly List<string> Requests = [];
        /// <summary>8-19 完整 Uri 列表（含 query——区分带/不带 gameVersion 的两次请求）</summary>
        public readonly List<string> RequestUrls = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routes = [];
        private readonly Dictionary<string, (int Status, byte[] Body)> _routesFull = [];

        public void RouteJson(string hostPath, string json) => _routes[hostPath] = (200, Encoding.UTF8.GetBytes(json));
        public void RouteBytes(string hostPath, byte[] body) => _routes[hostPath] = (200, body);
        public void RouteStatus(string hostPath, int status) => _routes[hostPath] = (status, []);
        /// <summary>8-19 按 PathAndQuery 路由（区分带/不带 gameVersion）；带 body（CF 错误 JSON 模拟）</summary>
        public void RouteJsonFull(string pathAndQuery, string json) => _routesFull[pathAndQuery] = (200, Encoding.UTF8.GetBytes(json));
        public void RouteStatusWithBody(string hostPath, int status, string json)
            => _routes[hostPath] = (status, Encoding.UTF8.GetBytes(json));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            var full = request.RequestUri!.PathAndQuery;
            var hasKey = request.Headers.TryGetValues("x-api-key", out var values);
            Requests.Add($"{request.Method} {key} x-api-key={(hasKey ? values!.First() : "(none)")}");
            RequestUrls.Add(full);
            if (_routesFull.TryGetValue(full, out var routeFull))
            {
                return Task.FromResult(routeFull.Status == 200
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(routeFull.Body) }
                    : new HttpResponseMessage((HttpStatusCode)routeFull.Status));
            }
            if (_routes.TryGetValue(key, out var route))
            {
                return Task.FromResult(route.Status == 200
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(route.Body) }
                    : new HttpResponseMessage((HttpStatusCode)route.Status));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }"""

assert src.count(old_handler) == 1, f"handler 出现 {src.count(old_handler)} 次"
src = src.replace(old_handler, new_handler)

ERR = r'{"statusCode":400,"error":"Bad Request","message":"Invalid game version parameter"}'
ERR = ERR.replace('"', '\\"')  # C# 字符串转义

new_tests = f"""
    // ---------- 8-19 容错 + 版本参数降级 ----------

    private const string CfErrorJson = "{ERR}";

    [Fact]
    public async Task SearchAsync_ErrorJsonBody_ThrowsCfApiExceptionWithMessage()
    {{
        var handler = new CfStubHandler();
        handler.RouteJson("/v1/mods/search", CfErrorJson);
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var ex = await Assert.ThrowsAsync<CurseForgeService.CurseForgeApiException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2"));
        Assert.Equal(400, ex.CfStatusCode);
        Assert.Contains("Invalid game version parameter", ex.Message);
    }}

    [Fact]
    public async Task SearchAsync_InvalidGameVersion_DowngradesOnceWithoutVersion()
    {{
        var handler = new CfStubHandler();
        handler.RouteJsonFull("/v1/mods/search?gameId=432&classId=6552&gameVersion=26.2&searchFilter=&sortField=relevancy&sortOrder=desc&pageSize=20&index=0", CfErrorJson);
        handler.RouteJson("/v1/mods/search", ProjectJson);
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var page = await svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2");

        Assert.NotNull(page);
        Assert.True(page.VersionFilterDropped);
        Assert.Equal(1, page.Projects.Count);
        Assert.Equal(2, handler.RequestUrls.Count);                     // 带版本 + 不带版本各一次
        Assert.Contains("gameVersion=26.2", handler.RequestUrls[0]);
        Assert.DoesNotContain("gameVersion", handler.RequestUrls[1]);
    }}

    [Fact]
    public async Task SearchAsync_DowngradeFailsSecondTime_ThrowsAndExactlyTwoRequests()
    {{
        var handler = new CfStubHandler();
        handler.RouteJson("/v1/mods/search", CfErrorJson);   // 两种 URL 都命中同一条路由（host+path 匹配）
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        await Assert.ThrowsAsync<CurseForgeService.CurseForgeApiException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2"));
        Assert.Equal(2, handler.RequestUrls.Count);          // 防循环：最多 2 请求
    }}

    [Fact]
    public async Task SearchAsync_Html200Body_ThrowsGenericMessage()
    {{
        var handler = new CfStubHandler();
        handler.RouteBytes("/v1/mods/search", "<html>CloudFront error</html>"u8.ToArray());
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "26.2"));
        Assert.Contains("响应格式异常", ex.Message);
    }}

    [Fact]
    public async Task GetJsonAsync_Non2xx400_WithCfErrorBody_ThrowsCfApiException()
    {{
        var handler = new CfStubHandler();
        handler.RouteStatusWithBody("/v1/mods/search", 400, CfErrorJson);
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var ex = await Assert.ThrowsAsync<CurseForgeService.CurseForgeApiException>(() =>
            svc.SearchAsync(ProjectType.Shader, gameVersion: "1.21.1"));
        Assert.Equal(400, ex.CfStatusCode);
        Assert.Contains("Invalid game version parameter", ex.Message);
    }}

    [Fact]
    public async Task GetFilesAsync_InvalidGameVersion_FallsBackToAllFiles()
    {{
        var handler = new CfStubHandler();
        handler.RouteStatusWithBody("/v1/mods/files", 400, CfErrorJson);   // 带 gameVersion 的 files 请求
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson); // 不带版本的请求
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var files = await svc.GetFilesAsync(100, "26.2");

        Assert.Single(files);
        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.DoesNotContain("gameVersion", handler.RequestUrls[1]);
    }}

    [Fact]
    public async Task FindBestFileAsync_Dropped_SelectsFromUnfilteredPool()
    {{
        var handler = new CfStubHandler();
        handler.RouteStatusWithBody("/v1/mods/files", 400, CfErrorJson);
        handler.RouteJsonFull("/v1/mods/100/files?pageSize=50", FilesJson);
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var best = await svc.FindBestFileAsync(100, "26.2");

        Assert.NotNull(best); // 降级后从全池选——不再按 26.2 过滤（否则误报「没有适配文件」）
        Assert.Equal("Sodium 0.5.11", best.DisplayName);
    }}

    [Fact]
    public async Task SearchAsync_NoGameVersion_NoFallback()
    {{
        var handler = new CfStubHandler();
        handler.RouteJson("/v1/mods/search", ProjectJson);
        var svc = new CurseForgeService(new HttpClient(handler), "test-key");

        var page = await svc.SearchAsync(ProjectType.Shader);

        Assert.NotNull(page);
        Assert.False(page.VersionFilterDropped);
        Assert.Single(handler.RequestUrls); // 无版本 → 不降级重试
    }}
"""

idx = src.rstrip().rfind("\n}")
assert idx > 0, "类闭合未找到"
src = src[:idx] + new_tests + src[idx:]
open(path, "w", encoding="utf-8", newline="\n").write(src)
print("测试已追加 OK")
