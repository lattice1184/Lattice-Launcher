using System.Net;
using System.Net.Http;
using System.Text;
using Launcher.Core.Ecosystem;
using Launcher.Core.Services;
using PCL.Core.Minecraft.ResourceProject.Curseforge;

namespace Launcher.Core.Tests;

/// <summary>依赖适配器（CurseForge）：依赖提取 / ProjectResolver 映射（离线）</summary>
public class CurseForgeAdapterTests
{
    /// <summary>按 host+path 路由 JSON</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = [];
        public void RouteJson(string hostPath, string json) => _routes[hostPath] = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = $"{request.RequestUri!.Host}{request.RequestUri.AbsolutePath}";
            if (_routes.TryGetValue(key, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static CurseforgeFile FileWithDeps(params (int ModId, int Relation)[] deps) =>
        new(7, 432, 100, true, "a", "a.jar", 1, 1, new CurseforgeHashes("h", 1),
            "https://x/a.jar", 10, ["1.21.1"],
            deps.Select(d => new CurseforgeFileDependency(d.ModId, d.Relation)).ToList());

    [Fact]
    public void ToDependencyReferences_OnlyRequired()
    {
        var refs = EcosystemDependencyAdapter.ToDependencyReferences(
            FileWithDeps((200, 1), (201, 2), (202, 3)));

        Assert.Single(refs);
        Assert.Equal("200", refs[0].ProjectId);
        Assert.Equal("curseforge", refs[0].Source);
        Assert.True(refs[0].IsRequired);
    }

    [Fact]
    public void ToDependencyReferences_NoDeps_Empty()
        => Assert.Empty(EcosystemDependencyAdapter.ToDependencyReferences(FileWithDeps()));

    [Fact]
    public void CreateResolver_NonNumeric_ReturnsNull_NoHttp()
    {
        var handler = new StubHandler();
        var svc = new CurseForgeService("k", new HttpClient(handler));
        var resolver = EcosystemDependencyAdapter.CreateResolver(svc, null);

        Assert.Null(resolver("curseforge", "not-a-number")); // 非数字直接返回，不发请求
    }

    [Fact]
    public void CreateResolver_MapsFilesFromStub()
    {
        var handler = new StubHandler();
        handler.RouteJson("api.curseforge.com/v1/mods/100/files",
            """
            {"data":[{"id":7,"gameId":432,"modId":100,"isAvailable":true,"displayName":"a","fileName":"a.jar",
            "releaseType":1,"fileStatus":1,"hashes":{"value":"h","algo":1},
            "downloadUrl":"https://x/a.jar","fileLength":10,"gameVersions":["1.21.1"],
            "dependencies":[{"modId":200,"relationType":1}]}]}
            """);
        var svc = new CurseForgeService("k", new HttpClient(handler));
        var resolver = EcosystemDependencyAdapter.CreateResolver(svc, "1.21.1");

        var project = resolver("curseforge", "100");

        Assert.NotNull(project);
        Assert.Equal("curseforge", project!.Source);
        var file = Assert.Single(project.Files);
        Assert.Equal("7", file.Id);
        Assert.Equal("a.jar", file.Version);
        Assert.Equal(1, file.ReleaseType);
        Assert.Contains("1.21.1", file.GameVersions);
        Assert.Equal("200", Assert.Single(file.RequiredDependencies).ProjectId);
    }

    [Fact]
    public void CreateResolver_DisabledService_ReturnsNull()
    {
        var resolver = EcosystemDependencyAdapter.CreateResolver(new CurseForgeService((string?)null), null);
        Assert.Null(resolver("curseforge", "100"));
    }
}
