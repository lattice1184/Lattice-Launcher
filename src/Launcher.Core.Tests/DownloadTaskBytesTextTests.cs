using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>AL10.2：未知大小（weight=0 子任务）显示 "--" 而非 "0 B"</summary>
public class DownloadTaskBytesTextTests
{
    [Fact]
    public async Task UnknownSize_ShowsDash()
    {
        var mgr = new DownloadManager();
        var task = mgr.Enqueue("叶子", (_, _) => Task.CompletedTask);
        await task.Completion;
        Assert.Equal(DownloadTaskState.Completed, task.State);
        Assert.Equal("-- / --", task.BytesText);
    }
}
