using System.Globalization;

namespace Launcher.Core.Utils;

/// <summary>24 位 RGB 纯值（无 UI 依赖，Core 可单测）</summary>
public sealed record Rgb24(byte R, byte G, byte B);

/// <summary>
/// 强调色派生数学（主题系统）：从 Accent 派生「深卡背景 / 亮色文字 / 前景对比色」。
/// 纯字节运算、无 Avalonia 依赖——App 层做 Rgb24 ↔ Avalonia Color 适配。
/// 派生目标（默认青绿 #2DD4BF → #12332F 深卡 / #B5F4E9 亮字），换任何主题色都协调。
/// </summary>
public static class AccentColorMath
{
    /// <summary>taskRow 强调条半透明通道（App 侧拼 Color.FromArgb）</summary>
    public const byte SoftAlpha = 0x26;

    /// <summary>暗字前景（Accent 底上，亮度足够时用）</summary>
    private static readonly Rgb24 DarkText = new(0x0B, 0x1F, 0x1C);

    /// <summary>校验并规范化十六进制色值：#RRGGBB → Rgb24；非法（缺 #、长度错、非 hex）返回 null</summary>
    public static Rgb24? TryNormalizeHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return null;
        for (var i = 1; i < hex.Length; i++)
            if (!Uri.IsHexDigit(hex[i])) return null;
        return new Rgb24(
            byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber),
            byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber),
            byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));
    }

    /// <summary>深卡背景：HSV 派生——H 不变、V=0.20、S×0.82（默认青绿 → #12332F）</summary>
    public static Rgb24 DeriveDark(Rgb24 accent)
    {
        var (h, s, _) = ToHsv(accent);
        return FromHsv(h, s * 0.82, 0.20);
    }

    /// <summary>亮色文字：HSL 派生——H 不变、L=0.83、S 不变（默认青绿 → #B5F4E9）</summary>
    public static Rgb24 DeriveLight(Rgb24 accent)
    {
        var (h, s, _) = ToHsl(accent);
        return FromHsl(h, s, 0.83);
    }

    /// <summary>
    /// Accent 底上的前景文字：底亮度 &gt; 0.30 用暗字 #0B1F1C，否则白字（蓝/紫底白字、橙/绿底暗字，
    /// 8 预设全量对比度 ≥3.0（WCAG UI 组件级）——纯 4.5 需要文字级正文，按钮等组件 3.0 即达标）
    /// </summary>
    public static Rgb24 DeriveOnAccent(Rgb24 accent)
        => RelativeLuminance(accent) > 0.30 ? DarkText : new Rgb24(0xFF, 0xFF, 0xFF);

    /// <summary>WCAG 相对亮度（sRGB 线性化）</summary>
    public static double RelativeLuminance(Rgb24 c)
    {
        double L(double v) => v / 255.0 <= 0.03928 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4);
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    /// <summary>两色对比度（WCAG）</summary>
    public static double ContrastRatio(Rgb24 a, Rgb24 b)
    {
        var (la, lb) = (RelativeLuminance(a), RelativeLuminance(b));
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    // ---------- RGB ↔ HSV ----------

    public static (double H, double S, double V) ToHsv(Rgb24 c)
    {
        var (r, g, b) = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : delta / max, max);
    }

    private static Rgb24 FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgb24(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    // ---------- RGB ↔ HSL ----------

    public static (double H, double S, double L) ToHsl(Rgb24 c)
    {
        var (r, g, b) = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var l = (max + min) / 2;
        double h = 0, s = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            s = delta / (1 - Math.Abs(2 * l - 1));
        }
        if (h < 0) h += 360;
        return (h, s, l);
    }

    private static Rgb24 FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgb24(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
