using System.IO;
using System.Text.Json;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Tests;

/// <summary>
/// 四代真实 version.json 固化测试（官方原样拉取：1.21.1 / 1.20.4 / 1.12.2 / 1.8.9）
/// </summary>
public class MojangModelTests
{
    private static readonly string[] TestVersions = ["1.21.1", "1.20.4", "1.12.2", "1.8.9"];

    private static VersionJson Load(string id)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "versions", $"{id}.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<VersionJson>(json)!;
    }

    [Theory]
    [InlineData("1.21.1")]
    [InlineData("1.20.4")]
    [InlineData("1.12.2")]
    [InlineData("1.8.9")]
    public void Parse_RealVersionJson_Succeeds(string id)
    {
        var v = Load(id);
        Assert.Equal(id, v.Id);
        Assert.NotNull(v.Libraries);
        Assert.True(v.Libraries!.Count > 10, "libraries 应超过 10 个");
        Assert.False(string.IsNullOrEmpty(v.MainClass));
    }

    [Theory]
    [InlineData("1.21.1")]
    [InlineData("1.20.4")]
    public void ModernVersions_UseArgumentsJson(string id)
    {
        var v = Load(id);
        Assert.NotNull(v.Arguments);
        Assert.NotNull(v.Arguments!.Game);
        Assert.True(v.Arguments.Game!.Count > 3, "game 参数应包含多个条目");
        Assert.True(v.Arguments.Jvm!.Count > 5, "jvm 参数应包含多个条目");
        Assert.Null(v.MinecraftArguments);
    }

    [Theory]
    [InlineData("1.12.2")]
    [InlineData("1.8.9")]
    public void LegacyVersions_UseMinecraftArguments(string id)
    {
        var v = Load(id);
        Assert.NotNull(v.MinecraftArguments);
        Assert.Contains("${auth_player_name}", v.MinecraftArguments);
        Assert.True(v.Arguments is null, "旧版本不应有 arguments 块");
    }

    [Fact]
    public void ModernVersion_HasAssetIndexAndJavaVersion()
    {
        var v = Load("1.21.1");
        Assert.NotNull(v.AssetIndex);
        Assert.NotNull(v.AssetIndex!.Url);
        Assert.Equal(21, v.JavaVersion!.MajorVersion);
        Assert.NotNull(v.Downloads?.Client);
    }

    [Fact]
    public void ModernVersion_HasLoggingConfig()
    {
        var v = Load("1.21.1");
        Assert.NotNull(v.Logging?.Client);
        Assert.Contains("-Dlog4j.configurationFile", v.Logging.Client.Argument);
    }
}
