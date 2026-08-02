using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>本启动器安装标记：写入/判定/清理（来源标签区分 PCL2 扫描版本）</summary>
public class InstallMarkerTests
{
    [Fact]
    public void Mark_ThenIsMarked_True()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"marker-{Guid.NewGuid():N}");
        try
        {
            InstallMarker.Mark(dir, "1.21.1");
            Assert.True(InstallMarker.IsMarked(dir, "1.21.1"));
            Assert.True(File.Exists(Path.Combine(dir, "versions", "1.21.1", ".yanla-installed")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unmarked_IsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"marker-{Guid.NewGuid():N}");
        try
        {
            Assert.False(InstallMarker.IsMarked(dir, "1.21.1")); // 无标记（PCL2 扫描的版本）
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unmark_Removes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"marker-{Guid.NewGuid():N}");
        try
        {
            InstallMarker.Mark(dir, "26.2");
            InstallMarker.Unmark(dir, "26.2");
            Assert.False(InstallMarker.IsMarked(dir, "26.2"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
