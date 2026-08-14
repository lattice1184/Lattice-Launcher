using System.Globalization;
using Avalonia.Data.Converters;

namespace Launcher.App.Converters;

/// <summary>ToggleSwitch Thumb 位移：checked → 20（右端），unchecked → 2（左端）——轨道 36 宽、
/// Thumb 14 宽，双侧各留 2px（36-14-2*2=18，行程 2→20）。绑定驱动 + 模板内 Transition 0.14s</summary>
public sealed class BoolThumbXConverter : IValueConverter
{
    public static readonly BoolThumbXConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 20.0 : 2.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
