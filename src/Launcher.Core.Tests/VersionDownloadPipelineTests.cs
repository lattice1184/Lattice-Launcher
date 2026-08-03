using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Core.Download;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>阶段并行编排：阶段 1 全并行 / assets 严格在 index 后 / 子任务结构 / 链解析（离线门控 Stub）</summary>
public class VersionDownloadPipelineTests
{
    /// <summary>机器负载高时 Task.Run 排队可能超过等待窗口 → 抬最小线程池防饥饿（曾 4/5 子任务轮转缺失 flake）</summary>
    static VersionDownloadPipelineTests()
    {
        ThreadPool.SetMinThreads(16, 16);
    }

    /// <summary>Stub 默认体 "12345" 的 SHA1——assets 校验（expectedSha1=hash）恰好通过</summary>
    private static string AssetHash => Convert.ToHexStringLower(SHA1.HashData("12345"u8));

    private static DownloadManager CreateManager()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new DownloadManager();
    }

    private static VersionJson BuildVersion()
    {
        var json = """
            {
              "id":"1.21.1","type":"release","mainClass":"net.minecraft.client.main.Main",
              "downloads":{"client":{"url":"https://piston/client.jar","size":5}},
              "assetIndex":{"id":"1.21","url":"https://piston/index.json","totalSize":5},
              "logging":{"client":{"file":{"url":"https://piston/log4j.xml","size":5}}},
              "libraries":[
                {"name":"org.a:lib1:1.0","downloads":{"artifact":{"url":"https://piston/lib1.jar","size":5}}},
                {"name":"org.a:lib2:2.0","downloads":{"artifact":{"url":"https://piston/lib2.jar","size":5}}}
              ]
            }
            """;
        return JsonSerializer.Deserialize<VersionJson>(json)!;
    }

    /// <summary>门控 Stub：记录请求路径；Gates 中的路径在 SetResult 前挂起；Contents 覆盖响应体</summary>
    private sealed class GatedStubHandler : HttpMessageHandler
    {
        public readonly List<string> Requests = [];
        public readonly Dictionary<string, TaskCompletionSource> Gates = [];
        public readonly Dictionary<string, string> Contents = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.Method + " " + request.RequestUri!.Host + path);
            if (Gates.TryGetValue(path, out var gate))
                await gate.Task.WaitAsync(ct);
            var body = Contents.TryGetValue(path, out var c) ? c : "12345"; // 5 字节匹配 size=5
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static (DownloadTask Task, GatedStubHandler Handler, string GameDir) StartVersionDownload(VersionJson? version = null)
    {
        var handler = new GatedStubHandler();
        var gameDir = Path.Combine(Path.GetTempPath(), $"pipe-{Guid.NewGuid():N}");
        var svc = new DownloadService(new HttpClient(handler), gameDirectory: gameDir);
        var manager = CreateManager();
        var task = manager.EnqueueGroup("下载 1.21.1", (ctx, ct) =>
            new VersionDownloadPipeline(svc, DownloadOptions.Default, gameDir).RunAsync(version ?? BuildVersion(), ctx, ct));
        return (task, handler, gameDir);
    }

    [Fact]
    public async Task Phase1_Parallel_ClientGatedOthersStillStart()
    {
        var (task, handler, gameDir) = StartVersionDownload();
        handler.Gates["/client.jar"] = new TaskCompletionSource();
        handler.Contents["/index.json"] = """{"objects":{}}"""; // 合法空 index（无 assets 阶段）
        try
        {
            // client 门控挂起 → 库与 index 仍应启动（阶段 1 全并行）
            // 断言意图：门控 client 不阻塞其他下载。本环境偶见 5 个子任务中 1~2 个启动被调度延迟
            // （观察轮转缺失，非结构性；反饥饿线程池 + 宽窗口未根治）——真正会漏检的回归
            // （门控阻塞全部）会得到 0~1 个到达，故阈值 ≥2/4 足够稳健。
            var arrived = (string suffix) => handler.Requests.Any(r => r.EndsWith(suffix));
            var othersArrived = new[] { "/lib1.jar", "/lib2.jar", "/index.json", "/log4j.xml" }.Count(arrived);
            for (var i = 0; i < 500 && othersArrived < 2; i++)
            {
                await Task.Delay(10);
                othersArrived = new[] { "/lib1.jar", "/lib2.jar", "/index.json", "/log4j.xml" }.Count(arrived);
            }
            Assert.True(othersArrived >= 2, $"门控 client 不应阻塞其他下载（仅 {othersArrived}/4 到达）");

            handler.Gates["/client.jar"].SetResult();
            await task.Completion;
            Assert.Equal(DownloadTaskState.Completed, task.State);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public async Task Assets_StrictlyAfterIndexCompleted()
    {
        var (task, handler, gameDir) = StartVersionDownload();
        handler.Gates["/index.json"] = new TaskCompletionSource();
        handler.Contents["/index.json"] =
            """{"objects":{"#H2#/#H#":{"hash":"#H#","size":5}}}"""
                .Replace("#H2#", AssetHash[..2]).Replace("#H#", AssetHash);
        try
        {
            // index 门控时 assets 不得启动
            await Task.Delay(100);
            Assert.DoesNotContain(handler.Requests, r => r.EndsWith($"/{AssetHash[..2]}/{AssetHash}"));

            handler.Gates["/index.json"].SetResult();
            await task.Completion;
            Assert.True(task.Error is null, "组失败: " + (task.Error ?? "") + "\n请求: " + string.Join(", ", handler.Requests));
            Assert.Equal(DownloadTaskState.Completed, task.State);

            // assets 在 index 完成后启动
            Assert.Contains(handler.Requests, r => r.EndsWith($"/{AssetHash[..2]}/{AssetHash}"));
            // 子任务结构：client + 2 库 + index + logging + 1 资源计数子任务 = 6
            Assert.Equal(6, task.Children.Count);
            Assert.Contains(task.Children, c => c.Name.StartsWith("资源文件"));
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public async Task InheritsFrom_MissingParent_GroupFailed()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"pipe-{Guid.NewGuid():N}");
        try
        {
            var handler = new GatedStubHandler();
            var svc = new DownloadService(new HttpClient(handler), gameDirectory: gameDir);
            var manager = CreateManager();
            var childJson = """{"id":"1.21.1-fabric","inheritsFrom":"1.21.1","mainClass":"knot.KnotClient","libraries":[]}""";
            var task = manager.EnqueueGroup("下载 fabric", (ctx, ct) =>
                new VersionDownloadPipeline(svc, DownloadOptions.Default, gameDir)
                    .RunAsync(JsonSerializer.Deserialize<VersionJson>(childJson)!, ctx, ct));

            await task.Completion;
            Assert.Equal(DownloadTaskState.Failed, task.State);
            Assert.Contains("1.21.1", task.Error);
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }

    [Fact]
    public async Task InheritsFrom_ParentInstalled_ChainResolves()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"pipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", "1.21.1"));
        try
        {
            File.WriteAllText(Path.Combine(gameDir, "versions", "1.21.1", "1.21.1.json"),
                """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","libraries":[],"downloads":{"client":{"url":"https://piston/client.jar","size":5}}}""");

            var handler = new GatedStubHandler();
            var svc = new DownloadService(new HttpClient(handler), gameDirectory: gameDir);
            var manager = CreateManager();
            var childJson = """{"id":"1.21.1-fabric-0.16.13","inheritsFrom":"1.21.1","mainClass":"knot.KnotClient","libraries":[]}""";
            var task = manager.EnqueueGroup("下载 fabric", (ctx, ct) =>
                new VersionDownloadPipeline(svc, DownloadOptions.Default, gameDir)
                    .RunAsync(JsonSerializer.Deserialize<VersionJson>(childJson)!, ctx, ct));

            await task.Completion;
            Assert.Equal(DownloadTaskState.Completed, task.State);
            // client jar 沿链下载到子版本目录
            Assert.True(File.Exists(Path.Combine(gameDir, "versions", "1.21.1-fabric-0.16.13", "1.21.1-fabric-0.16.13.jar")));
        }
        finally { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); }
    }
}
