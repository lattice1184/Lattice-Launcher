using Launcher.Core.Diagnostics;
using Launcher.Core.Launch;
using Launcher.Core.Multiplayer;

namespace Launcher.Core.Tests;

/// <summary>统一失败诊断（AL44）：联机枚举/下载异常/启动异常 → DiagnosticHit 映射</summary>
public class FailureDiagnosticsTests
{
    // ---------- 联机 ----------

    [Fact]
    public void TerracottaMap_CoversAllEnumValues()
    {
        // 防未来追加枚举值漏映射（缺映射会 KeyNotFound 崩溃）
        var missing = Enum.GetValues<TerracottaLobbyFailure>()
            .Where(v => !FailureDiagnostics.TerracottaKeys.Contains(v))
            .Select(v => v.ToString())
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void ForTerracotta_AllEntries_HaveReasonAndFix()
    {
        foreach (var key in FailureDiagnostics.TerracottaKeys)
        {
            var hit = FailureDiagnostics.ForTerracotta(key);
            Assert.False(string.IsNullOrWhiteSpace(hit.Explanation), $"{key} 文案为空");
            // Cancelled 无建议（用户主动取消）；其余必须带建议段
            if (key != TerracottaLobbyFailure.Cancelled)
                Assert.True(hit.Explanation.Contains("建议："), $"{key} 缺建议段");
        }
    }

    [Fact]
    public void ForTerracotta_DetailEmbedded()
    {
        var hit = FailureDiagnostics.ForTerracotta(TerracottaLobbyFailure.RoomConnectionFailed, "连不上房主");
        Assert.Contains("连不上房主", hit.Explanation);
        Assert.Contains("建议：", hit.Explanation);
    }

    [Fact]
    public void ForTerracotta_ErrorTextFixes()
    {
        // 关键修复动作分类（真机 08-09 场景）
        Assert.Equal(FixKind.RestartService, FailureDiagnostics.ForTerracotta(TerracottaLobbyFailure.TerracottaBusy).Fix);
        Assert.Equal(FixKind.RestartService, FailureDiagnostics.ForTerracotta(TerracottaLobbyFailure.ProtocolFailed).Fix);
        Assert.Equal(FixKind.RestartService, FailureDiagnostics.ForTerracotta(TerracottaLobbyFailure.StartupFailed).Fix);
        Assert.Equal(FixKind.ReinstallModule, FailureDiagnostics.ForTerracotta(TerracottaLobbyFailure.TerracottaUnavailable).Fix);
        Assert.Equal(FixKind.AdviceOnly, FailureDiagnostics.ForTerracotta(TerracottaLobbyFailure.RoomConnectionFailed).Fix);
    }

    // ---------- 下载 ----------

    [Fact]
    public void ForDownload_NetworkFirstFailure_RetryDownload()
    {
        var hit = FailureDiagnostics.ForDownload(new HttpRequestException("超时"));
        Assert.NotNull(hit);
        Assert.Equal(FixKind.RetryDownload, hit!.Fix);
        Assert.True(hit.IsAutoFixable);
    }

    [Fact]
    public void ForDownload_NetworkAfterRetry_CheckNetwork()
    {
        var hit = FailureDiagnostics.ForDownload(new HttpRequestException("超时"), alreadyRetried: true);
        Assert.NotNull(hit);
        Assert.Equal(FixKind.CheckNetwork, hit!.Fix);
        Assert.False(hit.IsAutoFixable);
    }

    [Fact]
    public void ForDownload_InvalidData_Redownload()
    {
        var hit = FailureDiagnostics.ForDownload(new InvalidDataException("SHA1 不符"));
        Assert.NotNull(hit);
        Assert.Equal(FixKind.Redownload, hit!.Fix);
    }

    [Fact]
    public void ForDownload_UnknownException_Null()
        => Assert.Null(FailureDiagnostics.ForDownload(new InvalidOperationException("未知")));

    // ---------- 启动 ----------

    [Fact]
    public void ForLaunch_ParentVersionMissing_Redownload()
    {
        var hit = FailureDiagnostics.ForLaunch(new ParentVersionMissingException("缺父版本 26.2"));
        Assert.Equal(FixKind.Redownload, hit.Fix);
        Assert.True(hit.IsAutoFixable);
    }

    [Fact]
    public void ForLaunch_FileNotFound_Redownload()
    {
        var hit = FailureDiagnostics.ForLaunch(new FileNotFoundException("缺 client jar"));
        Assert.Equal(FixKind.Redownload, hit.Fix);
    }
}
