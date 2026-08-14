using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>ToolTip 边缘翻转方向判定（8-13）：窗口边缘区域 → 翻向空间充足的方向</summary>
public class ToolTipPlacementPickerTests
{
    // 窗口 1200x800，控件 14x14（问号图标），提示约 200x50

    [Fact]
    public void Pick_PlentySpace_KeepsBottom()
    {
        // 左上角：各方向都够 → 默认 Bottom（视觉一致）
        var d = ToolTipPlacementPicker.Pick(100, 100, 14, 14, 200, 50, 1200, 800);
        Assert.Equal(ToolTipDirection.Bottom, d);
    }

    [Fact]
    public void Pick_NearBottomEdge_FlipsTop()
    {
        // 贴近窗口底部：下方不够（800-767-25<0）→ 翻上
        var d = ToolTipPlacementPicker.Pick(100, 760, 14, 14, 200, 50, 1200, 800);
        Assert.Equal(ToolTipDirection.Top, d);
    }

    [Fact]
    public void Pick_NearRightEdge_FlipsLeft()
    {
        // 贴近右缘：Bottom 模式水平居中会溢出右侧（1057+200>1200）→ 翻左
        var d = ToolTipPlacementPicker.Pick(1150, 100, 14, 14, 200, 50, 1200, 800);
        Assert.Equal(ToolTipDirection.Left, d);
    }

    [Fact]
    public void Pick_Corner_OnlyLeftFits()
    {
        // 右下角：Bottom/Top 横向溢出、Right 空间不足 → Left 唯一满足
        var d = ToolTipPlacementPicker.Pick(1150, 760, 14, 14, 200, 50, 1200, 800);
        Assert.Equal(ToolTipDirection.Left, d);
    }

    [Fact]
    public void Pick_BottomCenter_FlipsTop()
    {
        // 底部中央：垂直不够翻上（横向居中 OK）
        var d = ToolTipPlacementPicker.Pick(600, 760, 14, 14, 200, 50, 1200, 800);
        Assert.Equal(ToolTipDirection.Top, d);
    }

    [Fact]
    public void Pick_TinyWindow_LeftFits()
    {
        // 窗口比提示还小（Bottom 垂直不够、Top 横向溢出、Right 空间不足）→ Left 是唯一满足方向
        var d = ToolTipPlacementPicker.Pick(300, 300, 14, 14, 200, 50, 400, 350);
        Assert.Equal(ToolTipDirection.Left, d);
    }

    // ---------- 尺寸估算 ----------

    [Fact]
    public void Estimate_ChineseText_WidthByChars()
    {
        var (w, h) = ToolTipPlacementPicker.Estimate("你好世界");
        Assert.Equal(4 * 14 + 12, w); // 4 全角 ×14 + 内边距 12
        Assert.Equal(20 + 12, h);     // 1 行
    }

    [Fact]
    public void Estimate_MultiLine_HeightByLines()
    {
        var (_, h) = ToolTipPlacementPicker.Estimate("第一行\n第二行");
        Assert.Equal(2 * 20 + 12, h);
    }

    [Fact]
    public void Estimate_Empty_Fallback()
    {
        var (w, h) = ToolTipPlacementPicker.Estimate(null);
        Assert.True(w > 0 && h > 0);
    }
}
