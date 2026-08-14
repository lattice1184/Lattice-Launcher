using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>同名文件自动加 (1) (2) 后缀，永不覆盖</summary>
public class UniquePathTests
{
    [Fact]
    public void Resolve_NotExists_ReturnsOriginal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unique-{Guid.NewGuid():N}.jar");
        Assert.Equal(path, UniquePath.Resolve(path));
    }

    [Fact]
    public void Resolve_Exists_AppendsNumber()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"unique-dir-{Guid.NewGuid():N}"));
        try
        {
            var path = Path.Combine(dir.FullName, "mod.jar");
            File.WriteAllText(path, "x");
            var first = UniquePath.Resolve(path);
            Assert.Equal(Path.Combine(dir.FullName, "mod (1).jar"), first);
            File.WriteAllText(first, "x");
            Assert.Equal(Path.Combine(dir.FullName, "mod (2).jar"), UniquePath.Resolve(path));
        }
        finally { Directory.Delete(dir.FullName, true); }
    }

    [Fact]
    public void Resolve_KeepsExtension()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"unique-dir-{Guid.NewGuid():N}"));
        try
        {
            var path = Path.Combine(dir.FullName, "pack.zip");
            File.WriteAllText(path, "x");
            var resolved = UniquePath.Resolve(path);
            Assert.EndsWith(" (1).zip", resolved);
        }
        finally { Directory.Delete(dir.FullName, true); }
    }
}
