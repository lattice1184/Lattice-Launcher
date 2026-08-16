using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Services;
using Launcher.Core.Ecosystem;

namespace Launcher.App.ViewModels;

/// <summary>
/// 生态导航页（8-16 批次 51：mcnav 式内置精选站点——无公开 API，静态清单 + 点击开浏览器）。
/// 分类 chips 切换 + 站点卡片网格；纯只读展示，无网络请求。
/// </summary>
public partial class EcosystemNavViewModel : ViewModelBase
{
    /// <summary>分类（含「全部」由 View 端固定写死——SelectedCategory=null 即全部）</summary>
    public IReadOnlyList<string> Categories { get; } = SiteCatalog.Categories;

    /// <summary>当前分类（null = 全部）</summary>
    [ObservableProperty]
    public partial string? SelectedCategory { get; set; }

    /// <summary>当前显示的站点行（favicon 懒加载）</summary>
    public ObservableCollection<NavSiteItemVM> Cards { get; } = [];

    public EcosystemNavViewModel() => RebuildCards();

    partial void OnSelectedCategoryChanged(string? value) => RebuildCards();

    /// <summary>分类 chips 点击（「全部」传 "全部" → 归 null）</summary>
    [RelayCommand]
    private void SelectCategory(string? category)
    {
        SelectedCategory = category is null or "全部" ? null : category;
    }

    /// <summary>点卡片开浏览器（失败提示——不静默）</summary>
    [RelayCommand]
    private void OpenSite(NavSite? site)
    {
        if (site is null || string.IsNullOrWhiteSpace(site.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(site.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NotificationService.Error($"打开 {site.Name} 失败：{ex.Message}");
        }
    }

    /// <summary>底部「更多站点」→ 完整目录 mcnav.net</summary>
    [RelayCommand]
    private void OpenFullCatalog()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.mcnav.net") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NotificationService.Error($"打开失败：{ex.Message}");
        }
    }

    private void RebuildCards()
    {
        Cards.Clear();
        foreach (var site in SiteCatalog.ByCategory(SelectedCategory))
            Cards.Add(new NavSiteItemVM(site));
    }
}

/// <summary>生态导航行 VM：favicon 懒加载（ImageLoader 磁盘缓存 + 并发门）+ 域名计算</summary>
public partial class NavSiteItemVM : ObservableObject
{
    public NavSite Site { get; }

    /// <summary>favicon（失败 → null，视图层降级首字母块）</summary>
    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }

    /// <summary>站点域名（去 www.，等宽小字展示）</summary>
    public string Host { get; }

    /// <summary>首字母（favicon 失败时的降级占位）</summary>
    public string Initial => string.IsNullOrWhiteSpace(Site.Name) ? "?" : Site.Name[..1].ToUpperInvariant();

    public NavSiteItemVM(NavSite site)
    {
        Site = site;
        var host = Uri.TryCreate(site.Url, UriKind.Absolute, out var uri) ? uri.Host : site.Url;
        Host = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        // favicon 免鉴权；ImageLoader 失败会缓存 null（卡片留首字母，不崩）
        _ = ImageLoader.LoadAsync($"https://{Host}/favicon.ico", bmp => Icon = bmp, 48);
    }
}
