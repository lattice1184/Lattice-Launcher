namespace Launcher.Core.Download;

/// <summary>
/// 下载源映射：把官方 URL 映射到镜像源。默认直连 Mojang，可切换 BMCLAPI。
/// </summary>
public interface IDlSourceMapper
{
    string Map(string url);
}

/// <summary>官方 URL → 候选源列表（按优先级，去重）；失败时依次尝试</summary>
public interface IDlSourceResolver
{
    IReadOnlyList<string> Resolve(string officialUrl);
}

/// <summary>
/// 候选源解析：官方优先，回退镜像（BMCLAPI）。不可映射的 URL 只有单一候选。
/// </summary>
public sealed class ResolvingDlSourceMapper : IDlSourceResolver
{
    private readonly IDlSourceMapper _primary;
    private readonly IDlSourceMapper? _fallback;

    public ResolvingDlSourceMapper(IDlSourceMapper primary, IDlSourceMapper? fallback = null)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public static ResolvingDlSourceMapper Default { get; } =
        new(new DefaultDlSourceMapper(), new BmclapiDlSourceMapper());

    public IReadOnlyList<string> Resolve(string url)
    {
        var list = new List<string> { _primary.Map(url) };
        if (_fallback is not null)
        {
            var alt = _fallback.Map(url);
            if (!list.Contains(alt)) list.Add(alt);
        }
        return list;
    }
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
        // 8-20 下载提速：Modrinth 文件 CDN 备用域名竞速——官方 cdn.modrinth.com 实测 307 重定向到
        // cdn-alt.modrinth.com（国内实测 88KB/s vs 官方 275B/s，快 300 倍但波动大）；
        // 双候选走引擎竞速：谁快用谁，波动由竞速兜底
        if (url.Contains("cdn.modrinth.com"))
            return url.Replace("https://cdn.modrinth.com", "https://cdn-alt.modrinth.com");
        if (url.Contains("piston-meta.mojang.com") || url.Contains("launcher.mojang.com")
            || url.Contains("resources.download.minecraft.net"))
        {
            return url.Replace("https://piston-meta.mojang.com", Mirror)
                      .Replace("https://launcher.mojang.com", Mirror)
                      .Replace("https://resources.download.minecraft.net", Mirror);
        }
        // AL39：maven.fabricmc.net 也走镜像——loader 本体/库从该域直连国内实测 21.5s（08-09），
        // 与 libraries.minecraft.net 同格式（bmclapi2/maven/...）；镜像不可达时多源竞速自动回退原源
        // 8-14 补 maven.minecraftforge.net：Forge 安装器/库国内直连实测 37-81KB/s 判死 2 轮失败
        // （整合包 1.20.1-47.4.0 实机）；BMCLAPI /maven 已验证 200 镜像 forge（302→minio 6MB）
        if (url.Contains("maven.fabricmc.net") || url.Contains("libraries.minecraft.net")
            || url.Contains("maven.minecraftforge.net"))
        {
            return url.Replace("https://maven.fabricmc.net", $"{Mirror}/maven")
                      .Replace("https://libraries.minecraft.net", $"{Mirror}/maven")
                      .Replace("https://maven.minecraftforge.net", $"{Mirror}/maven");
        }
        return url;
    }
}
