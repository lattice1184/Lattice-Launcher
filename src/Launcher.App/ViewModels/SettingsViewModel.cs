using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Launch;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>强调色预设项（选色器圆点 + 名字）</summary>
public sealed record AccentPresetVM(string Name, string Hex);

/// <summary>内存预设项（Mb=-2 自动按可用内存，0 总内存 60%，Mb=-1 自定义）</summary>
public sealed record MemoryPresetVM(string Name, int Mb)
{
    public bool IsCustom => Mb == -1;
}

/// <summary>下载源策略选项（设置页 ComboBox）</summary>
public sealed record DownloadSourceOption(string Name, DownloadSourcePreference Value);

/// <summary>性能档位选项（设置页 ComboBox）</summary>
public sealed record JvmProfileOption(string Name, PerformanceProfile Value);

/// <summary>
/// 设置页：游戏目录 / 版本隔离 / 内存预设 / Java 路径 / 额外 JVM 参数 / 下载选项。
/// 所有改动即时写入 settings.json（LauncherSettings.Save）。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    public List<MemoryPresetVM> MemoryPresets { get; } =
    [
        new("自动（按可用内存）", -2),
        new("低（2G）", 2048),
        new("中（4G）", 4096),
        new("高（8G）", 8192),
        new("极致（总内存 60%）", 0),
        new("自定义", -1),
    ];

    // ---------- 游戏目录 ----------

    /// <summary>当前目录（安装目标）</summary>
    [ObservableProperty]
    public partial string GameDirectoryText { get; set; }

    /// <summary>目录来源标签（本启动器 / 自配 / PCL2 / 官方）</summary>
    [ObservableProperty]
    public partial string SourceLabelText { get; set; }

    /// <summary>版本隔离开关（各版本独立 saves/mods）</summary>
    [ObservableProperty]
    public partial bool VersionIsolation { get; set; }

    // ---------- 启动 ----------

    [ObservableProperty]
    public partial MemoryPresetVM? SelectedMemoryPreset { get; set; }

    /// <summary>自定义内存输入（MB）</summary>
    [ObservableProperty]
    public partial string MemoryCustomText { get; set; } = "";

    public bool IsCustomMemory => SelectedMemoryPreset?.IsCustom == true;

    /// <summary>Java 路径（空 = 自动选配）</summary>
    [ObservableProperty]
    public partial string JavaPathText { get; set; } = "";

    [ObservableProperty]
    public partial string ExtraJvmArgsText { get; set; } = "";

    [ObservableProperty]
    public partial bool AutoChineseEnabled { get; set; } = true;

    /// <summary>性能档位选项与选中项（GC 参数预设；不影响内存）</summary>
    public IReadOnlyList<JvmProfileOption> JvmProfileOptions { get; } =
    [
        new("轻量", PerformanceProfile.Low),
        new("均衡", PerformanceProfile.Medium),
        new("流畅", PerformanceProfile.High),
        new("极致", PerformanceProfile.Ultra),
    ];

    [ObservableProperty]
    public partial JvmProfileOption? SelectedJvmProfile { get; set; }

    /// <summary>启动随机小提示（彩蛋开关）</summary>
    [ObservableProperty]
    public partial bool StartupTipEnabled { get; set; } = true;

    // ---------- 下载 ----------

    /// <summary>下载源策略选项与选中项（官方优先/镜像优先/仅镜像）</summary>
    public IReadOnlyList<DownloadSourceOption> DownloadSourceOptions { get; } =
    [
        new("官方优先", DownloadSourcePreference.OfficialFirst),
        new("镜像优先", DownloadSourcePreference.MirrorFirst),
        new("仅镜像", DownloadSourcePreference.MirrorOnly),
    ];

    [ObservableProperty]
    public partial DownloadSourceOption? SelectedDownloadSource { get; set; }

    [ObservableProperty]
    public partial int MaxConcurrentDownloads { get; set; }

    /// <summary>下载限速（KB/s；0 = 不限）</summary>
    [ObservableProperty]
    public partial int SpeedLimitKbps { get; set; }

    /// <summary>分片数（每文件并发连接数，1-32；0=用档位默认）</summary>
    [ObservableProperty]
    public partial int ChunkCount { get; set; } = 8;

    /// <summary>CurseForge API Key（空 = 禁用 CF 源）</summary>
    [ObservableProperty]
    public partial string CurseForgeApiKeyText { get; set; } = "";

    /// <summary>Key 有效性验证状态（只含 有效/无效/HTTP 码——**永不包含 key 内容**）</summary>
    [ObservableProperty]
    public partial string CurseForgeApiKeyStatus { get; set; } = "";

    /// <summary>验证序列号：key 输入变化即递增，丢弃过期验证结果（防抖 + 防旧结果覆盖新输入状态）</summary>
    private int _keyValidateSeq;

    /// <summary>构造加载阶段：属性赋值会触发 OnXxxChanged → Save，此时未加载字段还是默认值——
    /// 若不拦截，会把空值写回文件覆盖已保存的设置（如 CurseForgeApiKey）。</summary>
    private bool _loading = true;

    /// <summary>CF 服务（构造含 GameDirectory.Detect() 文件扫描——缓存实例避免每次验证重扫）</summary>
    private readonly CurseForgeService _curseForge = new();

    /// <summary>失焦/页面打开时验证当前 key（调一次 search API；结果只含状态不含 key）</summary>
    public async Task ValidateApiKeyAsync()
    {
        var seq = ++_keyValidateSeq;
        if (string.IsNullOrWhiteSpace(CurseForgeApiKeyText))
        {
            CurseForgeApiKeyStatus = "未配置 Key（留空 = 禁用 CurseForge 源）";
            return;
        }
        CurseForgeApiKeyStatus = "验证中…";
        try
        {
            var (valid, msg) = await _curseForge.ValidateKeyAsync();
            if (seq != _keyValidateSeq) return; // 输入已变，丢弃过期结果
            CurseForgeApiKeyStatus = (valid ? "✓ " : "✗ ") + msg;
        }
        catch (Exception)
        {
            if (seq == _keyValidateSeq) CurseForgeApiKeyStatus = "✗ 验证异常，稍后再试";
        }
    }

    /// <summary>CurseForge 文件 CDN 镜像前缀（空 = 官方 CDN 直连）</summary>
    [ObservableProperty]
    public partial string CurseForgeCdnPrefixText { get; set; } = "";

    // ---------- 外观 ----------

    /// <summary>窗口透明度（0.7-1.0）</summary>
    [ObservableProperty]
    public partial double WindowOpacity { get; set; } = 0.9;

    /// <summary>当前强调色（#RRGGBB）</summary>
    [ObservableProperty]
    public partial string AccentColor { get; set; } = "#2DD4BF";

    /// <summary>界面密度（默认紧凑）</summary>
    [ObservableProperty]
    public partial int DensityIndex { get; set; } = 1; // AL7：默认标准（0=紧凑 1=标准 2=舒适）

    /// <summary>外观变化（MainWindow/App 应用透明度/强调色/密度）</summary>
    public event Action? AppearanceChanged;

    /// <summary>外观预览（点击选项即时预览，不写盘；保存才持久化）</summary>
    public event Action? PreviewChanged;

    /// <summary>预设强调色（圆点+名字；非预设颜色动态插入「自定义 #HEX」项）</summary>
    public static IReadOnlyList<AccentPresetVM> AccentPresets { get; } =
    [
        new("青绿", "#2DD4BF"),
        new("蓝", "#3B82F6"),
        new("紫", "#8B5CF6"),
        new("琥珀", "#F59E0B"),
        new("玫红", "#EC4899"),
    ];

    /// <summary>选色器列表（含自定义兜底项）</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AccentPresetVM> AccentPresetItems { get; set; } = AccentPresets;

    /// <summary>当前选中的预设（null = 自定义色未匹配，回退显示 hex）</summary>
    [ObservableProperty]
    public partial AccentPresetVM? SelectedAccent { get; set; }

    partial void OnSelectedAccentChanged(AccentPresetVM? value)
    {
        if (value is not null) AccentColor = value.Hex; // 触发 PreviewChanged 预览
    }

    public SettingsViewModel()
    {
        var s = LauncherSettings.Current;
        GameDirectoryText = GameDirectory.InstallDir();
        SourceLabelText = GameDirectory.SourceLabel(GameDirectory.DetectSource());
        VersionIsolation = s.VersionIsolation;
        SelectedMemoryPreset = MemoryPresets.FirstOrDefault(p => p.Mb == s.MemoryMb)
            ?? MemoryPresets[^1]; // 非预设值 → 自定义
        MemoryCustomText = s.MemoryMb > 0 ? s.MemoryMb.ToString() : "";
        JavaPathText = s.JavaPath ?? "";
        ExtraJvmArgsText = s.ExtraJvmArgs ?? "";
        AutoChineseEnabled = s.AutoChineseEnabled;
        SelectedJvmProfile = JvmProfileOptions.FirstOrDefault(o => o.Value == s.JvmProfile) ?? JvmProfileOptions[1];
        StartupTipEnabled = s.StartupTipEnabled;
        SelectedDownloadSource = DownloadSourceOptions.FirstOrDefault(o => o.Value == s.DownloadSource) ?? DownloadSourceOptions[0];
        MaxConcurrentDownloads = s.MaxConcurrentDownloads;
        SpeedLimitKbps = s.DownloadSpeedLimitKbps;
        ChunkCount = s.ChunkCount > 0 ? s.ChunkCount : (int)s.DownloadTier; // 老用户继承当前档位，新装默认 8
        CurseForgeApiKeyText = s.CurseForgeApiKey ?? "";
        CurseForgeCdnPrefixText = s.CurseForgeCdnPrefix ?? "";
        WindowOpacity = s.WindowOpacity;
        DensityIndex = (int)s.Density;
        // 强调色：非预设值（老用户自定义）动态插「自定义 #HEX」项；选中项触发 AccentColor 赋值预览
        AccentColor = s.AccentColor;
        if (AccentPresets.All(p => p.Hex != s.AccentColor))
        {
            AccentPresetItems = AccentPresets
                .Prepend(new AccentPresetVM($"自定义 {s.AccentColor.ToUpperInvariant()}", s.AccentColor))
                .ToList();
        }
        SelectedAccent = AccentPresetItems.FirstOrDefault(p => p.Hex == s.AccentColor);

        // 已有 key 的老用户打开设置页即验证一次（结果只含状态，不含 key）
        if (!string.IsNullOrWhiteSpace(CurseForgeApiKeyText))
            _ = ValidateApiKeyAsync();

        _loading = false; // 加载完成，之后属性变化才允许落盘
    }

    // ---------- 写入 ----------

    private void Save()
    {
        if (_loading) return; // 构造加载阶段不落盘（防未加载字段的空值覆盖）
        var s = LauncherSettings.Current;
        s.VersionIsolation = VersionIsolation;
        s.JavaPath = string.IsNullOrWhiteSpace(JavaPathText) ? null : JavaPathText.Trim();
        s.ExtraJvmArgs = string.IsNullOrWhiteSpace(ExtraJvmArgsText) ? null : ExtraJvmArgsText.Trim();
        s.AutoChineseEnabled = AutoChineseEnabled;
        s.DownloadSource = SelectedDownloadSource?.Value ?? DownloadSourcePreference.OfficialFirst;
        s.JvmProfile = SelectedJvmProfile?.Value ?? PerformanceProfile.Medium;
        s.StartupTipEnabled = StartupTipEnabled;
        s.MaxConcurrentDownloads = MaxConcurrentDownloads;
        s.DownloadSpeedLimitKbps = SpeedLimitKbps;
        s.ChunkCount = ChunkCount;
        s.CurseForgeApiKey = CurseForgeApiKeyText.Trim();
        s.CurseForgeCdnPrefix = CurseForgeCdnPrefixText.Trim();
        s.Save();
    }

    partial void OnVersionIsolationChanged(bool value) => Save();

    partial void OnSelectedMemoryPresetChanged(MemoryPresetVM? value)
    {
        if (_loading) return; // 构造加载阶段：仅完成 UI 赋值，不落盘
        OnPropertyChanged(nameof(IsCustomMemory));
        if (value is { } preset)
        {
            if (preset.IsCustom) return; // 自定义值从输入框提交
            LauncherSettings.Current.MemoryMb = preset.Mb;
            LauncherSettings.Current.Save();
        }
    }

    /// <summary>自定义内存输入提交（回车/失焦）</summary>
    public void ApplyCustomMemory(string text)
    {
        if (!IsCustomMemory) return;
        if (int.TryParse(text, out var mb) && mb >= 512)
        {
            LauncherSettings.Current.MemoryMb = mb;
            LauncherSettings.Current.Save();
        }
    }

    partial void OnJavaPathTextChanged(string value) => Save();
    partial void OnExtraJvmArgsTextChanged(string value) => Save();
    partial void OnAutoChineseEnabledChanged(bool value) => Save();
    partial void OnSelectedJvmProfileChanged(JvmProfileOption? value) => Save();
    partial void OnStartupTipEnabledChanged(bool value)
    {
        Save();
        NotificationService.Info(value ? "已开启小提示，下次启动生效" : "已关闭小提示，下次启动生效");
    }
    partial void OnSelectedDownloadSourceChanged(DownloadSourceOption? value) => Save();
    // 滑块拖动连续触发——150ms 防抖写盘（避免每 tick 写 settings.json）
    private CancellationTokenSource? _saveDebounce;

    partial void OnMaxConcurrentDownloadsChanged(int value) => DebouncedSave();
    partial void OnSpeedLimitKbpsChanged(int value) => DebouncedSave();
    partial void OnChunkCountChanged(int value) => DebouncedSave(); // 滑块拖动防抖写盘
    partial void OnCurseForgeApiKeyTextChanged(string value) => Save();
    partial void OnCurseForgeCdnPrefixTextChanged(string value) => Save();

    // 外观：预览模式（改动即时预览，[保存并应用] 才写盘）
    partial void OnWindowOpacityChanged(double value) => PreviewChanged?.Invoke();
    partial void OnAccentColorChanged(string value) => PreviewChanged?.Invoke();
    partial void OnDensityIndexChanged(int value) => PreviewChanged?.Invoke();

    /// <summary>保存并应用外观（写盘 + 持久应用）</summary>
    [RelayCommand]
    private void SaveAppearance()
    {
        var s = LauncherSettings.Current;
        s.WindowOpacity = WindowOpacity;
        s.AccentColor = AccentColor;
        s.Density = (DensityMode)DensityIndex;
        s.Save();
        AppearanceChanged?.Invoke();
        NotificationService.Success("外观已保存并应用");
    }

    /// <summary>重置外观（恢复默认：0.9 / 青绿 / 标准）</summary>
    [RelayCommand]
    private async Task ResetAppearance()
    {
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner,
                "把外观（透明度/强调色/密度）重置回默认？", "重置外观", "重置", "取消"))
        {
            return;
        }
        WindowOpacity = 0.9;
        AccentColor = "#2DD4BF";
        DensityIndex = 1; // 默认标准（AL7：不再默认紧凑缩小 10%）
        PreviewChanged?.Invoke();
        NotificationService.Success("已重置为默认外观（点击「保存并应用」生效）");
    }

    private async void DebouncedSave()
    {
        _saveDebounce?.Cancel();
        var cts = _saveDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, cts.Token);
            Save();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>游戏目录：浏览选择后应用（由 View code-behind 的 FolderPicker 回调）</summary>
    public void ApplyGameDirectory(string path)
    {
        var s = LauncherSettings.Current;
        s.GameDirectory = path;
        s.Save();
        GameDirectoryText = GameDirectory.InstallDir();
        SourceLabelText = GameDirectory.SourceLabel(GameDirectory.DetectSource());
    }

    /// <summary>游戏目录：重置为默认（D 盘优先）</summary>
    public void ResetGameDirectory()
    {
        var s = LauncherSettings.Current;
        s.GameDirectory = null;
        s.Save();
        GameDirectoryText = GameDirectory.InstallDir();
        SourceLabelText = "本启动器";
    }

    /// <summary>Java 路径：浏览选择后应用（FilePicker 回调）</summary>
    public void ApplyJavaPath(string path)
    {
        JavaPathText = path;
        Save();
    }

    /// <summary>Java 路径：恢复自动选配</summary>
    public void ResetJavaPath()
    {
        JavaPathText = "";
        Save();
    }

    /// <summary>清理下载缓存：删除断点续传残留的 *.parts 临时目录（不影响已装版本）</summary>
    public (int Dirs, long Bytes) ClearDownloadCache()
    {
        var gameDir = LauncherSettings.Current.GameDirectory ?? GameDirectory.Detect();
        var removed = 0;
        long freed = 0;
        if (Directory.Exists(gameDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(gameDir, "*.parts", SearchOption.AllDirectories))
            {
                try
                {
                    freed += DirSize(dir);
                    Directory.Delete(dir, true);
                    removed++;
                }
                catch { /* 占用中跳过 */ }
            }
        }
        return (removed, freed);
    }

    private static long DirSize(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch { return 0; }
    }
}
