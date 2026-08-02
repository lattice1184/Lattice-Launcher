using Avalonia.Data.Converters;

namespace Launcher.App.Converters;

/// <summary>值等于参数（字符串比较；null 视作空串；参数 "ALL" 匹配 null）——chips 选中态用</summary>
public sealed class ValueEqualsConverter : IValueConverter
{
    public static ValueEqualsConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var v = value?.ToString() ?? "";
        var p = parameter?.ToString() ?? "";
        if (p == "ALL") p = "";
        return v == p;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
