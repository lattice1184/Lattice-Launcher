using Launcher.Core.Download;
using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>下载配置默认值 + 设置映射</summary>
public class DownloadOptionsTests
{
    [Fact]
    public void Defaults_AreSane()
    {
        var o = DownloadOptions.Default;
        Assert.Equal(8, o.LibraryConcurrency);
        Assert.Equal(16, o.AssetConcurrency);
        Assert.Equal(8, o.ChunkCount);
        Assert.Equal(81920, o.BufferSize);
        Assert.Equal(3, o.MaxSourceAttempts);
        Assert.True(o.MirrorFallbackEnabled);
    }

    [Fact]
    public void FromSettings_TierMapsToConcurrency()
    {
        var low = DownloadOptions.FromSettings(new LauncherSettings { DownloadTier = DownloadTier.Low });
        Assert.Equal(8, low.ChunkCount);
        Assert.Equal(8, low.LibraryConcurrency);
        Assert.Equal(16, low.AssetConcurrency);

        var mid = DownloadOptions.FromSettings(new LauncherSettings { DownloadTier = DownloadTier.Medium });
        Assert.Equal(16, mid.ChunkCount);
        Assert.Equal(16, mid.LibraryConcurrency);
        Assert.Equal(32, mid.AssetConcurrency);

        var high = DownloadOptions.FromSettings(new LauncherSettings { DownloadTier = DownloadTier.High });
        Assert.Equal(24, high.ChunkCount);
        Assert.Equal(24, high.LibraryConcurrency);
        Assert.Equal(48, high.AssetConcurrency);
    }

    [Fact]
    public void FromSettings_OverridesWinOverTier()
    {
        var s = new LauncherSettings
        {
            DownloadTier = DownloadTier.Low,
            ChunkCount = 12,
            BufferSize = 4096,
            MaxConcurrentDownloads = 4,
        };
        var o = DownloadOptions.FromSettings(s);
        Assert.Equal(12, o.ChunkCount);
        Assert.Equal(4096, o.BufferSize);
        Assert.Equal(4, o.LibraryConcurrency);
        Assert.Equal(16, o.AssetConcurrency); // max(4*2, 16)
    }

    [Fact]
    public void FromSettings_Defaults()
    {
        var o = DownloadOptions.FromSettings(new LauncherSettings());
        Assert.Equal(81920, o.BufferSize);
        Assert.Equal(8, o.ChunkCount);
        Assert.True(o.MirrorFallbackEnabled);
        Assert.Equal(0, o.BytesPerSecond);
    }
}
