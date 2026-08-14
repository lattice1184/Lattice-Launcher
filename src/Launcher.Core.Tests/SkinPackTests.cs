using System.IO.Compression;
using Launcher.Core.Launch;

namespace Launcher.Core.Tests;

/// <summary>8-13 皮肤资源包：zip 生成 / options.txt 注入幂等 / pack_format 映射</summary>
public class SkinPackTests
{
    [Fact]
    public void Apply_WritesPackAndInjectsOptions()
    {
        var dir = TempDir();
        try
        {
            var skin = Path.Combine(dir, "skin.png");
            File.WriteAllBytes(skin, [1, 2, 3]);

            SkinPack.Apply(dir, skin, 15);

            var pack = Path.Combine(dir, "resourcepacks", SkinPack.PackFileName);
            Assert.True(File.Exists(pack));
            using var zip = ZipFile.OpenRead(pack);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("pack.mcmeta", names);
            Assert.Contains("assets/minecraft/textures/entity/steve.png", names);
            Assert.Contains("assets/minecraft/textures/entity/alex.png", names);
            // options.txt 注入
            var options = File.ReadAllText(Path.Combine(dir, "options.txt"));
            Assert.Contains("resourcePacks:[\"LatticeSkin.zip\"]", options);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Apply_ExistingResourcePacksLine_MergesAndIdempotent()
    {
        var dir = TempDir();
        try
        {
            var skin = Path.Combine(dir, "skin.png");
            File.WriteAllBytes(skin, [1]);
            File.WriteAllLines(Path.Combine(dir, "options.txt"), ["resourcePacks:[\"Other.zip\"]", "lang:zh_cn"]);

            SkinPack.Apply(dir, skin, 15);
            SkinPack.Apply(dir, skin, 15); // 重复注入幂等

            var lines = File.ReadAllLines(Path.Combine(dir, "options.txt"));
            var rp = lines.Single(l => l.StartsWith("resourcePacks:"));
            Assert.Equal("resourcePacks:[\"Other.zip\",\"LatticeSkin.zip\"]", rp);
            Assert.Contains("lang:zh_cn", lines); // 其他行原样保留
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Apply_MissingSkin_NoOp()
    {
        var dir = TempDir();
        try
        {
            SkinPack.Apply(dir, Path.Combine(dir, "nope.png"), 15);
            Assert.False(File.Exists(Path.Combine(dir, "options.txt")));
            Assert.False(Directory.Exists(Path.Combine(dir, "resourcepacks")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MergeArray_Malformed_FallsBackToSingleItem()
    {
        Assert.Equal("[\"LatticeSkin.zip\"]", SkinPack.MergeArray("[]", "LatticeSkin.zip"));
        Assert.Equal("[\"A\",\"LatticeSkin.zip\"]", SkinPack.MergeArray("[\"A\"]", "LatticeSkin.zip"));
        Assert.Equal("[\"LatticeSkin.zip\"]", SkinPack.MergeArray("garbage", "LatticeSkin.zip"));
    }

    [Fact]
    public void IsSupportedSize_OnlySkinFormats()
    {
        // 8-13 皮肤尺寸校验：图标/截图等杂图拒绝（拖入换肤防呆）
        Assert.True(SkinPack.IsSupportedSize(64, 64));
        Assert.True(SkinPack.IsSupportedSize(64, 32));
        Assert.False(SkinPack.IsSupportedSize(256, 256)); // 图标
        Assert.False(SkinPack.IsSupportedSize(1920, 1080)); // 截图
        Assert.False(SkinPack.IsSupportedSize(128, 128)); // 高清皮肤（游戏内无 mod 会错位）
    }

    [Fact]
    public void PackFormatFor_MapsVersions()
    {
        Assert.Equal(15, SkinPack.PackFormatFor("1.20.1"));
        Assert.Equal(32, SkinPack.PackFormatFor("1.20.6"));
        Assert.Equal(34, SkinPack.PackFormatFor("1.21.1"));
        Assert.Equal(34, SkinPack.PackFormatFor("1.21.4"));
        Assert.Equal(13, SkinPack.PackFormatFor("1.19.2"));
        Assert.Equal(15, SkinPack.PackFormatFor("unknown"));
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"skinpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
