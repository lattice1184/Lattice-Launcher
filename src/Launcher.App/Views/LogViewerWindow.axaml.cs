using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.App.Services;

namespace Launcher.App.Views;

/// <summary>树节点（分类 / 条目共用）：标题 + 可选文件路径（叶子有内容时显示）</summary>
public sealed class LogNodeVM
{
    public string Title { get; }
    public string? Icon { get; }
    public string? FilePath { get; }
    public string? Summary { get; }          // 叶子摘要（无文件时直接当内容展示）
    public ObservableCollection<LogNodeVM> Children { get; } = [];

    public string CountText => Children.Count > 0 ? $"{Children.Count}" : "";

    public LogNodeVM(string title, string? icon = null, string? filePath = null, string? summary = null)
    {
        Title = title;
        Icon = icon;
        FilePath = filePath;
        Summary = summary;
    }

    /// <summary>选中展示的内容：优先读文件；无文件用摘要</summary>
    public string Content()
    {
        if (FilePath is not null && File.Exists(FilePath))
        {
            try { return File.ReadAllText(FilePath); }
            catch { return $"（无法读取 {FilePath}）"; }
        }
        return Summary ?? "（无内容）";
    }
}

/// <summary>
/// 8-22 步骤5：内部树形日志查看器（启动器内展开，不打开外部程序）。
/// 三类：下载（logs/downloads/{任务}_时间戳.log）、启动（logs/launch-*.log）、修复（logs/downloads 中「自动修复」任务）。
/// 选中叶子 → 右侧显示内容；「打开文件」保留外部查看习惯。
/// </summary>
public partial class LogViewerWindow : Window
{
    public ObservableCollection<LogNodeVM> Categories { get; } = [];

    private string? _currentFile;

    public LogViewerWindow()
    {
        InitializeComponent();
        global::Launcher.App.Animations.UiAnim.AttachDialog(this, Root);
        DataContext = this;
        Opened += (_, _) => Refresh();
    }

    private void Refresh()
    {
        Categories.Clear();
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "logs");

        // ① 下载（logs/downloads/*.log 任务摘要 + download.log 完整过程）
        var dlRoot = Path.Combine(logs, "downloads");
        var dlNode = new LogNodeVM("下载", "⭳", summary: "选择条目看内容。下方「完整日志」是全部下载过程（候选源/换源/判死/完成），按时间混排。");
        if (Directory.Exists(dlRoot))
        {
            // 8-22 修：同名任务按时间倒序显示最近一条（旧代码 GroupBy 后取 First 但没按时间排序——「同名合并」假象）
            var files = Directory.GetFiles(dlRoot, "*.log")
                .OrderByDescending(Path.GetFileName)
                .GroupBy(Path.GetFileNameWithoutExtension)
                .Select(g => g.First())
                .OrderByDescending(Path.GetFileName);
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                // 去掉尾部 _HHmmss 时间戳（展示名更干净）
                var display = System.Text.RegularExpressions.Regex.Replace(name, @"_\d{6}$", "");
                dlNode.Children.Add(new LogNodeVM(display, "📄", filePath: f));
            }
        }
        else dlNode.Children.Add(new LogNodeVM("（还没有任务日志）", summary: "下载完成/失败后这里会按任务出现"));
        // 完整过程日志（候选源/换源/判死/竞速——任务摘要只有 4 行，这里是完整证据）
        dlNode.Children.Add(new LogNodeVM("完整日志（download.log）", "📄", filePath: Path.Combine(logs, "download.log")));
        Categories.Add(dlNode);

        // ② 启动（logs/launch-*.log，一条启动会话一个文件）
        var launchNode = new LogNodeVM("启动", "▶");
        if (Directory.Exists(logs))
        {
            var launches = Directory.GetFiles(logs, "launch-*.log").OrderByDescending(Path.GetFileName);
            foreach (var f in launches)
            {
                var ts = Path.GetFileNameWithoutExtension(f).Replace("launch-", "");
                var display = ts.Length >= 12 ? $"{ts[..8].Insert(4, "-").Insert(7, "-")} {ts[8..10]}:{ts[10..12]}" : ts;
                launchNode.Children.Add(new LogNodeVM(display, "📄", filePath: f));
            }
        }
        else launchNode.Children.Add(new LogNodeVM("（还没有启动日志）", summary: "启动游戏后这里会出现当次日志"));
        Categories.Add(launchNode);

        // ③ 修复（下载日志里「自动修复」开头的任务）
        var repairNode = new LogNodeVM("修复", "🔧");
        if (Directory.Exists(dlRoot))
        {
            var repairs = Directory.GetFiles(dlRoot, "自动修复*.log")
                .OrderByDescending(Path.GetFileName)
                .Take(20); // 修复只留最近 20 条（高频噪音）
            foreach (var f in repairs)
            {
                var name = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(f), @"_\d{6}$", "");
                repairNode.Children.Add(new LogNodeVM(name, "📄", filePath: f));
            }
        }
        if (repairNode.Children.Count == 0)
            repairNode.Children.Add(new LogNodeVM("（还没有修复记录）", summary: "启动自动修复/一键修复后这里会记录"));
        Categories.Add(repairNode);

        if (Categories.Count > 0) LogTree.SelectedItem = null;
        // 8-22 修：刷新后清空右侧 + 打开文件按钮（防残留刷新前内容）
        ContentView.Text = "";
        _currentFile = null;
        OpenFileBtn.IsEnabled = false;
        SummaryText.Text = "";
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LogTree.SelectedItem is not LogNodeVM node)
        {
            // 8-22 修：Refresh 后选中被清空 → 清右侧，避免残留刷新前的内容
            ContentView.Text = "";
            _currentFile = null;
            OpenFileBtn.IsEnabled = false;
            SummaryText.Text = "";
            return;
        }
        if (node.Children.Count > 0)
        {
            // 8-22 修：分类节点点击有反馈——显示该分类说明
            ContentView.Text = node.Summary ?? "（选择下方条目看内容）";
            _currentFile = null;
            OpenFileBtn.IsEnabled = false;
            SummaryText.Text = node.Title;
            return;
        }
        ContentView.Text = node.Content();
        _currentFile = node.FilePath;
        OpenFileBtn.IsEnabled = _currentFile is not null && File.Exists(_currentFile);
        SummaryText.Text = node.FilePath is not null
            ? System.IO.Path.GetFileName(node.FilePath)
            : node.Title;
    }

    /// <summary>8-22 修：重复点击同一叶子重新读文件（SelectionChanged 只在变化时触发）</summary>
    private void OnTreePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (LogTree.SelectedItem is LogNodeVM { Children.Count: 0 } node
            && e.GetPosition(LogTree) is var p && LogTree.Bounds.Contains(p))
        {
            ContentView.Text = node.Content();
            _currentFile = node.FilePath;
            OpenFileBtn.IsEnabled = _currentFile is not null && File.Exists(_currentFile);
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (_currentFile is null || !File.Exists(_currentFile)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_currentFile) { UseShellExecute = true });
        }
        catch { NotificationService.Error("无法打开日志文件"); }
    }
}
