using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Server;
using Launcher.Core.Utils;

namespace Launcher.App.Views;

/// <summary>存储空间行：路径 + 大小 + 是否可删除</summary>
public sealed partial class StorageItemVM : ObservableObject
{
    public string Path { get; }
    public bool CanDelete { get; }
    public bool IsFile { get; }
    public bool IsHeader { get; }

    [ObservableProperty]
    public partial string SizeText { get; set; } = "…";

    public IRelayCommand DeleteCommand { get; }
    public Action<StorageItemVM>? DeleteRequested { get; set; }

    public StorageItemVM(string path, bool canDelete = false, bool isFile = false, bool isHeader = false)
    {
        Path = path;
        CanDelete = canDelete;
        IsFile = isFile;
        IsHeader = isHeader;
        DeleteCommand = new RelayCommand(() => DeleteRequested?.Invoke(this));
    }
}

/// <summary>存储空间窗口：列出启动器全部文件位置与占用，可清理日志/缓存/崩溃报告/服务端</summary>
public partial class StorageWindow : Window
{
    public ObservableCollection<StorageItemVM> Items { get; } = [];

    public StorageWindow()
    {
        InitializeComponent();
        DataContext = this;
        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Items.Clear();
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher");
        var gameDir = LauncherSettings.Current.GameDirectory ?? GameDirectory.Detect();
        var serversRoot = ServerInstaller.ServerDir(gameDir, "").TrimEnd(Path.DirectorySeparatorChar);

        // ---- 应用数据（%AppData%\Launcher） ----
        Add(appData + "（应用数据）", isHeader: true);
        foreach (var f in new[] { "settings.json", "accounts.json", "history.json", "launch-history.json" })
            Add(Path.Combine(appData, f), isFile: true);
        Add(Path.Combine(appData, "logs"), "（日志，可删）", canDelete: true);
        Add(Path.Combine(appData, "cache"), "（缓存，可删）", canDelete: true);
        Add(Path.Combine(appData, "skins"), "（皮肤）");

        // ---- 游戏目录 ----
        Add(gameDir + "（游戏目录）", isHeader: true);
        Add(Path.Combine(gameDir, "versions"), "（版本）");
        Add(Path.Combine(gameDir, "libraries"), "（库文件）");
        Add(Path.Combine(gameDir, "assets"), "（资源）");
        Add(Path.Combine(gameDir, "saves"), "（存档）");
        Add(Path.Combine(gameDir, "mods"), "（模组）");
        Add(Path.Combine(gameDir, "resourcepacks"), "（材质包）");
        Add(Path.Combine(gameDir, "shaderpacks"), "（光影包）");
        Add(Path.Combine(gameDir, "logs"), "（日志，可删）", canDelete: true);
        Add(Path.Combine(gameDir, "crash-reports"), "（崩溃报告，可删）", canDelete: true);

        // ---- 服务端（启动器目录树下 servers\） ----
        Add(serversRoot + "（服务端根目录）", isHeader: true);
        if (Directory.Exists(serversRoot))
            foreach (var d in Directory.EnumerateDirectories(serversRoot))
                Add(d, "（服务端，可删）", canDelete: true);

        // 后台算大小（大目录 GB 级，逐项异步；防 UI 卡）
        var snap = Items.Where(i => !i.IsHeader).ToList();
        await Task.Run(() =>
        {
            foreach (var item in snap)
                item.SizeText = FormatSize(ItemSize(item.Path, item.IsFile));
        });
    }

    private void Add(string path, string? hint = null, bool canDelete = false, bool isFile = false, bool isHeader = false)
        => Items.Add(new StorageItemVM(isHeader ? $"— {path} —" : hint is null ? path : $"{path} {hint}",
            canDelete, isFile, isHeader)
        {
            DeleteRequested = OnDeleteRequested,
        });

    /// <summary>删除确认（对话框）→ 删除文件/目录 → 移除列表项</summary>
    private void OnDeleteRequested(StorageItemVM item)
    {
        _ = Task.Run(async () =>
        {
            var owner = DialogService.MainWindow();
            if (owner is null || !await DialogService.Confirm(owner,
                    $"删除：{item.Path}\n\n此操作不可恢复，确认删除？", "删除", "删除", "取消"))
            {
                return;
            }
            try
            {
                if (item.IsFile) { if (File.Exists(item.Path)) File.Delete(item.Path); }
                else if (Directory.Exists(item.Path)) Directory.Delete(item.Path, true);
                Dispatcher.UIThread.Post(() =>
                {
                    Items.Remove(item);
                    NotificationService.Success("已删除");
                });
            }
            catch (Exception ex)
            {
                NotificationService.Error($"删除失败: {ex.Message}");
            }
        });
    }

    private static long ItemSize(string path, bool isFile)
    {
        try
        {
            if (isFile) return File.Exists(path) ? new FileInfo(path).Length : 0;
            if (!Directory.Exists(path)) return 0;
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:0.0} GB",
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };
}
