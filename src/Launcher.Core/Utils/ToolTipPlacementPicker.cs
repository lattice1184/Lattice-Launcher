namespace Launcher.Core.Utils;

/// <summary>ToolTip 弹出方向（与 Avalonia PlacementMode 的映射在 App 层做——Core 不依赖 UI 框架）</summary>
public enum ToolTipDirection { Bottom, Top, Left, Right }

/// <summary>
/// ToolTip 屏幕边缘翻转的方向判定（8-13）：问号提示默认跟随鼠标弹出，窗口右/下边缘会溢出看不见。
/// 判定：按控件位置 + 预估提示尺寸，候选方向须「该侧空间充足 + 垂直/水平不溢出」双满足
/// （Avalonia 对齐语义：Bottom/Top 水平居中于控件，Left/Right 垂直居中、水平边缘贴控件边缘）；
/// 默认 Bottom 优先（视觉一致），不足时垂直翻转 Top，再水平翻转 Right/Left。
/// 纯函数 UI 无关——App 层 ToolTipEdgeFlip 调用并映射 PlacementMode。
/// </summary>
public static class ToolTipPlacementPicker
{
    /// <summary>中文/全角字宽（px）——估算基准，够用即可（提示文案都是短句）</summary>
    private const double FullWidthChar = 14;

    /// <summary>半角字宽（px）</summary>
    private const double HalfWidthChar = 7;

    /// <summary>行高（px）</summary>
    private const double LineHeight = 20;

    /// <summary>提示框内边距余量（px，上下左右合计）</summary>
    private const double Padding = 12;

    /// <summary>
    /// 方向判定：控件左上角 (posX,posY)、控件尺寸 (cw,ch)、预估提示尺寸、窗口尺寸。
    /// 候选（Bottom→Top→Right→Left 顺序，首个双向满足者胜出）。
    /// </summary>
    public static ToolTipDirection Pick(double posX, double posY, double cw, double ch,
        double estW, double estH, double winW, double winH)
    {
        var cx = posX + cw / 2; // 控件水平中心
        var cy = posY + ch / 2; // 控件垂直中心
        var hw = estW / 2;
        var hh = estH / 2;

        // Bottom/Top：垂直贴控件，水平居中（cx ± hw 在窗内）
        if (posY + ch + estH <= winH && cx - hw >= 0 && cx + hw <= winW) return ToolTipDirection.Bottom;
        if (posY - estH >= 0 && cx - hw >= 0 && cx + hw <= winW) return ToolTipDirection.Top;
        // Right/Left：水平贴控件边缘，垂直居中（cy ± hh 在窗内）
        if (posX + cw + estW <= winW && cy - hh >= 0 && cy + hh <= winH) return ToolTipDirection.Right;
        if (posX - estW >= 0 && cy - hh >= 0 && cy + hh <= winH) return ToolTipDirection.Left;

        // 窗口比提示还小（任何方向都溢出）：取剩余空间最大的方向（尽力而为）
        var spaces = new[]
        {
            (ToolTipDirection.Bottom, winH - posY - ch - estH),
            (ToolTipDirection.Top, posY - estH),
            (ToolTipDirection.Right, winW - posX - cw - estW),
            (ToolTipDirection.Left, posX - estW),
        };
        var best = spaces[0];
        for (var i = 1; i < spaces.Length; i++)
            if (spaces[i].Item2 > best.Item2) best = spaces[i];
        return best.Item1;
    }

    /// <summary>文本 → 预估提示尺寸（行 × 行高 + 内边距；每行按字符类型计宽）</summary>
    public static (double Width, double Height) Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return (120, LineHeight + Padding);
        var lines = text.Split('\n');
        var width = 0d;
        foreach (var line in lines)
        {
            var w = 0d;
            foreach (var c in line)
                w += c < 0x80 ? HalfWidthChar : FullWidthChar;
            if (w > width) width = w;
        }
        return (width + Padding, lines.Length * LineHeight + Padding);
    }
}
