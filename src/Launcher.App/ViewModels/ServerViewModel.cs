using System.Collections.ObjectModel;
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

/// <summary>server.properties 编辑行（显示名 + 键 + 当前值）</summary>
public partial class PropRowVM : ObservableObject
{
    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    public partial string Value { get; set; }

    public PropRowVM(string key, string label, string value)
    {
        Key = key;
        Label = label;
        Value = value;
    }
}

/// <summary>
/// 开服页：选择已装版本 → 下载服务端 → 编辑 server.properties → 启动/停止/控制台。
/// </summary>
public partial class ServerViewModel : ViewModelBase
{
    private static readonly (string Key, string Label)[] PropDefs =
    [
        ("server-port", "端口"),
        ("level-name", "世界名"),
        ("max-players", "最大玩家"),
        ("motd", "服务器描述 (MOTD)"),
        ("online-mode", "正版验证 (online-mode)"),
        ("difficulty", "难度 (easy/normal/hard)"),
        ("gamemode", "游戏模式 (survival/creative)"),
        ("view-distance", "视距 (区块)"),
        ("pvp", "PVP"),
        ("white-list", "白名单"),
    ];

    private readonly ServerInstaller _installer = new();
    private readonly ServerProcess _process = new();
    private const int MaxLogLines = 500;

    public ObservableCollection<VersionInstanceVM> InstalledVersions { get; } = [];
    public ObservableCollection<PropRowVM> PropRows { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    [ObservableProperty]
    public partial VersionInstanceVM? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "选一个版本开服";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

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

    public ServerViewModel()
    {
        _process.OutputReceived += line => AppendLog(line);
        _process.Exited += code =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                AppendLog(code == 0 ? "§ 服务端已停止" : $"§ 服务端异常退出（exitCode={code}）");
                Status = code == 0 ? "服务端已停止" : "服务端异常退出，请查看日志";
            });
        };
        _ = RefreshVersionsAsync();
    }

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

    /// <summary>查看机器状态并给出建议配置（内存/CPU/磁盘 + Xmx/Java/视距）</summary>
    [RelayCommand]
    private void RefreshMachineStatus()
    {
        try
        {
            var avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;      // 可用物理内存
            var total = TotalPhysicalMemory();                               // 总物理内存
            var cpu = Environment.ProcessorCount;
            var diskFree = FreeDiskGb(GameDirectory.InstallDir());
            var (xmxMb, view, players) = BuildSuggestion();
            var java = xmxMb >= 4096 ? "17/21（大内存建议 21）" : "17+";

            MachineStatusText =
                $"内存：可用 {avail / 1024.0 / 1024 / 1024:0.#} GB / 总 {total / 1024.0 / 1024 / 1024:0.#} GB" + Environment.NewLine +
                $"CPU：{cpu} 核 · 磁盘剩余：{diskFree:0.#} GB" + Environment.NewLine +
                $"建议配置：-Xmx{xmxMb}M · Java {java} · 视距 {view} · 最大玩家 {players}";
        }
        catch (Exception ex)
        {
            MachineStatusText = $"读取失败: {ex.Message}";
        }
    }

    /// <summary>一键应用建议：写入 server.properties（视距/玩家）+ 更新全局内存</summary>
    [RelayCommand]
    private void ApplySuggestion()
    {
        var dir = ServerDir;
        if (dir is null) { Status = "请先选择版本"; return; }
        var (xmxMb, view, players) = BuildSuggestion();

        // server.properties：只覆盖建议项，不碰用户已有配置
        var props = ServerProperties.Load(Path.Combine(dir, "server.properties"));
        props.Set("view-distance", view.ToString());
        props.Set("max-players", players.ToString());
        props.Save(Path.Combine(dir, "server.properties"));

        // 全局内存 = 建议 Xmx
        var s = LauncherSettings.Current;
        s.MemoryMb = (int)xmxMb;
        s.Save();

        Status = $"已应用建议：内存 {xmxMb}MB · 视距 {view} · 玩家 {players}";
        NotificationService.Success("已应用建议配置");
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
        if (version is null) { Status = "请先选择版本"; return; }
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
            var dir = await _installer.InstallAsync(version.Name, GameDirectory.InstallDir(), null, CancellationToken.None);
            ServerDirText = ServerDir ?? "";
            Status = "服务端下载完成，可启动";
            NotificationService.Success($"{version.Name} 服务端已就绪");
            LoadProperties();
        }
        catch (Exception ex)
        {
            Status = $"下载失败: {ex.Message}";
            NotificationService.Error(ex.Message);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>启动服务端（自动同意 EULA；Java 自动选配 + 设置页内存）</summary>
    [RelayCommand]
    private void StartServer()
    {
        var version = SelectedVersion;
        var dir = ServerDir;
        if (version is null || dir is null) { Status = "请先选择版本"; return; }
        if (!File.Exists(Path.Combine(dir, "server.jar")))
        {
            Status = "尚未下载服务端，请先下载";
            return;
        }
        if (IsRunning) return;

        ServerInstaller.AcceptEula(dir);
        var java = LauncherSettings.Current.JavaPath is { } custom && File.Exists(custom)
            ? custom
            : JavaSelector.Pick(17);
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
        }
        catch (Exception ex)
        {
            Status = $"启动失败: {ex.Message}";
            NotificationService.Error(ex.Message);
        }
    }

    /// <summary>优雅停止（stop 命令 + 超时强杀；后台等待不阻塞 UI）</summary>
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

    /// <summary>加载 server.properties 到编辑表单（默认值兜底）</summary>
    private void LoadProperties()
    {
        var dir = ServerDir;
        if (dir is null) return;
        var props = ServerProperties.Load(Path.Combine(dir, "server.properties"));
        PropRows.Clear();
        foreach (var (key, label) in PropDefs)
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
            PropRows.Add(new PropRowVM(key, label, props.Get(key, fallback)));
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
    }
}
