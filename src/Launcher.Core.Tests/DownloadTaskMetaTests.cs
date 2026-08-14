using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>DownloadTask 元数据：Enqueue 传入的 sourceUrl/targetPath（下载历史「重新下载/打开位置」）</summary>
public class DownloadTaskMetaTests
{
    [Fact]
    public void Enqueue_WithSourceAndTarget_SetsFields()
    {
        var mgr = new DownloadManager(null);
        var task = mgr.Enqueue("下载 mod.jar", (_, _) => Task.CompletedTask,
            "https://example.com/files/mod.jar", @"C:\Downloads\mod.jar");

        Assert.Equal("https://example.com/files/mod.jar", task.SourceUrl);
        Assert.Equal(@"C:\Downloads\mod.jar", task.TargetPath);
    }

    [Fact]
    public void Enqueue_WithoutMeta_FieldsAreNull()
    {
        var mgr = new DownloadManager(null);
        var task = mgr.Enqueue("普通任务", (_, _) => Task.CompletedTask);

        Assert.Null(task.SourceUrl);
        Assert.Null(task.TargetPath);
    }
}
