using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Download;
using Launcher.Core.Services;
using Launcher.Core.Utils;

namespace Launcher.App.ViewModels;

/// <summary>已装版本行（左栏）：名称 + 来源标签 + 加载器徽章 + 所在目录</summary>
public sealed record InstalledVersionRowVM(
    string Id, string SourceLabel, string LoaderBadge, string GameDir, string ReleaseDate);

/// <summary>
/// 版本页（PCL2 式已装管理）：左栏已装版本列表（跨源扫描 + 搜索 + 行启动），
/// 右栏选中版本的完整设置分区（基本信息/启动配置/加载器/模组/存档/版本操作）。
/// 下载新版本在【下载】板块的"下载游戏"tab。
/// </summary>
public partial class VersionBrowseViewModel : ViewModelBase
{
    private readonly VersionManifestService _svc;
    private readonly VersionInstaller _installer;

    public ObservableCollection<InstalledVersionRowVM> Versions { get; } = [];
    public InstalledVersionDetailVM Detail { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial InstalledVersionRowVM? SelectedVersion { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "加载中…";

    private List<InstalledVersionRowVM> _all = [];

    public VersionBrowseViewModel()
    {
        _svc = new VersionManifestService();
        _installer = new VersionInstaller();
        Detail = new InstalledVersionDetailVM(_installer, OnInstalled);
    }

    private int _loaded;

    /// <summary>幂等加载（首次进入才扫描；失败可重试）</summary>
    public async Task EnsureLoadedAsync()
    {
        if (Volatile.Read(ref _loaded) == 1) return;
        try
        {
            await LoadAsync();
            Volatile.Write(ref _loaded, 1);
        }
        catch { /* 失败保持 0，下次进入重试 */ }
    }

    public async Task LoadAsync()
    {
        try
        {
            await _svc.RefreshAsync();
            var installed = _svc.Entries.Where(e => e.Installed)
                .ToDictionary(e => e.Id, e => e.GameDirectory, StringComparer.OrdinalIgnoreCase);

            _all.Clear();
            foreach (var (id, dir) in installed)
                _all.Add(MakeRow(id, dir));

            // 目录补漏：加载器版本（fabric/forge 等不在 manifest）+ PCL/官方扫描源
            foreach (var (dir, _) in GameDirectory.ScanSourceDirs())
            {
                var versionsDir = Path.Combine(dir, "versions");
                if (!Directory.Exists(versionsDir)) continue;
                foreach (var d in Directory.EnumerateDirectories(versionsDir))
                {
                    var id = Path.GetFileName(d);
                    if (_all.Any(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.Exists(Path.Combine(d, $"{id}.json")))
                        _all.Add(MakeRow(id, dir));
                }
            }

            _all = [.. _all.OrderByDescending(r => r.Id)];
            Rebuild();
            Status = _all.Count == 0
                ? "尚未安装任何版本——去【下载】板块的「下载游戏」下载"
                : $"已安装 {_all.Count} 个版本";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    private static InstalledVersionRowVM MakeRow(string id, string dir) => new(
        id,
        InstallMarker.IsMarked(dir, id) ? "本启动器" : GameDirectory.SourceLabel(GameDirectory.SourceOf(dir)),
        LoaderBadgeOf(id),
        dir,
        GetReleaseDate(dir, id));

    /// <summary>从版本 JSON 读发布时间（懒，缺省空）</summary>
    private static string GetReleaseDate(string dir, string id)
    {
        try
        {
            var json = Path.Combine(dir, "versions", id, $"{id}.json");
            if (!File.Exists(json)) return "";
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
            return doc.RootElement.TryGetProperty("releaseTime", out var t) && t.GetString() is { } s
                ? s[..10] : "";
        }
        catch { return ""; }
    }

    /// <summary>加载器徽章（fabric/forge/neoforge/quilt 从版本 id 判断）</summary>
    private static string LoaderBadgeOf(string id)
    {
        var lower = id.ToLowerInvariant();
        foreach (var kw in new[] { "neoforge", "fabric", "forge", "quilt" })
            if (lower.Contains(kw)) return kw;
        return "";
    }

    private void Rebuild()
    {
        Versions.Clear();
        var kw = SearchText.Trim();
        foreach (var row in _all)
        {
            if (kw.Length == 0 || row.Id.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || row.LoaderBadge.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                Versions.Add(row);
            }
        }
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnSelectedVersionChanged(InstalledVersionRowVM? value)
    {
        if (value is not null) Detail.Select(value);
    }

    /// <summary>安装完成重扫（下载页下载完成后切换回来时调用）</summary>
    public void OnInstalled(string versionId)
    {
        _ = LoadAsync();
    }
}

/// <summary>
/// 右栏版本详情（PCL2 六分区）：基本信息+启动 / 启动配置（版本级覆盖）/ 加载器 / 模组 / 存档 / 版本操作。
/// </summary>
public partial class InstalledVersionDetailVM : ViewModelBase
{
    private readonly VersionInstaller _installer;
    private readonly Action<string> _onInstalled;
    private int _sizeGeneration;

    // ---------- 基本信息 ----------

    [ObservableProperty]
    public partial string Id { get; set; } = "";

    [ObservableProperty]
    public partial string SourceLabel { get; set; } = "";

    [ObservableProperty]
    public partial string ReleaseDate { get; set; } = "";

    [ObservableProperty]
    public partial string SizeText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    public string GameDir { get; private set; } = "";
    public bool ShowRepairButton => !IsDownloading;
    public bool ShowProgress => IsDownloading;
    public bool HasError => ErrorText.Length > 0;

    // ---------- 分区 ----------

    /// <summary>版本管理（模组/存档/删除/备份/导出/打开）</summary>
    [ObservableProperty]
    public partial VersionManageViewModel? Manage { get; set; }

    /// <summary>加载器安装面板（版本 id 无加载器徽章时显示）</summary>
    [ObservableProperty]
    public partial LoaderPickerViewModel? Loader { get; set; }

    // ---------- 版本级启动配置（VersionConfigService） ----------

    [ObservableProperty]
    public partial string ConfigMemoryText { get; set; } = "";

    [ObservableProperty]
    public partial string ConfigJavaText { get; set; } = "";

    [ObservableProperty]
    public partial string ConfigArgsText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasConfigOverrides { get; set; }

    public InstalledVersionDetailVM(VersionInstaller installer, Action<string> onInstalled)
    {
        _installer = installer;
        _onInstalled = onInstalled;
    }

    /// <summary>选中左栏版本 → 填充六分区（加载器徽章为空才显示安装面板）</summary>
    public void Select(InstalledVersionRowVM row)
    {
        if (HasSelection && Id == row.Id) return;
        Id = row.Id;
        GameDir = row.GameDir;
        SourceLabel = row.SourceLabel;
        ReleaseDate = row.ReleaseDate;
        SizeText = "预估体积：计算中…";
        ErrorText = "";
        DownloadProgressPercent = 0;
        HasSelection = true;

        // 分区：版本管理（模组/存档/操作） + 加载器（未装时）
        Manage = new VersionManageViewModel(GameDir, Id, OnVersionDeleted);
        Loader = row.LoaderBadge.Length == 0
            ? new LoaderPickerViewModel(Id, GameDir, () => { })
            : null;

        LoadConfig();
        _ = LoadSizeAsync(row);
    }

    private async Task LoadSizeAsync(InstalledVersionRowVM row)
    {
        var gen = ++_sizeGeneration;
        try
        {
            var version = await _installer.GetOrFetchVersionJsonAsync(row.Id, null, CancellationToken.None);
            if (gen != _sizeGeneration) return;
            long total = version.Downloads?.Client?.Size ?? 0;
            foreach (var lib in version.Libraries ?? [])
            {
                total += lib.Downloads?.Artifact?.Size ?? 0;
                if (lib.Downloads?.Classifiers is { } c) total += c.Values.Sum(x => x.Size ?? 0);
            }
            total += version.AssetIndex?.TotalSize ?? 0;
            total += version.Logging?.Client?.File?.Size ?? 0;
            SizeText = total > 0 ? $"预估体积：{FormatMb(total)}" : "";
        }
        catch
        {
            if (gen == _sizeGeneration) SizeText = "";
        }
    }

    private static string FormatMb(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024.0 / 1024:0.0} MB" : $"{bytes / 1024.0:0} KB";

    // ---------- 启动 / 停止（跳主页执行） ----------

    [RelayCommand]
    private void Launch() => MainViewModel.Current?.LaunchVersion(Id, GameDir);

    [RelayCommand]
    private void Stop() => MainViewModel.Current?.StopGame();

    // ---------- 版本级启动配置 ----------

    private void LoadConfig()
    {
        var cfg = VersionConfigService.Load(GameDir, Id);
        ConfigMemoryText = cfg.MemoryMb?.ToString() ?? "";
        ConfigJavaText = cfg.JavaPath ?? "";
        ConfigArgsText = cfg.ExtraJvmArgs ?? "";
        HasConfigOverrides = cfg.HasOverrides;
    }

    /// <summary>保存版本级配置（空 = 跟随全局）</summary>
    [RelayCommand]
    private void SaveConfig()
    {
        var cfg = new VersionConfig
        {
            MemoryMb = int.TryParse(ConfigMemoryText, out var mb) && mb >= 512 ? mb : null,
            JavaPath = string.IsNullOrWhiteSpace(ConfigJavaText) ? null : ConfigJavaText.Trim(),
            ExtraJvmArgs = string.IsNullOrWhiteSpace(ConfigArgsText) ? null : ConfigArgsText.Trim(),
        };
        VersionConfigService.Save(GameDir, Id, cfg);
        HasConfigOverrides = cfg.HasOverrides;
        NotificationService.Success($"已保存 {Id} 的启动配置");
    }

    /// <summary>恢复跟随全局（清除版本级覆盖）</summary>
    [RelayCommand]
    private void ResetConfig()
    {
        VersionConfigService.Reset(GameDir, Id);
        LoadConfig();
        NotificationService.Success($"已恢复 {Id} 跟随全局设置");
    }

    // ---------- 重新下载（修复） ----------

    [RelayCommand]
    private async Task Repair()
    {
        if (IsDownloading) return;
        var owner = DialogService.MainWindow();
        if (owner is null || !await DialogService.Confirm(owner,
                $"将重新下载 {Id} 的缺失或损坏文件（已有文件自动跳过）。继续？",
                "重新下载", "重新下载", "取消"))
        {
            return;
        }
        var targetId = Id;
        IsDownloading = true;
        ErrorText = "";
        DownloadProgressPercent = 0;
        try
        {
            var installer = new VersionInstaller(gameDirectory: GameDir);
            var version = await installer.GetOrFetchVersionJsonAsync(targetId, null, CancellationToken.None);
            var task = DownloadManager.Instance.EnqueueGroup($"修复 {targetId}", (ctx, ct) =>
                installer.InstallAsync(version, ctx, ct));
            void Sync(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(DownloadTask.ProgressPercent))
                    DownloadProgressPercent = task.ProgressPercent;
                if (e.PropertyName == nameof(DownloadTask.Error) && task.Error is { } err)
                    ErrorText = err;
            }
            task.PropertyChanged += Sync;
            try { await task.Completion; }
            finally { task.PropertyChanged -= Sync; }
            if (task.State == DownloadTaskState.Completed)
                NotificationService.Success($"{targetId} 修复完成");
            else if (task.Error is { } failed) ErrorText = failed;
        }
        catch (Exception ex) { ErrorText = ex.Message; }
        finally
        {
            IsDownloading = false;
            OnPropertyChanged(nameof(ShowRepairButton));
            OnPropertyChanged(nameof(ShowProgress));
            OnPropertyChanged(nameof(HasError));
        }
    }

    private void OnVersionDeleted()
    {
        HasSelection = false;
        Manage = null;
        Loader = null;
        _onInstalled(Id);
    }
}
