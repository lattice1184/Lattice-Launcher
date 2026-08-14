using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Launcher.App.Converters;

/// <summary>下载历史状态文字 → 颜色（"失败"红 / "完成"绿 / "已取消"灰；未知走 TextDim 灰）。</summary>
public sealed class HistoryStateBrushConverter : IValueConverter
{
    public static HistoryStateBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string state) return null;
        return new SolidColorBrush(state switch
        {
            "失败" => Color.Parse("#E05A5A"),
            "完成" => Color.Parse("#5AD07C"),
            "已取消" => Color.Parse("#8A93A6"),
            _ => Color.Parse("#6F7B90"),
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
