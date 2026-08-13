using Launcher.Core.Download;

namespace Launcher.Core.Tests;

/// <summary>8-15 GitHub 大文件提速：CDN 域判定 + 镜像竞速覆盖（纯函数离线可测）</summary>
public class GitHubSpeedupTests
{
    [Theory]
    [InlineData("https://github.com/foo/bar/releases/download/v1/x.zip", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123", true)]
    [InlineData("https://codeload.github.com/foo/bar/zip/refs/heads/main", true)]
    [InlineData("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", false)]
    [InlineData("https://cdn.modrinth.com/data/abc/1.zip", false)]
    public void IsGitHubCdn_Hosts(string url, bool expected)
        => Assert.Equal(expected, DownloadService.IsGitHubCdn(url));

    [Theory]
    [InlineData("https://github.com/foo/bar/releases/download/v1/x.zip", true)]
    [InlineData("https://objects.githubusercontent.com/abc/def", true)]
    [InlineData("https://codeload.github.com/foo/bar/zip/main", true)]
    [InlineData("https://example.com/file.zip", false)]
    public void Resolver_IsGitHubUrl(string url, bool expected)
        => Assert.Equal(expected, ThirdPartyDlSourceResolver.IsGitHubUrl(url));

    [Fact]
    public void Resolver_SignedUrl_GetsMirrorCandidates()
    {
        // 8-15：签名直链（objects.githubusercontent.com）也要镜像竞速（国内直连几十 KB/s）
        var r = new ThirdPartyDlSourceResolver();
        var signed = "https://objects.githubusercontent.com/github-production-release-asset/abc.zip";
        var list = r.Resolve(signed);
        Assert.Equal(3, list.Count); // 原 + 2 镜像
        Assert.Equal(signed, list[0]);
        Assert.Contains(list, u => u.StartsWith("https://ghproxy.net/"));
        Assert.Contains(list, u => u.StartsWith("https://gh-proxy.com/"));
    }

    [Fact]
    public void Resolver_ReleaseUrl_StillHasGhapiFallback()
    {
        var r = new ThirdPartyDlSourceResolver();
        var release = "https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier.zip";
        var list = r.Resolve(release);
        Assert.Contains(list, u => u.StartsWith("ghapi:"));
    }

    [Fact]
    public void Resolver_NonGithub_SingleCandidate()
    {
        var r = new ThirdPartyDlSourceResolver();
        Assert.Single(r.Resolve("https://example.com/file.zip"));
    }
}
