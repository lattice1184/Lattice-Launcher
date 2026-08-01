namespace Launcher.Core.Download;

/// <summary>
/// 下载源映射：把官方 URL 映射到镜像源。默认直连 Mojang，可切换 BMCLAPI。
/// </summary>
public interface IDlSourceMapper
{
    string Map(string url);
}

/// <summary>官方源直连</summary>
public sealed class DefaultDlSourceMapper : IDlSourceMapper
{
    public string Map(string url) => url;
}

/// <summary>
/// BMCLAPI 镜像（国内加速）：piston-meta / launcher / resources / libraries 全部走 bmclapi2
/// </summary>
public sealed class BmclapiDlSourceMapper : IDlSourceMapper
{
    private const string Mirror = "https://bmclapi2.bangbang93.com";

    public string Map(string url)
    {
        if (url.Contains("piston-meta.mojang.com") || url.Contains("launcher.mojang.com")
            || url.Contains("resources.download.minecraft.net"))
        {
            return url.Replace("https://piston-meta.mojang.com", Mirror)
                      .Replace("https://launcher.mojang.com", Mirror)
                      .Replace("https://resources.download.minecraft.net", Mirror);
        }
        if (url.Contains("libraries.minecraft.net"))
        {
            return url.Replace("https://libraries.minecraft.net", $"{Mirror}/maven");
        }
        return url;
    }
}
