using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>
/// 版本列表：全部 / 正式版（前 10 折叠展开）/ 快照 / 愚人节 / 远古 分组。
/// </summary>
public partial class VersionListViewModel : ViewModelBase
{
    public ObservableCollection<VersionGroupVM> Groups { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "加载中…";

    public async Task LoadAsync()
    {
        try
        {
            var svc = new VersionManifestService();
            await svc.RefreshAsync();
            Groups.Clear();

            var all = svc.Entries.ToList();
            var release = all.Where(e => e.Type == "release" && !IsAprilFools(e)).ToList();
            var snapshot = all.Where(e => e.Type == "snapshot" && !IsAprilFools(e)).ToList();
            var april = all.Where(IsAprilFools).ToList();
            var ancient = all.Where(e => e.Type is "old_alpha" or "old_beta").ToList();

            Groups.Add(new VersionGroupVM($"全部 ({all.Count})", all.Select(ToVm), isCollapsible: false));
            Groups.Add(new VersionGroupVM($"正式版 ({release.Count})", release.Select(ToVm), isCollapsible: true));
            Groups.Add(new VersionGroupVM($"快照 ({snapshot.Count})", snapshot.Select(ToVm)));
            Groups.Add(new VersionGroupVM($"愚人节 ({april.Count})", april.Select(ToVm)));
            Groups.Add(new VersionGroupVM($"远古 ({ancient.Count})", ancient.Select(ToVm)));

            Status = $"共 {all.Count} 个版本 · 游戏目录已自动识别";
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    /// <summary>愚人节版本识别：4/1 前后发布的 + 特征 id（potato/craftmine/mob/combat 等）</summary>
    public static bool IsAprilFools(VersionManifestService.GameVersionEntry e)
    {
        if (e.ReleaseTime is { Month: 4, Day: <= 3 }) return true;
        var id = e.Id.ToLowerInvariant();
        return id.Contains("potato") || id.Contains("craftmine") || id.Contains("mob")
            || id.Contains("combat") || id.Contains("21w14") || id.Contains("25w14");
    }

    private static VersionEntryVM ToVm(VersionManifestService.GameVersionEntry e) =>
        new(e.Id, e.Type, e.Installed, e.ReleaseTime.ToString("yyyy-MM-dd"));
}

/// <summary>版本分组（正式版组支持前 10 折叠展开）</summary>
public partial class VersionGroupVM : ObservableObject
{
    private const int CollapsedLimit = 10;
    private readonly List<VersionEntryVM> _all;

    public string Title { get; }
    public int Total { get; }
    public bool IsCollapsible { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public IEnumerable<VersionEntryVM> Items =>
        IsCollapsible && !IsExpanded ? _all.Take(CollapsedLimit) : _all;

    public string ToggleText => IsExpanded ? "收起" : $"展开全部 {Total} 个";

    public VersionGroupVM(string title, IEnumerable<VersionEntryVM> items, bool isCollapsible = false)
    {
        Title = title;
        Total = items.Count();
        _all = items.ToList();
        IsCollapsible = isCollapsible && Total > CollapsedLimit;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ToggleText));
    }
}

public sealed record VersionEntryVM(string Id, string Type, bool Installed, string ReleaseDate);
