using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Launch;
using Launcher.Core.Server;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>server.properties 编辑行控件类型</summary>
public enum PropControlKind { Text, Bool, Number, Choice }

/// <summary>在线玩家行（服务器图形化管理）</summary>
public sealed record ServerPlayerVM(string Name);

/// <summary>server.properties 编辑行（按类型渲染：文本/开关/数字/下拉）</summary>
public partial class PropRowVM : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public PropControlKind Kind { get; }
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    public partial string Value { get; set; }

    public bool IsBool => Kind == PropControlKind.Bool;
    public bool IsNumber => Kind == PropControlKind.Number;
    public bool IsChoice => Kind == PropControlKind.Choice;
    public bool IsText => Kind == PropControlKind.Text;

    /// <summary>开关绑定（true/false ↔ Value）</summary>
    public bool BoolValue
    {
        get => Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    public PropRowVM(string key, string label, string value, PropControlKind kind, IReadOnlyList<string>? options = null)
    {
        Key = key;
        Label = label;
        Value = value;
        Kind = kind;
        Options = options ?? [];
    }
}

/// <summary>
/// 开服页：选择已装版本 → 下载服务端 → 编辑 server.properties → 启动/停止/控制台。
/// </summary>
public partial class ServerViewModel : ViewModelBase
{
    private static readonly (string Key, string Label, PropControlKind Kind, string[]? Options)[] PropDefs =
    [
        ("server-port", "端口", PropControlKind.Number, null),
        ("level-name", "世界名", PropControlKind.Text, null),
        ("max-players", "最大玩家", PropControlKind.Number, null),
        ("motd", "服务器描述 (MOTD)", PropControlKind.Text, null),
        ("online-mode", "正版验证", PropControlKind.Bool, null),
        ("difficulty", "难度", PropControlKind.Choice, ["easy", "normal", "hard"]),
        ("gamemode", "游戏模式", PropControlKind.Choice, ["survival", "creative", "adventure", "spectator"]),
        ("view-distance", "视距（区块）", PropControlKind.Number, null),
        ("pvp", "PVP", PropControlKind.Bool, null),
        ("white-list", "白名单", PropControlKind.Bool, null),
    ];

    private readonly ServerInstaller _installer = new();
    private readonly ServerProcess _process = new();
    private const int MaxLogLines = 500;

