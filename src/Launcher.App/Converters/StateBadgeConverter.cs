using Avalonia.Data.Converters;
using Avalonia.Media;
using Launcher.Core.Download;

namespace Launcher.App.Converters;

/// <summary>下载任务状态 → 徽章配色（背景 / 前景两个实例）。Core 层不依赖 Avalonia，颜色映射放视图层。</summary>
public sealed class StateBadgeConverter : IValueConverter
{
    public static StateBadgeConverter Background { get; } = new(bg: true);
    public static StateBadgeConverter Foreground { get; } = new(bg: false);

    private readonly bool _bg;

    private StateBadgeConverter(bool bg) => _bg = bg;

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not DownloadTaskState state) return null;
        return _bg
            ? new SolidColorBrush(state switch
            {
                DownloadTaskState.Completed => Color.Parse("#1E3A2E"),
                DownloadTaskState.Failed => Color.Parse("#3A2020"),
                DownloadTaskState.Canceled => Color.Parse("#2A3240"),
                _ => Color.Parse("#12332F"),
            })
            : new SolidColorBrush(state switch
            {
                DownloadTaskState.Completed => Color.Parse("#5AD07C"),
                DownloadTaskState.Failed => Color.Parse("#E05A5A"),
                DownloadTaskState.Canceled => Color.Parse("#8A93A6"),
                _ => Color.Parse("#6C8CFF"),
            });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
