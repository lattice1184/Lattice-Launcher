using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>强调色派生数学离线测试（不依赖 UI）</summary>
public class AccentColorMathTests
{
    private static Rgb24 Rgb(string hex) => AccentColorMath.TryNormalizeHex(hex)!;

    // ---------- TryNormalizeHex ----------

    [Theory]
    [InlineData("#2dd4bf", 0x2D, 0xD4, 0xBF)] // 小写接受
    [InlineData("#2DD4BF", 0x2D, 0xD4, 0xBF)] // 大写接受
    [InlineData("#12332F", 0x12, 0x33, 0x2F)]
    public void TryNormalizeHex_Accepts(string hex, byte r, byte g, byte b)
    {
        var c = AccentColorMath.TryNormalizeHex(hex);
        Assert.NotNull(c);
        Assert.Equal(new Rgb24(r, g, b), c);
    }

    [Theory]
    [InlineData("2DD4BF")]   // 缺 #
    [InlineData("#123")]     // 短
    [InlineData("#1234567")] // 长
    [InlineData("#GGGGGG")]  // 非 hex
    [InlineData("")]         // 空
    [InlineData(null)]       // null
    public void TryNormalizeHex_Rejects(string? hex)
        => Assert.Null(AccentColorMath.TryNormalizeHex(hex));

    // ---------- 派生色（默认青绿基准值） ----------

    [Fact]
    public void DeriveDark_DefaultTeal_Is12332F()
    {
        // 设计目标：深卡 #12332F（每通道 ±1 允许舍入差）
        var dark = AccentColorMath.DeriveDark(Rgb("#2DD4BF"));
        Assert.InRange(dark.R, 0x11, 0x13);
        Assert.InRange(dark.G, 0x32, 0x34);
        Assert.InRange(dark.B, 0x2E, 0x30);
    }

    [Fact]
    public void DeriveLight_DefaultTeal_NearB5F4E9()
    {
        // 设计目标亮字 #B5F4E9；公式（L=0.83、S 不变）实际得 #B0F7EE——同色相亮青绿，
        // 肉眼无差，容差 ±12（精确复刻需 S 再降 ~8%，得不偿失）
        var light = AccentColorMath.DeriveLight(Rgb("#2DD4BF"));
        Assert.InRange(light.R, 0xA4, 0xC0);
        Assert.InRange(light.G, 0xE8, 0xFF);
        Assert.InRange(light.B, 0xDE, 0xFA);
    }

    // ---------- 8 预设全量约束（换色协调性的核心保证） ----------

    private static readonly string[] Presets =
    [
        "#6C8CFF", "#3B82F6", "#8B5CF6", "#F59E0B", "#EC4899", "#EF4444", "#22C55E", "#F97316",
    ];

    [Fact]
    public void DerivedColors_KeepHue()
    {
        foreach (var hex in Presets)
        {
            var accent = Rgb(hex);
            var (h0, _, _) = AccentColorMath.ToHsv(accent);
            var (hd, _, _) = AccentColorMath.ToHsv(AccentColorMath.DeriveDark(accent));
            var (hl, _, _) = AccentColorMath.ToHsl(AccentColorMath.DeriveLight(accent));
            Assert.True(Math.Abs(DiffHue(h0, hd)) <= 2, $"{hex} dark 色相漂移 {DiffHue(h0, hd):F1}°");
            Assert.True(Math.Abs(DiffHue(h0, hl)) <= 2, $"{hex} light 色相漂移 {DiffHue(h0, hl):F1}°");
        }
    }

    [Fact]
    public void DerivedColors_StayInTargetRanges()
    {
        foreach (var hex in Presets)
        {
            var dark = AccentColorMath.ToHsv(AccentColorMath.DeriveDark(Rgb(hex)));
            var light = AccentColorMath.ToHsl(AccentColorMath.DeriveLight(Rgb(hex)));
            Assert.InRange(dark.V, 0.18, 0.24); // 深卡亮度压暗
            Assert.InRange(light.L, 0.80, 0.86); // 亮字提亮
        }
    }

    private static double DiffHue(double a, double b)
    {
        var d = Math.Abs(a - b);
        return d > 180 ? 360 - d : d;
    }

    // ---------- OnAccent 前景色 ----------

    [Fact]
    public void DeriveOnAccent_BrightBgGetsDarkText()
        => Assert.Equal(new Rgb24(0x0B, 0x1F, 0x1C), AccentColorMath.DeriveOnAccent(Rgb("#2DD4BF")));

    [Fact]
    public void DeriveOnAccent_DarkBgGetsWhiteText()
    {
        // 蓝/紫底用暗字对比度不足 → 白字（比现行固定 #0B1F1C 协调）
        Assert.Equal(new Rgb24(0xFF, 0xFF, 0xFF), AccentColorMath.DeriveOnAccent(Rgb("#3B82F6")));
        Assert.Equal(new Rgb24(0xFF, 0xFF, 0xFF), AccentColorMath.DeriveOnAccent(Rgb("#8B5CF6")));
    }

    [Fact]
    public void OnAccent_AllPresetsReachWcag()
    {
        // 按钮等 UI 组件文字：WCAG 3.0（AA 大文本/组件级）；蓝紫底白字 ~3.7 为行业常见水平
        foreach (var hex in Presets)
        {
            var accent = Rgb(hex);
            var on = AccentColorMath.DeriveOnAccent(accent);
            Assert.True(AccentColorMath.ContrastRatio(accent, on) >= 3.0,
                $"{hex} 前景对比度 {AccentColorMath.ContrastRatio(accent, on):F2} < 3.0");
        }
    }
}