    public ObservableCollection<VersionInstanceVM> InstalledVersions { get; } = [];
    public ObservableCollection<PropRowVM> PropRows { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    /// <summary>在线玩家（日志解析 joined/left the game + list 命令回填）</summary>
    public ObservableCollection<ServerPlayerVM> OnlinePlayers { get; } = [];

    /// <summary>在线玩家标题（在线玩家（N））</summary>
    public string PlayersCountText => $"在线玩家（{OnlinePlayers.Count}）";

    private static readonly Regex JoinedGame = new(@"]: (.+?) joined the game", RegexOptions.Compiled);
    private static readonly Regex LeftGame = new(@"]: (.+?) left the game", RegexOptions.Compiled);
    private static readonly Regex PlayerList = new(@"There are \d+ of a max of \d+ players online: (.+)", RegexOptions.Compiled);

    [ObservableProperty]
    public partial VersionInstanceVM? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "选一个版本开服";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>运行状态上报全局（版本页徽章）——服务端启动时同步刷新局域网地址（AG2/AG3）</summary>
    partial void OnIsRunningChanged(bool value)
    {
        var main = MainViewModel.Current;
        if (main is not null)
        {
            if (value)
                main.RunningVersion = new RunningVersionInfo(SelectedVersion?.Name ?? "", "服务端");
            else if (main.RunningVersion?.Kind == "服务端")
                main.RunningVersion = null;
        }
        if (value) RefreshLanAddress();
        else { LanAddressText = ""; LocalAddressText = ""; }
    }

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    public partial string ServerDirText { get; set; } = "";

    /// <summary>机器状态摘要（内存/CPU/磁盘 + 建议配置）</summary>
    [ObservableProperty]
    public partial string MachineStatusText { get; set; } = "点击刷新查看机器状态与建议配置";

    public string CommandInput { get; set; } = "";

    /// <summary>当前服务端目录（servers/{versionId}）</summary>
    private string? ServerDir => SelectedVersion is null
        ? null
        : ServerInstaller.ServerDir(GameDirectory.InstallDir(), SelectedVersion.Name);

    /// <summary>授予 OP 的玩家名（AH1：预填当前登录账号；可改任意名，无需在线）</summary>
    [ObservableProperty]
    public partial string OpNameText { get; set; } = "";

    /// <summary>授予 OP 操作反馈（成功/错误提示行）</summary>
    [ObservableProperty]
    public partial string OpStatusText { get; set; } = "";

    /// <summary>预生成世界标志：服务端就绪（Done）后自动 stop（AI 批次）</summary>
    private bool _autoStopOnReady;

    /// <summary>预生成世界完成标志：Exited 时提示"世界已生成"</summary>
    private bool _worldGenDone;

    public ServerViewModel()
    {
        OpNameText = Launcher.Core.Account.AccountService.Shared.Current?.Name ?? "";
        _process.OutputReceived += line => AppendLog(line);
        _process.Exited += code =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                IsRunning = false;
                if (_worldGenDone)
                {
                    // 预生成世界流程：世界已落盘，正常结束
                    _worldGenDone = false;
                    _autoStopOnReady = false;
                    var world = CurrentLevelName();
                    AppendLog($"§ 世界「{world}」生成完成，已自动停止");
                    Status = $"世界「{world}」已生成——玩家可以直接进了";
                    NotificationService.Success($"世界「{world}」已生成");
                    RefreshOps();
                    return;
                }
                AppendLog(code == 0 ? "§ 服务端已停止" : $"§ 服务端异常退出（exitCode={code}）");
                Status = code == 0 ? "服务端已停止" : "服务端异常退出，请查看日志";
                if (code != 0)
                {
                    // 动态诊断：等 stdout 缓冲刷完，用已收集日志匹配已知错误模式 → 中文原因弹窗
                    await Task.Delay(300);
                    var diag = Launcher.App.Services.LogDiagnostics.Diagnose(string.Join(Environment.NewLine, Logs));
                    foreach (var d in diag) AppendLog("§ 诊断：" + d.Replace(Environment.NewLine, " "));
                    if (diag.Count > 0 && DialogService.MainWindow() is { } owner)
                        await DialogService.Warn(owner, $"服务端启动失败（exitCode={code}）",
                            string.Join(Environment.NewLine + Environment.NewLine, diag)
                            + Environment.NewLine + Environment.NewLine + "完整日志可在控制台复制或导出。",
                            "服务端异常退出", "知道了", "");
                }
                RefreshOps(); // 停止后 ops.json 为最终态，刷新 OP 列表
            });
        };
        InitSuggestions();
        RefreshSuggestionDiff();
        _ = RefreshVersionsAsync();
        // 机器状态实时刷新（每 5 秒；后台读内存/CPU/磁盘，只更新状态文本不动建议输入）
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusCoreAsync();
        _statusTimer.Start();
    }

    private readonly DispatcherTimer _statusTimer;

    /// <summary>建议配置编辑值（机器状态卡内直接可改，ApplySuggestion 应用）</summary>
    [ObservableProperty]
    public partial string SuggestionMemoryText { get; set; } = "2048";

    [ObservableProperty]
    public partial string SuggestionViewText { get; set; } = "10";

    [ObservableProperty]
    public partial string SuggestionPlayersText { get; set; } = "20";

    /// <summary>建议与当前参数的 diff 提示（应用后/输入变化时联动）</summary>
    [ObservableProperty]
    public partial string SuggestionStatusText { get; set; } = "";

    /// <summary>填入初始建议值（内存/视距/玩家）</summary>
    private void InitSuggestions()
    {
        var (xmx, view, players) = BuildSuggestion();
        SuggestionMemoryText = xmx.ToString();
        SuggestionViewText = view.ToString();
        SuggestionPlayersText = players.ToString();
    }

    partial void OnSuggestionMemoryTextChanged(string value) => RefreshSuggestionDiff();
    partial void OnSuggestionViewTextChanged(string value) => RefreshSuggestionDiff();
    partial void OnSuggestionPlayersTextChanged(string value) => RefreshSuggestionDiff();

    /// <summary>刷新已装版本（构造 + 每次进入开服页调用——新装的版本立即可见）</summary>
    public async Task RefreshVersionsAsync()
    {
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            InstalledVersions.Clear();
            foreach (var e in svc.Entries.Where(e => e.Installed))
                InstalledVersions.Add(new VersionInstanceVM(e.Id));
            // 目录补漏：加载器版本（fabric/forge 等不在 manifest）+ PCL/官方扫描源
            foreach (var (dir, _) in GameDirectory.ScanSourceDirs())
            {
                var versionsDir = Path.Combine(dir, "versions");
                if (!Directory.Exists(versionsDir)) continue;
                foreach (var d in Directory.EnumerateDirectories(versionsDir))
                {
                    var id = Path.GetFileName(d);
                    if (InstalledVersions.Any(i => i.Name.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.Exists(Path.Combine(d, $"{id}.json")))
                        InstalledVersions.Add(new VersionInstanceVM(id));
                }
            }
            if (InstalledVersions.Count > 0 && SelectedVersion is null) SelectedVersion = InstalledVersions[0];
            // 全局版本绑定：主页当前版本优先选中（AF1）
            if (MainViewModel.Current?.CurrentVersion is { } cur)
            {
                var hit = InstalledVersions.FirstOrDefault(v => v.Name.Equals(cur.Name, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) SelectedVersion = hit;
            }
        }
        catch { }
    }

    [RelayCommand]
    private void RefreshVersions() => _ = RefreshVersionsAsync();

    partial void OnSelectedVersionChanged(VersionInstanceVM? value)
    {
        if (value is null) return;
        var dir = ServerInstaller.ServerDir(GameDirectory.InstallDir(), value.Name);
        ServerDirText = dir;
        Status = File.Exists(Path.Combine(dir, "server.jar")) ? "服务端就绪，可启动" : "还没下载服务端";
        LoadProperties();
    }

    /// <summary>建议配置（供显示与应用共用）</summary>
    private (long XmxMb, int ViewDistance, int MaxPlayers) BuildSuggestion()
    {
        var avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (
            (long)Math.Clamp(avail * 0.6 / 1024 / 1024, 1024, 8192),
            10,
            20);
    }

    /// <summary>机器状态实时刷新（每 5 秒自动；后台读内存/CPU/磁盘）</summary>
    private async Task RefreshStatusCoreAsync()
    {
        MachineStatusText = await Task.Run(() =>
        {
            try
            {
                var avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;      // 可用物理内存
                var total = TotalPhysicalMemory();                               // 总物理内存
                var diskFree = FreeDiskGb(GameDirectory.InstallDir());
                var cpu = CpuUsagePercent();                                     // 实时 CPU 使用率（失败 -1）

                var cpuText = cpu >= 0 ? $"{cpu:0.#}%" : $"{Environment.ProcessorCount} 核";
                return $"内存：可用 {avail / 1024.0 / 1024 / 1024:0.#} GB / 总 {total / 1024.0 / 1024 / 1024:0.#} GB" + Environment.NewLine +
                       $"CPU：{cpuText}（{Environment.ProcessorCount} 核） · 磁盘剩余：{diskFree:0.#} GB";
            }
            catch (Exception ex)
            {
                return $"读取失败: {ex.Message}";
            }
        });
    }

    /// <summary>CPU 使用率（PerformanceCounter 两次采样差值；无权限/不支持返回 -1）</summary>
    private static double CpuUsagePercent()
    {
        try
        {
            using var counter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            counter.NextValue();
            Thread.Sleep(300);
            return Math.Round(counter.NextValue(), 1);
        }
        catch { return -1; }
    }

    /// <summary>应用建议配置（读建议编辑框值）：写 server.properties（视距/玩家）+ 更新全局内存</summary>
    [RelayCommand]
    private async Task ApplySuggestion()
    {
        var dir = ServerDir;
        if (dir is null)
        {
            await WarnNoVersion();
            return;
        }
        var xmxMb = long.TryParse(SuggestionMemoryText, out var m) && m >= 512 ? m : 2048;
        var view = int.TryParse(SuggestionViewText, out var v) && v >= 2 && v <= 32 ? v : 10;
        var players = int.TryParse(SuggestionPlayersText, out var p) && p >= 1 && p <= 1000 ? p : 20;

        // server.properties：只覆盖建议项，不碰用户已有配置
        var props = ServerProperties.Load(Path.Combine(dir, "server.properties"));
        props.Set("view-distance", view.ToString());
        props.Set("max-players", players.ToString());
        props.Save(Path.Combine(dir, "server.properties"));

        // 全局内存 = 建议 Xmx（启动服务端时使用）
        var s = LauncherSettings.Current;
        s.MemoryMb = (int)xmxMb;
        s.Save();

        // 刷新表单显示已应用值 + 建议区同步（改前可能是旧值，不刷则表单与建议区不同步）
        LoadProperties();
        RefreshSuggestionDiff();

        Status = $"已应用配置：内存 {xmxMb}MB · 视距 {view} · 玩家 {players}";
        NotificationService.Success("已应用服务器配置");
    }

    /// <summary>建议 diff：对比建议编辑框值与当前 server.properties 参数（输入变化/应用后联动）</summary>
    private void RefreshSuggestionDiff()
    {
        var view = int.TryParse(SuggestionViewText, out var sv) ? sv : 10;
        var players = int.TryParse(SuggestionPlayersText, out var sp) ? sp : 20;
        var diffs = new List<string>();
        if (int.TryParse(PropRows.FirstOrDefault(r => r.Key == "view-distance")?.Value, out var cv) && cv != view)
            diffs.Add($"视距 {view}（当前 {cv}）");
        if (int.TryParse(PropRows.FirstOrDefault(r => r.Key == "max-players")?.Value, out var cp) && cp != players)
            diffs.Add($"最大玩家 {players}（当前 {cp}）");
        SuggestionStatusText = diffs.Count == 0
            ? $"建议配置已与当前参数一致 ✓（视距 {view} · 最大玩家 {players}）"
            : $"建议调整：{string.Join("、", diffs)}（点[应用建议配置]生效）";
    }

    /// <summary>物理内存总量（GlobalMemoryStatusEx P/Invoke）</summary>
    private static ulong TotalPhysicalMemory()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys : 0;
        }
        catch { return 0; }
    }

    private static double FreeDiskGb(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path) ?? "C:\\";
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
        }
        catch { return 0; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>下载服务端 jar（确认后执行；幂等跳过已有）</summary>
    [RelayCommand]
    private async Task DownloadServer()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            await WarnNoVersion();
            return;
        }
        if (IsInstalling) return;
        if (DialogService.MainWindow() is { } owner
            && !await DialogService.Confirm(owner,
                $"下载 {version.Name} 服务端（约 50MB）？", "下载服务端", "下载", "取消"))
        {
            return;
        }

        IsInstalling = true;
        Status = "正在下载服务端…";
        try
        {
            var installer = _installer;
            var dir = GameDirectory.InstallDir();
            var task = DownloadManager.Instance.EnqueueGroup($"下载服务端 {version.Name}", (ctx, ct) =>
            {
                ctx.AddChild("server.jar", 1, (progress, c) => installer.InstallAsync(version.Name, dir, progress, c));
                return Task.CompletedTask;
            });
            // 自动跳到下载板块"下载记录"tab（角标已随 ActiveCountChanged 亮起）
            MainViewModel.Current?.NavigateToDownloadQueue();
            await task.Completion;
            VerifyServerJar(ServerDir); // 不完整则抛异常（进 catch 显式报错，防套娃）
            ServerDirText = ServerDir ?? "";
            LoadProperties();
            Status = "服务端下载完成，可启动";
            NotificationService.Success($"{version.Name} 服务端已就绪");
        }
        catch (Exception ex)
        {
            Status = $"下载失败: {ex.Message}";
            NotificationService.Error(ex.Message);
            // 失败显式报出（终止"未安装→再下载→又失败"套娃）：单按钮弹窗说明原因
            if (DialogService.MainWindow() is { } dlg)
                await DialogService.Warn(dlg, "服务端下载失败",
                    ex.Message + "\n\n可稍后重试「下载服务端」。", "下载失败", "知道了", "");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>前提不满足警告：未选版本（红字加粗原因 + 说明）</summary>
    private static async Task WarnNoVersion() =>
        await DialogService.Warn(DialogService.MainWindow(), "请先选择版本",
            "请在顶部选择要开服的已安装版本，再继续操作。", "无法继续", "知道了", "");

    /// <summary>下载服务端并自动启动（弹窗"立即下载并启动"确认后走这里；下载完成前提已满足直接 StartServer）</summary>
    private async Task DownloadAndStartAsync()
    {
        var version = SelectedVersion;
        if (version is null || IsInstalling) return;
        IsInstalling = true;
        Status = "正在下载服务端…";
        try
        {
            var installer = _installer;
            var dir = GameDirectory.InstallDir();
            var task = DownloadManager.Instance.EnqueueGroup($"下载服务端 {version.Name}", (ctx, ct) =>
            {
                ctx.AddChild("server.jar", 1, (progress, c) => installer.InstallAsync(version.Name, dir, progress, c));
                return Task.CompletedTask;
            });
            // 自动跳到下载板块"下载记录"tab（与 DownloadServer 一致）
            MainViewModel.Current?.NavigateToDownloadQueue();
            await task.Completion;
            VerifyServerJar(ServerDir); // 不完整则抛异常（进 catch 显式报错，防套娃）
            ServerDirText = ServerDir ?? "";
            LoadProperties();
            Status = "服务端下载完成，正在启动…";
            StartServer(); // 前提已满足（jar 就位）
        }
        catch (Exception ex)
        {
            Status = $"下载失败: {ex.Message}";
            NotificationService.Error(ex.Message);
            // 失败显式报出（终止"未安装→再下载→又失败"套娃）：单按钮弹窗说明原因
            if (DialogService.MainWindow() is { } dlg)
                await DialogService.Warn(dlg, "服务端下载失败",
                    ex.Message + "\n\n可稍后重试「下载服务端」。", "下载失败", "知道了", "");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>启动服务端（自动同意 EULA；Java 自动选配 + 设置页内存）；前提不满足弹红字警告对话框</summary>
    [RelayCommand]
    private async Task StartServer()
    {
        var version = SelectedVersion;
        var dir = ServerDir;
        if (version is null || dir is null)
        {
            await WarnNoVersion();
            return;
        }
        if (!File.Exists(Path.Combine(dir, "server.jar")))
        {
            // 红字警告：未安装服务端 → 提供"立即下载并启动"
            if (DialogService.MainWindow() is { } owner
                && await DialogService.Warn(owner, "未安装服务端",
                    $"「{version.Name}」的服务端尚未下载。可立即下载并启动，或先取消。", "无法启动服务端",
                    "立即下载并启动", "取消"))
            {
                await DownloadAndStartAsync();
            }
            return;
        }
        if (IsRunning || IsInstalling) return;

        ServerInstaller.AcceptEula(dir);
        var java = LauncherSettings.Current.JavaPath is { } custom && File.Exists(custom)
            ? custom
            : PickServerJava(version.Name);
        var mem = LauncherSettings.Current.MemoryMb > 0
            ? LauncherSettings.Current.MemoryMb
            : 2048;
        try
        {
            Logs.Clear();
            _process.Start(dir, java, mem);
            IsRunning = true;
            Status = "服务端运行中（控制台可输命令）";
            AppendLog($"§ 已启动：{java}");
            AppendLog($"§ 内存 {mem}MB · 世界目录 {dir}");
            RefreshOps(); // 启动后 ops.json 已就绪（含已授予的 OP）
        }
        catch (Exception ex)
        {
            Status = $"启动失败: {ex.Message}";
            NotificationService.Error(ex.Message);
        }
    }

    /// <summary>优雅停止（stop 命令 + 超时强杀；后台等待不阻塞 UI）</summary>
    /// <summary>服务端 Java 按版本选择（26.x 需 Java 21+——修复 Java 17 硬编码启动即崩）；找不到降级 21/17</summary>
    private static string PickServerJava(string versionId)
    {
        var major = 17;
        try
        {
            var p = Path.Combine(GameDirectory.InstallDir(), "versions", versionId, $"{versionId}.json");
            if (File.Exists(p))
            {
                var v = System.Text.Json.JsonSerializer.Deserialize<Launcher.Core.Model.Mojang.VersionJson>(File.ReadAllText(p));
                if (v?.JavaVersion?.MajorVersion is { } m && m > 0) major = m;
            }
        }
        catch { }
        foreach (var cand in new[] { major, 21, 17 })
        {
            try
            {
                var picked = JavaSelector.Pick(cand);
                if (!string.IsNullOrEmpty(picked)) return picked;
            }
            catch { }
        }
        throw new InvalidOperationException("未找到可用 Java（可在设置页指定 Java 路径）");
    }

    // ---------- 连接信息（AG3 + AH1：服务端运行中显示本机/局域网地址，朋友可连） ----------

    /// <summary>局域网地址行（ip:port，复制给局域网朋友）</summary>
    [ObservableProperty]
    public partial string LanAddressText { get; set; } = "";

    /// <summary>本机地址行（127.0.0.1:port，一键进服用）</summary>
    [ObservableProperty]
    public partial string LocalAddressText { get; set; } = "";

    /// <summary>刷新连接信息（读 server.properties 端口；无内网 IP 时局域网行留空）</summary>
    private void RefreshLanAddress()
    {
        var port = 25565;
        try
        {
            var dir = ServerDir;
            if (dir is not null)
                port = ServerProperties.Load(Path.Combine(dir, "server.properties")).GetInt("server-port", 25565);
        }
        catch { }
        LocalAddressText = $"本机 127.0.0.1:{port}";
        var ip = FindLanIp();
        LanAddressText = ip is null ? "" : $"局域网 {ip}:{port}";
    }

    /// <summary>复制局域网地址（去掉前缀标签，直接得到 ip:port）</summary>
    [RelayCommand]
    private async Task CopyLanAddress()
    {
        if (LanAddressText.Length == 0) return;
        var top = DialogService.MainWindow();
        if (top is null) return;
        var cb = Avalonia.Controls.TopLevel.GetTopLevel(top)?.Clipboard;
        if (cb is null) return;
        await cb.SetTextAsync(LanAddressText.Replace("局域网 ", ""));
        NotificationService.Success("局域网地址已复制");
    }

    /// <summary>取内网 IPv4（优先私有段 192.168 / 10.x / 172.16-31）</summary>
    private static string? FindLanIp()
    {
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                var ip = ua.Address.ToString();
                if (ip.StartsWith("192.168.") || ip.StartsWith("10.")) return ip;
                if (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var b) && b is >= 16 and <= 31) return ip;
            }
        }
        return null;
    }

    /// <summary>验证 server.jar 下载完整（缺失或 <1MB 视为失败，抛异常终止后续启动）</summary>
    private static void VerifyServerJar(string? dir)
    {
        var jar = Path.Combine(dir ?? "", "server.jar");
        if (!File.Exists(jar) || new FileInfo(jar).Length < 1024 * 1024)
            throw new InvalidDataException("服务端文件下载不完整（server.jar 缺失或过小），请重试");
    }

    /// <summary>一键进服：启动客户端并自动连接本地服务端（复用主页完整启动链路：阶段指示/日志/退出处理）</summary>
    [RelayCommand]
    private async Task JoinGame()
    {
        var version = SelectedVersion;
        if (version is null || !IsRunning) return;
        var dir = ServerDir;
        if (dir is null) return;
        var props = ServerProperties.Load(Path.Combine(dir, "server.properties"));
        var port = props.GetInt("server-port", 25565);
        Status = "正在拉起客户端并连接服务器…";
        if (MainViewModel.Current is { } main)
            await main.Home.RequestLaunchWithServerAsync(version.Name, GameDirectory.InstallDir(), "127.0.0.1", port);
    }

    [RelayCommand]
    private async Task StopServer()
    {
        if (!IsRunning) return;
        Status = "正在停止…";
        AppendLog("§ 发送 stop 命令…");
        await Task.Run(() => _process.Stop());
    }

    /// <summary>发送控制台命令（回车触发；输入框清空）</summary>
    [RelayCommand]
    private void SendCommand(string command)
    {
        var cmd = command?.Trim();
        if (string.IsNullOrEmpty(cmd)) return;
        AppendLog($"> {cmd}");
        _process.SendCommand(cmd);
    }

    // ---------- 服务器图形化管理 ----------

    /// <summary>刷新玩家列表（list 命令 → 日志解析回填）</summary>
    [RelayCommand]
    private void RefreshPlayers()
    {
        if (!IsRunning) return;
        _process.SendCommand("list");
    }

    /// <summary>踢出玩家</summary>
    [RelayCommand]
    private void KickPlayer(ServerPlayerVM player) => PlayerOp($"kick {player.Name}", $"已踢出 {player.Name}");

    /// <summary>封禁玩家</summary>
    [RelayCommand]
    private void BanPlayer(ServerPlayerVM player) => PlayerOp($"ban {player.Name}", $"已封禁 {player.Name}");

    /// <summary>授予 OP</summary>
    [RelayCommand]
    private void OpPlayer(ServerPlayerVM player) => PlayerOp($"op {player.Name}", $"已授予 {player.Name} OP");

    private void PlayerOp(string command, string doneText)
    {
        if (!IsRunning) return;
        _process.SendCommand(command);
        NotificationService.Success(doneText);
    }

    /// <summary>授予任意玩家 OP（AH1：不需要玩家在线——MC 的 op 命令写 ops.json，上线即生效）</summary>
    [RelayCommand]
    private void GrantOp()
    {
        var name = OpNameText.Trim();
        if (!IsRunning) { OpStatusText = "先启动服务端再授予 OP"; return; }
        if (name.Length == 0) { OpStatusText = "先输入玩家名"; return; }
        _process.SendCommand($"op {name}");
        OpStatusText = $"已授予 {name} OP——玩家上线即生效";
        _ = Task.Run(async () => { await Task.Delay(500); Dispatcher.UIThread.Post(RefreshOps); });
    }

    // ---------- OP 列表（AI：启动器图形化管理服务器权限——读 ops.json，服务器权限 > 游戏内命令） ----------

    /// <summary>OP 列表（ops.json 展示：名字 + 权限等级）</summary>
    public ObservableCollection<ServerOpEntry> OpsList { get; } = [];

    /// <summary>OP 列表标题（OP 列表（N））</summary>
    public string OpsCountText => $"OP 列表（{OpsList.Count}）";

    /// <summary>刷新 OP 列表（读 ops.json；服务端启动/停止后 + 手动刷新）</summary>
    [RelayCommand]
    private void RefreshOps()
    {
        var dir = ServerDir;
        OpsList.Clear();
        if (dir is not null)
            foreach (var op in ServerOpsFile.Load(dir))
                OpsList.Add(op);
        OnPropertyChanged(nameof(OpsCountText));
    }

    /// <summary>移除 OP（deop 命令；服务端写 ops.json 后自动刷新）</summary>
    [RelayCommand]
    private async Task RemoveOp(ServerOpEntry entry)
    {
        if (!IsRunning) { OpStatusText = "先启动服务端再移除 OP"; return; }
        _process.SendCommand($"deop {entry.Name}");
        OpStatusText = $"已发送 deop {entry.Name}";
        await Task.Delay(500);
        RefreshOps();
    }

    // ---------- 预生成世界（AI：启动服务端 → 日志 Done → 自动 stop，空世界落盘） ----------

    private static readonly Regex ServerReady = new(@"Done \(\d+(\.\d+)?s\)!?", RegexOptions.Compiled);

    /// <summary>当前世界名（server.properties 的 level-name，默认 world）</summary>
    private string CurrentLevelName()
    {
        try
        {
            var dir = ServerDir;
            if (dir is not null
                && ServerProperties.Load(Path.Combine(dir, "server.properties")).Get("level-name") is { } n
                && n.Length > 0)
                return n;
        }
        catch { }
        return "world";
    }

    /// <summary>预生成空世界：启动服务端 → 就绪（Done）自动停止 → 玩家直接进服</summary>
    [RelayCommand]
    private async Task GenerateWorld()
    {
        var dir = ServerDir;
        if (dir is null) { await WarnNoVersion(); return; }
        if (IsRunning || IsInstalling) { Status = "服务端运行中，停止后再生成世界"; return; }
        if (!File.Exists(Path.Combine(dir, "server.jar"))) { Status = "先下载服务端再生成世界"; return; }
        var levelName = CurrentLevelName();
        if (Directory.Exists(Path.Combine(dir, levelName)))
        {
            Status = $"世界「{levelName}」已存在，无需生成";
            return;
        }
        if (DialogService.MainWindow() is not { } owner
            || !await DialogService.Confirm(owner,
                $"将启动服务端生成新世界「{levelName}」（首次启动约 1~2 分钟），生成完成后自动停止。继续？",
                "生成世界", "生成世界", "取消"))
        {
            return;
        }
        _autoStopOnReady = true;
        _worldGenDone = false;
        Status = $"正在生成世界「{levelName}」（完成后自动停止）…";
        await StartServer();
    }

    /// <summary>行级检测：预生成世界时服务端就绪（Done）→ 自动 stop</summary>
    private void ParseServerReady(string line)
    {
        if (!_autoStopOnReady || !ServerReady.IsMatch(line)) return;
        _autoStopOnReady = false;
        _worldGenDone = true;
        AppendLog("§ 世界生成完成，自动停止…");
        _process.SendCommand("stop");
    }

    /// <summary>日志行玩家解析（joined/left 实时增删；list 输出整体重置）</summary>
    private void ParsePlayerLine(string line)
    {
        if (JoinedGame.Match(line) is { Success: true } j && j.Groups[1].Value is var jn
            && OnlinePlayers.All(p => p.Name != jn))
        {
            OnlinePlayers.Add(new ServerPlayerVM(jn));
            OnPropertyChanged(nameof(PlayersCountText));
        }
        else if (LeftGame.Match(line) is { Success: true } l)
        {
            var ln = l.Groups[1].Value;
            var hit = OnlinePlayers.FirstOrDefault(p => p.Name == ln);
            if (hit is not null)
            {
                OnlinePlayers.Remove(hit);
                OnPropertyChanged(nameof(PlayersCountText));
            }
        }
        else if (PlayerList.Match(line) is { Success: true } pl)
        {
            OnlinePlayers.Clear();
            foreach (var name in pl.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                OnlinePlayers.Add(new ServerPlayerVM(name));
            OnPropertyChanged(nameof(PlayersCountText));
        }
    }

    /// <summary>加载 server.properties 到编辑表单（默认值兜底）</summary>
    private void LoadProperties()
    {
        var dir = ServerDir;
        if (dir is null) return;
        var props = ServerProperties.Load(Path.Combine(dir, "server.properties"));
        PropRows.Clear();
        foreach (var (key, label, kind, options) in PropDefs)
        {
            var fallback = key switch
            {
                "server-port" => "25565",
                "max-players" => "20",
                "difficulty" => "normal",
                "gamemode" => "survival",
                "online-mode" => "true",
                "pvp" => "true",
                "white-list" => "false",
                "view-distance" => "10",
                _ => "",
            };
            PropRows.Add(new PropRowVM(key, label, props.Get(key, fallback), kind, options));
        }
    }

    /// <summary>保存 server.properties（写回服务端目录）</summary>
    [RelayCommand]
    private void SaveProperties()
    {
        var dir = ServerDir;
        if (dir is null) return;
        var props = ServerProperties.Load(Path.Combine(dir, "server.properties"));
        foreach (var row in PropRows) props.Set(row.Key, row.Value);
        props.Save(Path.Combine(dir, "server.properties"));
        Status = "server.properties 已保存";
        NotificationService.Success("server.properties 已保存");
        RefreshSuggestionDiff(); // 手动改参数后建议区联动（与建议不一致时提示差异）
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(line));
            return;
        }
        if (Logs.Count >= MaxLogLines) Logs.RemoveAt(0);
        Logs.Add(line);
        ParsePlayerLine(line); // 玩家在线跟踪（joined/left/list）
        ParseServerReady(line); // 预生成世界：Done → 自动 stop
    }
}
