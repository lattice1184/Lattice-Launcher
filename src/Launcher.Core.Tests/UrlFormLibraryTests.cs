using System.Net;
using System.Net.Http;
using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>
/// AL10.1：Fabric/Forge 的 url 形式库（顶层 url，无 downloads.artifact）必须被下载——
/// 曾全部静默跳过导致 CNFE KnotClient 复现且"补全完成"虚假成功（libraries/net/fabricmc/ 整个缺失）。
/// </summary>
public class UrlFormLibraryTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public readonly List<string> Requests = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.Host + request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("12345")
            });
        }
    }

    [Fact]
    public async Task UrlFormLibrary_DownloadedFromUrlRepo()
    {
        var handler = new StubHandler();
        var gameDir = Path.Combine(Path.GetTempPath(), $"urlform-{Guid.NewGuid():N}");
        var svc = new DownloadService(new HttpClient(handler), gameDirectory: gameDir);
        // Fabric meta profile 真实结构：顶层 url，无 downloads.artifact；intermediary/fabric-loader 无 hash
        var json = """
            {
              "id":"fabric-loader-0.19.3-1.21.11","type":"release",
              "mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
              "libraries":[
                {"name":"net.fabricmc:fabric-loader:0.19.3","url":"https://maven.fabricmc.net/"},
                {"name":"net.fabricmc:intermediary:1.21.11","url":"https://maven.fabricmc.net/"}
              ]
            }
            """;
        var version = JsonSerializer.Deserialize<VersionJson>(json)!;
        var manager = new DownloadManager();
        SynchronizationContext.SetSynchronizationContext(null);
        var task = manager.EnqueueGroup("下载 fabric", (ctx, ct) =>
            new VersionDownloadPipeline(svc, DownloadOptions.Default, gameDir).RunAsync(version, ctx, ct));
        await task.Completion;

        // 文件落盘到标准库路径（核心：url 形式库必须被下载，曾全部静默跳过）
        var fl = Path.Combine(gameDir, "libraries", "net", "fabricmc", "fabric-loader", "0.19.3", "fabric-loader-0.19.3.jar");
        var il = Path.Combine(gameDir, "libraries", "net", "fabricmc", "intermediary", "1.21.11", "intermediary-1.21.11.jar");
        Assert.True(File.Exists(fl), "fabric-loader jar 应已下载");
        Assert.True(File.Exists(il), "intermediary jar 应已下载");
        // 请求走顶层 url 仓库 + Maven 坐标路径（两库并发下载，Stub 记录并发下可能丢条目 → 宽松断言；
        // 源选择非确定（官方/镜像同速），只断言 jar 被从任一源请求过）
        Assert.Contains(handler.Requests, r => r.Contains("fabric-loader-0.19.3.jar"));
    }
}
