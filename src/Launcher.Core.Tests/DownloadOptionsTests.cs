using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>下载配置默认值</summary>
public class DownloadOptionsTests
{
    [Fact]
    public void Defaults_AreSane()
    {
        var o = DownloadOptions.Default;
        Assert.Equal(8, o.LibraryConcurrency);
        Assert.Equal(16, o.AssetConcurrency);
        Assert.Equal(8, o.ChunkCount);
        Assert.Equal(3, o.MaxSourceAttempts);
        Assert.True(o.MirrorFallbackEnabled);
    }
}
