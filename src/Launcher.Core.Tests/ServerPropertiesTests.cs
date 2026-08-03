using Launcher.Core.Server;

namespace Launcher.Core.Tests;

/// <summary>server.properties 解析/默认值/保存往返</summary>
public class ServerPropertiesTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"srv-{Guid.NewGuid():N}.properties");

    [Fact]
    public void Load_ParsesKeyValues_SkipsComments()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "#Minecraft server properties\nserver-port=25565\nmax-players=20\n\nonline-mode=true\n");
            var props = ServerProperties.Load(path);
            Assert.Equal("25565", props.Get("server-port"));
            Assert.Equal("20", props.Get("max-players"));
            Assert.Equal("true", props.Get("online-mode"));
            Assert.Equal("", props.Get("nonexistent"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void GetInt_GetBool_Fallbacks()
    {
        var props = ServerProperties.Load(Path.Combine(Path.GetTempPath(), "missing.properties"));
        Assert.Equal(0, props.GetInt("server-port"));
        Assert.False(props.GetBool("online-mode"));
        props.Set("server-port", "25565");
        props.Set("online-mode", "true");
        Assert.Equal(25565, props.GetInt("server-port"));
        Assert.True(props.GetBool("online-mode"));
    }

    [Fact]
    public void Save_RoundTrip()
    {
        var path = TempFile();
        try
        {
            var props = new ServerProperties();
            props.Set("server-port", "25565");
            props.Set("level-name", "world");
            props.Save(path);

            var loaded = ServerProperties.Load(path);
            Assert.Equal("25565", loaded.Get("server-port"));
            Assert.Equal("world", loaded.Get("level-name"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AcceptEula_WritesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"eula-{Guid.NewGuid():N}");
        try
        {
            ServerInstaller.AcceptEula(dir);
            Assert.Equal("eula=true", File.ReadAllText(Path.Combine(dir, "eula.txt")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
