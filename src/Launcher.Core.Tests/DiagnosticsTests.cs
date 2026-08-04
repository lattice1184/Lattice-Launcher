using System.IO.Compression;
using Launcher.Core.Diagnostics;
using Launcher.Core.Launch;

namespace Launcher.Core.Tests;

/// <summary>AL9 自修复引擎：规则分派（FixKind 归类）与 ExtractNatives 单测</summary>
public class DiagnosticsTests
{
    [Theory]
    [InlineData("Error: Could not find or load main class net.minecraft.client.main.Main", FixKind.Redownload)]
    [InlineData("java.lang.ClassNotFoundException: net.fabricmc.loader.impl.launch.knot.KnotClient", FixKind.Redownload)]
    [InlineData("java.lang.NoClassDefFoundError: cpw/mods/modlauncher/Launcher", FixKind.Redownload)]
    [InlineData("Error: Unable to access jarfile server.jar", FixKind.Redownload)]
    [InlineData("Failed to load main manifest attribute from a.jar", FixKind.Redownload)]
    [InlineData("no lwjgl64 in java.library.path", FixKind.ReExtractNatives)]
    [InlineData("java.lang.UnsatisfiedLinkError: Could not load library lwjgl.dll", FixKind.ReExtractNatives)]
    [InlineData("java.lang.OutOfMemoryError: Java heap space", FixKind.AdviceOnly)]
    [InlineData("java.lang.UnsupportedClassVersionError: 61.0 has been compiled by a more recent version", FixKind.AdviceOnly)]
    [InlineData("java.net.BindException: Address already in use", FixKind.AdviceOnly)]
    public void DiagnoseDetailed_ClassifiesFixKind(string logLine, FixKind expected)
    {
        var hits = LogDiagnostics.DiagnoseDetailed(logLine);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Fix == expected);
    }

    [Fact]
    public void DiagnoseDetailed_SamePatternReportedOnce()
    {
        var text = "Error: Could not find or load main class X\nError: Could not find or load main class Y";

        var hits = LogDiagnostics.DiagnoseDetailed(text);

        Assert.Single(hits);
        Assert.Equal(FixKind.Redownload, hits[0].Fix);
    }

    [Fact]
    public void DiagnoseDetailed_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(LogDiagnostics.DiagnoseDetailed(""));
        Assert.Empty(LogDiagnostics.DiagnoseDetailed("   \n  "));
        Assert.Empty(LogDiagnostics.DiagnoseDetailed("正常日志，没有已知错误模式"));
    }

    [Fact]
    public void Diagnose_LegacyWrapper_KeepsOutputFormat()
    {
        var lines = LogDiagnostics.Diagnose("Error: Could not find or load main class X");

        Assert.Single(lines);
        Assert.StartsWith("▸ 匹配：", lines[0]);
        Assert.Contains("说明：", lines[0]);
    }

    [Fact]
    public void ExtractNatives_ExtractsDlls_AndClearFirstWipesResidual()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"nat-{Guid.NewGuid():N}");
        var nativesDir = Path.Combine(dir, "natives");
        var jarPath = Path.Combine(dir, "lwjgl.jar");
        Directory.CreateDirectory(nativesDir);
        try
        {
            // 造一个含 dll（应提取）+ 其他文件（忽略）的假 natives jar
            using (var zip = ZipFile.Open(jarPath, ZipArchiveMode.Create))
            {
                using (var e1 = zip.CreateEntry("lib/lwjgl.dll").Open())
                using (var w1 = new StreamWriter(e1))
                    w1.Write("dll-data");
                using (var e2 = zip.CreateEntry("lib/README.txt").Open())
                using (var w2 = new StreamWriter(e2))
                    w2.Write("ignore");
            }
            // 残留文件：clearFirst 应清除
            File.WriteAllText(Path.Combine(nativesDir, "stale.dll"), "old");

            GameLaunchService.ExtractNatives([jarPath], nativesDir, clearFirst: true);

            Assert.True(File.Exists(Path.Combine(nativesDir, "lwjgl.dll")), "dll 应被解压到 natives 根目录");
            Assert.False(File.Exists(Path.Combine(nativesDir, "README.txt")), "非 dll 文件应被忽略");
            Assert.False(File.Exists(Path.Combine(nativesDir, "stale.dll")), "clearFirst 应清除残留");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
