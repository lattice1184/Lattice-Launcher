using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>内存预设项（Mb=0 表示总内存 60%，Mb=-1 表示自定义）</summary>
public sealed record MemoryPresetVM(string Name, int Mb)
{
    public bool IsCustom => Mb < 0;
}

/// <summary>
/// 设置页：游戏目录 / 版本隔离 / 内存预设 / Java 路径 / 额外 JVM 参数 / 下载选项。
/// 所有改动即时写入 settings.json（LauncherSettings.Save）。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    public List<MemoryPresetVM> MemoryPresets { get; } =
    [
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

    // ---------- 下载 ----------

    [ObservableProperty]
    public partial bool MirrorFallbackEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int MaxConcurrentDownloads { get; set; }

    /// <summary>下载限速（KB/s；0 = 不限）</summary>
    [ObservableProperty]
    public partial int SpeedLimitKbps { get; set; }

    // ---------- 外观 ----------

    /// <summary>窗口透明度（0.7-1.0）</summary>
    [ObservableProperty]
    public partial double WindowOpacity { get; set; } = 0.9;

    /// <summary>当前强调色（#RRGGBB）</summary>
    [ObservableProperty]
    public partial string AccentColor { get; set; } = "#2DD4BF";

    /// <summary>界面密度</summary>
    [ObservableProperty]
    public partial int DensityIndex { get; set; } = 1;

    /// <summary>外观变化（MainWindow/App 应用透明度/强调色/密度）</summary>
    public event Action? AppearanceChanged;

    /// <summary>预设强调色（色块按钮）</summary>
    public static IReadOnlyList<string> AccentPresets { get; } =
        ["#2DD4BF", "#3B82F6", "#8B5CF6", "#F59E0B", "#EC4899"];

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
        MirrorFallbackEnabled = s.MirrorFallbackEnabled;
        MaxConcurrentDownloads = s.MaxConcurrentDownloads;
        SpeedLimitKbps = s.DownloadSpeedLimitKbps;
        WindowOpacity = s.WindowOpacity;
        AccentColor = s.AccentColor;
        DensityIndex = (int)s.Density;
    }

    // ---------- 写入 ----------

    private void Save()
    {
        var s = LauncherSettings.Current;
        s.VersionIsolation = VersionIsolation;
        s.JavaPath = string.IsNullOrWhiteSpace(JavaPathText) ? null : JavaPathText.Trim();
        s.ExtraJvmArgs = string.IsNullOrWhiteSpace(ExtraJvmArgsText) ? null : ExtraJvmArgsText.Trim();
        s.AutoChineseEnabled = AutoChineseEnabled;
        s.MirrorFallbackEnabled = MirrorFallbackEnabled;
        s.MaxConcurrentDownloads = MaxConcurrentDownloads;
        s.DownloadSpeedLimitKbps = SpeedLimitKbps;
        s.WindowOpacity = WindowOpacity;
        s.AccentColor = AccentColor;
        s.Density = (DensityMode)DensityIndex;
        s.Save();
        AppearanceChanged?.Invoke();
    }

    partial void OnVersionIsolationChanged(bool value) => Save();

    partial void OnSelectedMemoryPresetChanged(MemoryPresetVM? value)
    {
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
    partial void OnMirrorFallbackEnabledChanged(bool value) => Save();
    // 滑块拖动连续触发——150ms 防抖写盘（避免每 tick 写 settings.json）
    private CancellationTokenSource? _saveDebounce;

    partial void OnMaxConcurrentDownloadsChanged(int value) => DebouncedSave();
    partial void OnSpeedLimitKbpsChanged(int value) => DebouncedSave();

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
}
