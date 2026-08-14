using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>第三方下载文件名识别：URL 段 / Content-Disposition（filename、filename*）/ 非法字符清理</summary>
public class UriFileNameResolverTests
{
    [Fact]
    public void FromUrl_LastSegment()
    {
        Assert.Equal("mod.jar", UriFileNameResolver.FromUrl("https://example.com/files/mod.jar"));
        Assert.Equal("pack.zip", UriFileNameResolver.FromUrl("https://example.com/a/b/pack.zip"));
    }

    [Fact]
    public void FromUrl_PercentDecoded()
    {
        Assert.Equal("my mod.jar", UriFileNameResolver.FromUrl("https://example.com/my%20mod.jar"));
        Assert.Equal("光影包.zip", UriFileNameResolver.FromUrl("https://example.com/%E5%85%89%E5%BD%B1%E5%8C%85.zip"));
    }

    [Fact]
    public void FromUrl_NoPath_ReturnsNull()
    {
        Assert.Null(UriFileNameResolver.FromUrl("https://example.com"));
        Assert.Null(UriFileNameResolver.FromUrl("https://example.com/"));
        Assert.Null(UriFileNameResolver.FromUrl("not a url"));
    }

    [Fact]
    public void ParseContentDisposition_PlainFilename()
    {
        Assert.Equal("a.jar", UriFileNameResolver.ParseContentDisposition(@"attachment; filename=""a.jar"""));
        Assert.Equal("a.jar", UriFileNameResolver.ParseContentDisposition("attachment; filename=a.jar"));
    }

    [Fact]
    public void ParseContentDisposition_StarFilename_WinsAndDecodes()
    {
        const string header = "attachment; filename=\"fallback.jar\"; filename*=UTF-8''%E4%B8%AD%E6%96%87.jar";
        Assert.Equal("中文.jar", UriFileNameResolver.ParseContentDisposition(header));
    }

    [Fact]
    public void ParseContentDisposition_NullOrGarbage_ReturnsNull()
    {
        Assert.Null(UriFileNameResolver.ParseContentDisposition(null));
        Assert.Null(UriFileNameResolver.ParseContentDisposition(""));
        Assert.Null(UriFileNameResolver.ParseContentDisposition("attachment; mode=inline"));
    }

    [Fact]
    public void Sanitize_StripsInvalidFileNameChars()
    {
        Assert.Equal("a_b_c_d.jar", UriFileNameResolver.Sanitize("a<b>c:d.jar"));
        // 路径穿越防护：/ 被替换，不会逃出目标目录
        Assert.Equal(".._.._evil.jar", UriFileNameResolver.Sanitize("../../evil.jar"));
        Assert.Null(UriFileNameResolver.Sanitize("   "));
    }
}
