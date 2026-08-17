using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Launcher.Core.Download;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>整合包安装编排（AL47）：mrpack 直链下载 / CF zip 解压 / API 兜底 / 无 key 报错（全部离线 Stub）</summary>
public class ModpackInstallerTests
{
    private const string VanillaJson = """
        {"id":"1.21.10","mainClass":"net.minecraft.client.main.Main","libraries":[],
         "downloads":{"client":{"url":"https://piston/1.21.10/client.jar","size":5}}}
        """;

    private const string ManifestJson = """
        {"latest":{"release":"1.21.10"},"versions":[
          {"id":"1.21.10","type":"release","url":"https://piston/mc/1.21.10.json","releaseTime":"2026-01-01T00:00:00Z"}]}
        """;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes;

        public StubHandler(Dictionary<string, string> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = _routes.TryGetValue(path, out var json) ? json : "12345"; // 5 字节，匹配 size=5 校验
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8),
            });
        }
    }

    private static string MakeZip(string dir, string name, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, name);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, content) in files)
        {
            var e = zip.CreateEntry(path);
            using var sw = new StreamWriter(e.Open(), Encoding.UTF8);
            sw.Write(content);
        }
        return zipPath;
    }

    private static (string GameDir, string CacheDir) TempDirs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mpk-{Guid.NewGuid():N}");
        return (Path.Combine(root, "game"), Path.Combine(root, "cache"));
    }

    [Fact]
    public async Task Mrpack_Import_DownloadsMods_Overrides_RewritesId_Marks()
    {
        var (gameDir, cacheDir) = TempDirs();
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "pack.mrpack",
                ("modrinth.index.json", """{"formatVersion":1,"name":"MR包","dependencies":{"minecraft":"1.21.10","fabric-loader":"0.15.0"},"files":[{"path":"mods/alpha.jar","hashes":{"sha1":"c1fe3a7b487f66a6ac8c7e4794bc55c31b0ef403"},"downloads":["https://cdn.example/alpha.jar"],"fileSize":5}]}"""),
                ("overrides/config/opt.txt", "opt"));

            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson, // 官方清单（stub）
                ["/mc/1.21.10.json"] = VanillaJson,                  // 父版本 json
                ["/alpha.jar"] = "AAAAA",                            // mods 直链
                // Fabric meta（声明版本 0.15.0）：版本列表 + profile json
                ["/v2/versions/loader/1.21.10"] = """[{"loader":{"version":"0.15.0","stable":true}}]""",
                ["/v2/versions/loader/1.21.10/0.15.0/profile/json"] =
                    """{"id":"fabric-loader-0.15.0-1.21.10","inheritsFrom":"1.21.10","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","libraries":[],"releaseTime":"2026-01-01T00:00:00Z","type":"release"}""",
            };
            var http = new HttpClient(new StubHandler(routes));
            var ds = new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true)); // networkChecker stub
            var installer = new ModpackInstaller(http, ds, gameDir, curseForgeApiBase: null, manifestCacheDir: cacheDir);

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入整合包 MR包", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;

            if (task.State != DownloadTaskState.Completed) Assert.Fail($"state={task.State} err={task.Error}");
            var vdir = Path.Combine(gameDir, "versions", "MR包");
            Assert.True(File.Exists(Path.Combine(vdir, "mods", "alpha.jar")));       // mods 直链落位
            Assert.True(File.Exists(Path.Combine(vdir, "config", "opt.txt")));       // overrides 去前缀
            Assert.False(File.Exists(Path.Combine(vdir, "overrides", "config", "opt.txt")));
            var json = System.Text.Json.JsonSerializer.Deserialize<Launcher.Core.Model.Mojang.VersionJson>(
                File.ReadAllText(Path.Combine(vdir, "MR包.json")));
            Assert.Equal("MR包", json!.Id);                                          // json id 重写
            Assert.True(InstallMarker.IsMarked(gameDir, "MR包"));
            Assert.True(InstallMarker.IsPrefetched(gameDir, "1.21.10"));             // 父版本预取标记
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true); }
    }

    [Fact]
    public async Task CurseForge_Import_ExtractsZip_WithOverrides()
    {
        var (gameDir, cacheDir) = TempDirs();
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "cf.zip",
                ("manifest.json", """{"minecraft":{"version":"1.21.10","modLoaders":[]},"manifestType":"minecraftModpack","manifestVersion":1,"name":"CF包","version":"1.0","files":[],"overrides":"overrides"}"""),
                ("mods/sodium.jar", "SODIUM"),
                ("overrides/config/opt.txt", "opt"),
                ("clientoverrides/options.txt", "opts"),
                ("modlist.html", "<html>"));

            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson,
                ["/mc/1.21.10.json"] = VanillaJson,
            };
            var http = new HttpClient(new StubHandler(routes));
            var installer = new ModpackInstaller(http, new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true)),
                gameDir, curseForgeApiBase: null, manifestCacheDir: cacheDir);

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入整合包 CF包", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;

            if (task.State != DownloadTaskState.Completed) Assert.Fail($"state={task.State} err={task.Error}");
            var vdir = Path.Combine(gameDir, "versions", "CF包");
            Assert.True(File.Exists(Path.Combine(vdir, "mods", "sodium.jar")));       // jar 实体解压
            Assert.True(File.Exists(Path.Combine(vdir, "config", "opt.txt")));        // overrides 去前缀
            Assert.True(File.Exists(Path.Combine(vdir, "options.txt")));              // clientoverrides 去前缀
            Assert.False(File.Exists(Path.Combine(vdir, "manifest.json")));           // 清单不入库
            Assert.False(File.Exists(Path.Combine(vdir, "modlist.html")));            // modlist 跳过
            Assert.True(InstallMarker.IsMarked(gameDir, "CF包"));
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true); }
    }

    [Fact]
    public async Task CurseForge_NoJars_NoKey_ThrowsClearError()
    {
        var (gameDir, cacheDir) = TempDirs();
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "cf-empty.zip",
                ("manifest.json", """{"minecraft":{"version":"1.21.10","modLoaders":[]},"manifestType":"minecraftModpack","manifestVersion":1,"name":"空包","version":"1.0","files":[{"projectID":100,"fileID":200,"required":true}],"overrides":"overrides"}"""));

            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson,
                ["/mc/1.21.10.json"] = VanillaJson,
            };
            var http = new HttpClient(new StubHandler(routes));
            // curseForgeApiKey: "" 显式禁用——不读真实 settings.json（用户机器上已迁移真实 key，否则 IsEnabled 误判）
            var installer = new ModpackInstaller(http, new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true)),
                gameDir, curseForgeApiBase: null, manifestCacheDir: cacheDir, curseForgeApiKey: "");

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入整合包 空包", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;

            Assert.Equal(DownloadTaskState.Failed, task.State);
            Assert.Contains("API Key", task.Error ?? ""); // 明确报错（缺 jar + 无 key）
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true); }
    }

    [Fact]
    public async Task CurseForge_NoJars_ApiFallback_Downloads()
    {
        var (gameDir, cacheDir) = TempDirs();
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "test-key"); // CurseForgeService 直连模式读环境变量
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "cf-api.zip",
                ("manifest.json", """{"minecraft":{"version":"1.21.10","modLoaders":[]},"manifestType":"minecraftModpack","manifestVersion":1,"name":"API包","version":"1.0","files":[{"projectID":100,"fileID":200,"required":true}],"overrides":"overrides"}"""));

            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson,
                ["/mc/1.21.10.json"] = VanillaJson,
                // CF API 兜底：单文件详情（返回 downloadUrl 指向 stub CDN）
                ["/v1/mods/100/files/200"] = """{"data":{"id":200,"gameId":432,"modId":100,"isAvailable":true,"displayName":"Sodium","fileName":"sodium-api.jar","releaseType":1,"fileStatus":1,"hashes":[{"value":"4601044687c40f1a23385d338d02f9fc7f5d512d","algo":1}],"downloadUrl":"https://cdn.example/sodium-api.jar","fileLength":5}}""",
                ["/sodium-api.jar"] = "BBBBB",
            };
            var http = new HttpClient(new StubHandler(routes));
            var installer = new ModpackInstaller(http, new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true)),
                gameDir, curseForgeApiBase: CurseForgeService.ApiBase, manifestCacheDir: cacheDir);

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入整合包 API包", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;

            if (task.State != DownloadTaskState.Completed) Assert.Fail($"state={task.State} err={task.Error}");
            var vdir = Path.Combine(gameDir, "versions", "API包");
            Assert.True(File.Exists(Path.Combine(vdir, "mods", "sodium-api.jar"))); // API 兜底下载落位
            Assert.True(InstallMarker.IsMarked(gameDir, "API包"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null);
            if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true);
        }
    }

    [Fact]
    public async Task Own_Zip_Content_Lands_OnDisk()
    {
        // REVIEW-C：自家 ZIP 格式导入内容必须落盘（旧代码返回 (0,[]) 永不解压，mods/config 静默丢失）
        var (gameDir, cacheDir) = TempDirs();
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "pack.zip",
                ("manifest.json", """{"name":"own-pack","mcVersion":"1.21.10","loader":"fabric","fileCount":2}"""),
                ("mods/a.jar", "JAR"), ("config/x.txt", "X"));
            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson,
                ["/mc/1.21.10.json"] = VanillaJson,
                ["/v2/versions/loader/1.21.10"] = """[{"loader":{"version":"0.15.0","stable":true}}]""",
                ["/v2/versions/loader/1.21.10/0.15.0/profile/json"] =
                    """{"id":"fabric-loader-0.15.0-1.21.10","inheritsFrom":"1.21.10","mainClass":"x","libraries":[],"releaseTime":"2026-01-01T00:00:00Z","type":"release"}""",
            };
            var http = new HttpClient(new StubHandler(routes));
            var ds = new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true));
            var installer = new ModpackInstaller(http, ds, gameDir, curseForgeApiBase: null, manifestCacheDir: cacheDir);

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入 own-pack", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;
            if (task.State != DownloadTaskState.Completed) Assert.Fail($"state={task.State} err={task.Error}");

            var vdir = Path.Combine(gameDir, "versions", "own-pack");
            Assert.True(File.Exists(Path.Combine(vdir, "mods", "a.jar")), "mods 内容必须落盘");
            Assert.True(File.Exists(Path.Combine(vdir, "config", "x.txt")), "config 内容必须落盘");
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true); }
    }

    [Fact]
    public async Task Mrpack_PathTraversal_IsSkipped()
    {
        // REVIEW-C：files[].path 路径穿越防护——../ 越界条目跳过且不落盘
        var (gameDir, cacheDir) = TempDirs();
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "evil.mrpack",
                ("modrinth.index.json", """{"formatVersion":1,"name":"evil","dependencies":{"minecraft":"1.21.10"},"files":[{"path":"../evil.txt","downloads":["https://cdn.example/evil.txt"],"fileSize":3}]}"""));
            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson,
                ["/mc/1.21.10.json"] = VanillaJson,
                ["/evil.txt"] = "EVIL",
            };
            var http = new HttpClient(new StubHandler(routes));
            var ds = new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true));
            var installer = new ModpackInstaller(http, ds, gameDir, curseForgeApiBase: null, manifestCacheDir: cacheDir);

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入 evil", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;
            if (task.State != DownloadTaskState.Completed) Assert.Fail($"state={task.State} err={task.Error}");

            Assert.False(File.Exists(Path.Combine(gameDir, "evil.txt")), "越界路径必须被跳过");
            var vdir = Path.Combine(gameDir, "versions", "evil");
            Assert.False(File.Exists(Path.Combine(vdir, "evil.txt")), "版本目录内也不得出现该文件");
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true); }
    }

    [Fact]
    public async Task Mrpack_NoDownloads_Sha1Fallback_ResolvesUrl()
    {
        // REVIEW-C：files.downloads 为空（自导自入）→ 按 sha1 反查 Modrinth 补直链，不再全部跳过
        var (gameDir, cacheDir) = TempDirs();
        try
        {
            var dir = Path.GetDirectoryName(gameDir)!;
            var zip = MakeZip(dir, "nourl.mrpack",
                ("modrinth.index.json", """{"formatVersion":1,"name":"nourl","dependencies":{"minecraft":"1.21.10"},"files":[{"path":"mods/alpha.jar","hashes":{"sha1":"c1fe3a7b487f66a6ac8c7e4794bc55c31b0ef403"},"fileSize":5}]}"""));
            var routes = new Dictionary<string, string>
            {
                ["/mc/game/version_manifest_v2.json"] = ManifestJson,
                ["/mc/1.21.10.json"] = VanillaJson,
                ["/v2/version_file/c1fe3a7b487f66a6ac8c7e4794bc55c31b0ef403"] =
                    """{"files":[{"url":"https://cdn.example/alpha.jar"}]}""",
                ["/alpha.jar"] = "AAAAA",
            };
            var http = new HttpClient(new StubHandler(routes));
            var ds = new DownloadService(http, null, DownloadOptions.FromSettings(new LauncherSettings()), gameDir, (_, _) => Task.FromResult(true));
            var installer = new ModpackInstaller(http, ds, gameDir, curseForgeApiBase: null, manifestCacheDir: cacheDir);

            var mgr = new DownloadManager(null);
            var task = mgr.EnqueueGroup("导入 nourl", (ctx, ct) => installer.ImportAsync(zip, gameDir, ctx, ct));
            await task.Completion;
            if (task.State != DownloadTaskState.Completed) Assert.Fail($"state={task.State} err={task.Error}");

            Assert.True(File.Exists(Path.Combine(gameDir, "versions", "nourl", "mods", "alpha.jar")),
                "无直链文件必须经 sha1 反查后下载落盘");
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(gameDir))) Directory.Delete(Path.GetDirectoryName(gameDir)!, true); }
    }

}
