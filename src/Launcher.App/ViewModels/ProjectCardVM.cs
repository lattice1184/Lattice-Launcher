using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Model.Modrinth;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

/// <summary>生态卡片 VM（图标异步加载，下载量格式化）</summary>
public partial class ProjectCardVM : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Author { get; }
    public string Description { get; }
    public string DownloadsText { get; }
    public string FollowsText { get; }
    public string UpdatedText { get; }
    public string IconUrl { get; }
    public ProjectType Type { get; }
    public string Initial => Title.Length > 0 ? Title[..1] : "?";

    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }

    /// <summary>收藏星标（FavoritesService 持久化）</summary>
    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    /// <summary>星标字符（★已收藏/☆未收藏）</summary>
    public string StarText => IsFavorite ? "★" : "☆";

    /// <summary>星标颜色（收藏=强调青，未收藏=弱灰）</summary>
    public IBrush StarColor => IsFavorite
        ? new SolidColorBrush(Color.Parse("#2DD4BF"))
        : new SolidColorBrush(Color.Parse("#6F7B90"));

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(StarText));
        OnPropertyChanged(nameof(StarColor));
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        FavoritesService.Toggle(Id);
        IsFavorite = !IsFavorite;
    }

    public ProjectCardVM(ModrinthSearchHit hit)
    {
        Id = hit.ProjectId;
        Title = hit.Title;
        Author = hit.Author;
        Description = hit.Description;
        DownloadsText = FormatCount(hit.Downloads);
        FollowsText = FormatCount(hit.Follows);
        UpdatedText = FormatDate(hit.DateModified);
        IconUrl = hit.IconUrl ?? "";
        Type = hit.ProjectType switch
        {
            "modpack" => ProjectType.Modpack,
            "resourcepack" => ProjectType.Resourcepack,
            "shader" => ProjectType.Shader,
            _ => ProjectType.Mod,
        };
        IsFavorite = FavoritesService.IsFavorite(Id);
        _ = ImageLoader.LoadAsync(IconUrl, bmp => Icon = bmp);
    }

    /// <summary>收藏列表构造（用项目详情；无描述/作者时取字段）</summary>
    public ProjectCardVM(ModrinthProjectDetail d)
    {
        Id = d.Id;
        Title = d.Title;
        Author = "";
        Description = d.Description;
        DownloadsText = FormatCount(d.Downloads);
        FollowsText = FormatCount(d.Follows);
        UpdatedText = FormatDate(d.DateModified);
        IconUrl = d.IconUrl ?? "";
        Type = d.ProjectType switch
        {
            "modpack" => ProjectType.Modpack,
            "resourcepack" => ProjectType.Resourcepack,
            "shader" => ProjectType.Shader,
            _ => ProjectType.Mod,
        };
        IsFavorite = FavoritesService.IsFavorite(Id);
        _ = ImageLoader.LoadAsync(IconUrl, bmp => Icon = bmp);
    }

    /// <summary>下载量格式化：1234567 → 1.2M，12345 → 12.3K</summary>
    public static string FormatCount(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}K",
        _ => n.ToString(),
    };

    /// <summary>最后更新时间："更新于 2026-07-20"（异常/默认值容错）</summary>
    private static string FormatDate(DateTime d)
        => d.Year > 2000 ? $"更新于 {d:yyyy-MM-dd}" : "";
}

/// <summary>目标版本实例（生态安装目标 / 主页启动选择）；SourceLabel 标识版本来源（PCL2/本启动器等）；GameDir 为版本所在游戏目录</summary>
public sealed record VersionInstanceVM(string Name, string SourceLabel = "", string GameDir = "")
{
    public string DisplayName => SourceLabel.Length > 0 ? $"{Name} · {SourceLabel}" : Name;
}
