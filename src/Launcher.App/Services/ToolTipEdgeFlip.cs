using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Launcher.Core.Utils;

namespace Launcher.App.Services;

/// <summary>
/// ToolTip 屏幕边缘翻转（8-13）：问号提示默认跟随鼠标弹出，窗口右/下边缘会溢出看不见。
/// 挂主窗口监听 ToolTip Opening 路由事件——打开前按目标控件位置 + 预估提示尺寸做区域判定
/// （ToolTipPlacementPicker 纯函数），SetPlacement 翻转（Bottom/Top/Left/Right 锚定控件，
/// 位置稳定且不溢出窗口）。
/// </summary>
public static class ToolTipEdgeFlip
{
    /// <summary>挂到主窗口（问号全部在主窗口各 View；路由事件从目标控件冒泡到根）</summary>
    public static void Attach(Window root)
        => ToolTip.AddToolTipOpeningHandler(root, OnToolTipOpening);

    private static void OnToolTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is not Control control) return;
        var topLevel = TopLevel.GetTopLevel(control);
        if (topLevel is null) return;
        var pos = control.TranslatePoint(new Point(0, 0), topLevel);
        if (pos is null) return;

        // 预估提示尺寸（文本估算；非文本提示用兜底值）
        var (estW, estH) = ToolTipPlacementPicker.Estimate(ToolTip.GetTip(control) as string);

        var b = control.Bounds;
        var win = topLevel.Bounds;
        var dir = ToolTipPlacementPicker.Pick(pos.Value.X, pos.Value.Y, b.Width, b.Height,
            estW, estH, win.Width, win.Height);

        ToolTip.SetPlacement(control, dir switch
        {
            ToolTipDirection.Top => PlacementMode.Top,
            ToolTipDirection.Left => PlacementMode.Left,
            ToolTipDirection.Right => PlacementMode.Right,
            _ => PlacementMode.Bottom,
        });
    }
}
