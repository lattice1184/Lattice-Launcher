namespace Launcher.Core.Server;

/// <summary>开服建议配置档位计算（纯逻辑，可单测）：按 CPU 核数 + 可用内存推算 内存/视距/玩家。</summary>
public static class SuggestionPresets
{
    public enum Preset { Low, Recommended, High }

    /// <summary>动态推荐：≤2 核或 <4G 可用 → 低配；≤4 核 → 中配；≥8 核 → 高配</summary>
    public static (long XmxMb, int ViewDistance, int MaxPlayers) Compute(int cores, long availMb)
    {
        if (cores <= 2 || availMb < 4096) return (1024, 6, 8);
        if (cores <= 4) return (Math.Clamp((long)(availMb * 0.6), 1024, 4096), 10, 20);
        return (Math.Clamp((long)(availMb * 0.6), 2048, 8192), 16, 40);
    }

    /// <summary>固定档位：测试低配 / 高配（推荐 = 动态，由调用方 Compute 现算）</summary>
    public static (long XmxMb, int ViewDistance, int MaxPlayers) Fixed(Preset preset) => preset switch
    {
        Preset.Low => (1024, 4, 5),
        Preset.High => (8192, 16, 40),
        _ => (0, 0, 0),
    };
}
