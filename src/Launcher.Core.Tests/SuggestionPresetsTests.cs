using Launcher.Core.Server;

namespace Launcher.Core.Tests;

/// <summary>开服建议配置档位：低配测试 / 动态推荐（按核数与内存）/ 高配</summary>
public class SuggestionPresetsTests
{
    [Fact]
    public void Compute_LowEndMachine_TinyTier()
    {
        var (xmx, view, players) = SuggestionPresets.Compute(2, 2048); // 2 核 + 2G 可用
        Assert.Equal(1024, xmx);
        Assert.Equal(6, view);
        Assert.Equal(8, players);
    }

    [Fact]
    public void Compute_MidMachine_Standard()
    {
        var (xmx, view, players) = SuggestionPresets.Compute(4, 8192);
        Assert.Equal(10, view);
        Assert.Equal(20, players);
        Assert.InRange(xmx, 1024, 4096);
    }

    [Fact]
    public void Compute_HighEnd_Spacious()
    {
        var (_, view, players) = SuggestionPresets.Compute(16, 32768);
        Assert.Equal(16, view);
        Assert.Equal(40, players);
    }

    [Fact]
    public void Fixed_LowAndHigh()
    {
        Assert.Equal((1024L, 4, 5), SuggestionPresets.Fixed(SuggestionPresets.Preset.Low));
        Assert.Equal((8192L, 16, 40), SuggestionPresets.Fixed(SuggestionPresets.Preset.High));
    }
}
