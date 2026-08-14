using Launcher.Core.Launch;
using Launcher.Core.Multiplayer;

namespace Launcher.Core.Diagnostics;

/// <summary>
/// 统一失败诊断（AL44）：把各模块的结构化失败（联机枚举 / 下载异常 / 启动异常）映射为
/// DiagnosticHit（人话原因 + 建议 + 修复动作）——与 LogDiagnostics 的日志正则诊断并列，
/// 覆盖「无日志可扫」的结构化失败路径。扩展方式：往映射表追加键值。
/// </summary>
public static class FailureDiagnostics
{
    private static readonly IReadOnlyDictionary<MultiplayerLobbyFailure, (string Reason, string Suggestion, FixKind Fix)> TerracottaMap =
        new Dictionary<MultiplayerLobbyFailure, (string, string, FixKind)>
        {
            [MultiplayerLobbyFailure.InvalidRoomCode] = (
                "房间码无效（服务器返回 400）。",
                "检查房间码完整性，注意 0/O、1/l 这类易混字符。",
                FixKind.AdviceOnly),
            [MultiplayerLobbyFailure.WorldUnavailable] = (
                "未检测到局域网世界（20 秒超时）：房主没开「局域网世界」，或双方不在同一网络。",
                "房主在游戏里 Esc →「对局域网开放」后再回来创建房间；仍超时检查网络与防火墙。",
                FixKind.AdviceOnly),
            [MultiplayerLobbyFailure.BackendUnavailable] = (
                "联机模块不可用（未安装/安装损坏/下载失败）。",
                "点「一键修复」自动重装联机模块（约 15 MB）。",
                FixKind.ReinstallModule),
            [MultiplayerLobbyFailure.BackendBusy] = (
                "联机服务正被其他启动器占用，或模块版本不匹配（常见：本机有残留陶瓦进程抢占端口）。",
                "点「一键修复」结束残留进程后自动重试；若双方版本不一致，请使用同一版本模块。",
                FixKind.RestartService),
            [MultiplayerLobbyFailure.ProtocolFailed] = (
                "联机模块接口异常。",
                "点「一键修复」重启联机服务；反复出现请重装联机模块。",
                FixKind.RestartService),
            [MultiplayerLobbyFailure.StartupFailed] = (
                "创建房间失败：联机模块启动异常（退出或握手超时）。",
                "点「一键修复」重启联机服务后自动重试。",
                FixKind.RestartService),
            [MultiplayerLobbyFailure.RoomConnectionFailed] = (
                "加入房间失败：连不上房主或加入超时。",
                "确认房间码未过期、双方已开启局域网世界且网络互通；提示版本不匹配时，请与房主对齐联机模块版本。",
                FixKind.AdviceOnly),
            [MultiplayerLobbyFailure.Cancelled] = (
                "操作已取消。",
                "",
                FixKind.AdviceOnly),
            [MultiplayerLobbyFailure.NetworkFailed] = (
                "组网失败：没连上对方（防火墙拦截/不在同一网络/NAT 类型受限）。",
                "检查双方防火墙是否放行；同一局域网内必通；跨网段走公网中继时确认网络可达。",
                FixKind.AdviceOnly),
        };

    /// <summary>全部联机失败键（测试枚举覆盖用：防未来追加枚举值漏映射）</summary>
    public static IEnumerable<MultiplayerLobbyFailure> TerracottaKeys => TerracottaMap.Keys;

    /// <summary>联机失败 → 诊断（Snippet=枚举名；detail 为底层异常消息，嵌入原因）</summary>
    public static DiagnosticHit ForMultiplayer(MultiplayerLobbyFailure failure, string? detail = null)
    {
        var (reason, suggestion, fix) = TerracottaMap[failure];
        var explanation = reason;
        if (!string.IsNullOrWhiteSpace(detail))
            explanation += $" 详情：{detail}";
        if (!string.IsNullOrWhiteSpace(suggestion))
            explanation += $" 建议：{suggestion}";
        return new DiagnosticHit(failure.ToString(), explanation, fix);
    }

    /// <summary>
    /// 下载失败 → 诊断：网络类（HttpRequestException/超时）首败 RetryDownload、重试仍败 CheckNetwork；
    /// 校验失败（InvalidDataException=SHA1/大小不符）Redownload（重下即修复）；未知异常 null（不诊断不自动重试）。
    /// </summary>
    public static DiagnosticHit? ForDownload(Exception ex, bool alreadyRetried = false)
    {
        if (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return alreadyRetried
                ? new DiagnosticHit("网络异常", "网络或下载源异常，已自动重试一次仍未成功。检查网络后重试，或稍后再试。", FixKind.CheckNetwork)
                : new DiagnosticHit("网络异常", "网络或下载源异常，已自动重试一次。", FixKind.RetryDownload);
        }
        if (ex is InvalidDataException)
        {
            return new DiagnosticHit("文件校验失败", "下载的文件校验失败（可能不完整或被篡改），将自动重新下载。", FixKind.Redownload);
        }
        return null;
    }

    /// <summary>启动失败 → 诊断：父版本缺失/文件缺失 → Redownload（自动补全重下）</summary>
    public static DiagnosticHit ForLaunch(Exception ex)
    {
        if (ex is ParentVersionMissingException or FileNotFoundException)
            return new DiagnosticHit("版本文件缺失", "版本文件缺失（含父版本），将自动重新下载补全。", FixKind.Redownload);
        return new DiagnosticHit(ex.GetType().Name, ex.Message, FixKind.AdviceOnly);
    }
}
