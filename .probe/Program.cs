using System.IO;
using System.Net.Http;
using Launcher.Core.Download;
using Launcher.Core.Model.Loader;
using Launcher.Core.Model.Mojang;
using Launcher.Core.Utils;

// 实机下载探针:1.21.10 + Fabric 0.19.3,走 GUI 同路径(EnqueueGroup 组任务 → VersionInstaller → LoaderService)
// 目标:验证「进度 99 封顶,100 只在完成时出现」——修复前 assets 阶段会提前显示 100% 且长时间卡住。
var gameDir = Path.Combine(Path.GetTempPath(), "lattice-probe");
Directory.CreateDirectory(gameDir);

var options = DownloadOptions.FromSettings(LauncherSettings.Current);
var downloads = new DownloadService(null, null, options, gameDir);
var installer = new VersionInstaller(downloads: downloads, gameDirectory: gameDir);
// GUI 同路径：从 Mojang version manifest 拿 1.21.10 的 json URL（探针无版本列表页）
using var http = new HttpClient();
using var doc = System.Text.Json.JsonDocument.Parse(
    await http.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
var entry = doc.RootElement.GetProperty("versions").EnumerateArray()
    .FirstOrDefault(v => v.GetProperty("id").GetString() == "1.21.10");
if (entry.ValueKind == System.Text.Json.JsonValueKind.Undefined)
{
    Console.WriteLine("manifest 里没有 1.21.10");
    return 1;
}
var version = await installer.GetOrFetchVersionJsonAsync("1.21.10",
    entry.GetProperty("url").GetString(), CancellationToken.None);
Console.WriteLine($"[probe] version={version.Id} inherits={version.InheritsFrom ?? "-"}");

var task = DownloadManager.Instance.EnqueueGroup("探针 1.21.10+fabric", (ctx, ct) =>
    InstallProbeAsync(installer, downloads, version, gameDir, ctx, ct));

var violations = new List<string>();
var lastPercent = -1.0;
task.PropertyChanged += (_, e) =>
{
    if (e.PropertyName != nameof(DownloadTask.ProgressPercent)) return;
    var p = task.ProgressPercent;
    var st = task.State;
    // 违规 = 未完成时进度已 100(修复目标);完成态 Post(100) 是正常收尾
    if (st != DownloadTaskState.Completed && p >= 99.999)
        violations.Add($"p={p:0.00}% state={st} stage={task.Stage}");
    if (p != lastPercent)
    {
        Console.WriteLine($"[probe] {p:0.00}% state={st} stage={task.Stage}");
        lastPercent = p;
    }
};

var sw = System.Diagnostics.Stopwatch.StartNew();
await task.Completion;
sw.Stop();

Console.WriteLine($"=== 结果:state={task.State} 耗时={sw.Elapsed.TotalSeconds:0.0}s 违规数={violations.Count}");
Console.WriteLine($"  父 Error: {task.Error ?? "(null)"}");
Console.WriteLine($"  子任务: {task.Children.Count} 个");
foreach (var c in task.Children)
    Console.WriteLine($"    [{c.State}] {c.Name} p={c.ProgressPercent:0.00}% stage={c.Stage} err={c.Error ?? "-"}");
foreach (var v in violations) Console.WriteLine("  违规: " + v);
Console.WriteLine($"下载目录: {gameDir}");
if (task.State == DownloadTaskState.Completed)
{
    var versionsDir = Path.Combine(gameDir, "versions");
    foreach (var d in Directory.GetDirectories(versionsDir))
        Console.WriteLine("  " + Path.GetFileName(d));
}
return task.State == DownloadTaskState.Completed ? 0 : 1;

static async Task InstallProbeAsync(VersionInstaller installer, DownloadService downloads,
    VersionJson version, string gameDir, DownloadGroupContext ctx, CancellationToken ct)
{
    // 必须传 downloads：LoaderService 不传会自建 DownloadService() → 写 GameDirectory.Detect()
    // （真实安装位），探针目录空跑还报"缺 94 个文件"（实测 08-09）
    var service = new LoaderService(downloads: downloads, gameDirectory: gameDir);
    var plan = await service.CreatePlanAsync(LoaderKind.Fabric, version.Id, "0.19.3", ct);
    await service.InstallAsync(plan, ctx, ct);
}
