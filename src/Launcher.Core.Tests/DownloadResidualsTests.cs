using System.Net;
using System.Net.Http;
using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>8-18 终态失败清理：中间产物（.tmp/.parts/.race*）全清，destPath 本体永不动（幂等语义）</summary>
public class DownloadResidualsTests
{
    [Fact]
    public void CleanupResiduals_DeletesAll_GhostsDestPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"resid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "f.bin");
        try
        {
            File.WriteAllText(dest, "SENTINEL");                                  // dest 本体（哨兵）
            File.WriteAllText(dest + ".tmp", "x");                                // 单连接暂存
            Directory.CreateDirectory(dest + ".parts");                           // 分片目录
            File.WriteAllText(Path.Combine(dest + ".parts", "0.part"), "x");
            File.WriteAllText(dest + ".race0", "x");                              // 竞速残留系列
            File.WriteAllText(dest + ".race0.tmp", "x");
            Directory.CreateDirectory(dest + ".race0.parts");
            File.WriteAllText(Path.Combine(dest + ".race0.parts", "0.part"), "x");

            DownloadService.CleanupResiduals(dest);

            Assert.Equal("SENTINEL", File.ReadAllText(dest));                     // dest 原样
            Assert.False(File.Exists(dest + ".tmp"));
            Assert.False(Directory.Exists(dest + ".parts"));
            Assert.False(File.Exists(dest + ".race0"));
            Assert.False(File.Exists(dest + ".race0.tmp"));
            Assert.False(Directory.Exists(dest + ".race0.parts"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task TaskTerminalFailure_CleansResiduals_KeepsDest()
    {
        // 任务层真正终态失败（不可重试的异常）→ 清中间产物、dest 本体不动
        // （Service 层 attempt 耗尽不清——Task 自动重试还要靠 .parts 换源续传，清理只能在终态）
        var ctx = new DeferredSyncContext();
        var mgr = new DownloadManager(ctx, 0);
        var dir = Path.Combine(Path.GetTempPath(), $"resid2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "f.bin");
        try
        {
            File.WriteAllText(dest, "KEEP");
            File.WriteAllText(dest + ".tmp", "x");
            Directory.CreateDirectory(dest + ".parts");
            File.WriteAllText(Path.Combine(dest + ".parts", "0.part"), "x");

            var task = mgr.Enqueue("终败", (_, _) => throw new InvalidOperationException("下载失败"),
                "https://example.com/f.bin", dest);
            await task.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            ctx.Drain();

            Assert.Equal(DownloadTaskState.Failed, task.State);
            Assert.Equal("KEEP", File.ReadAllText(dest));
            Assert.False(File.Exists(dest + ".tmp"));
            Assert.False(Directory.Exists(dest + ".parts"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
