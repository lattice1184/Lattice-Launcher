using System.Diagnostics;

namespace Launcher.Core.Multiplayer;

/// <summary>
/// Windows 防火墙规则管理（局域网联机 UDP 端口入站放行）。
/// 只在创建房间时检测/放行（弹一次 UAC）；加入侧出站连接默认放行无需处理。
/// 规则名纯 ASCII——netsh 输出在中文系统按 GBK、其他按 UTF-8 解码，ASCII 子串两种编码下都原样，Contains 匹配可靠。
/// profile=private：只放行专用网络（家庭/工作网），公共 WiFi 不放行。
/// </summary>
public static class FirewallRules
{
    private const string RuleName = "Lattice LAN Multiplayer UDP 34198";

    /// <summary>规则是否已存在（netsh 查询，5s 超时；异常视为不存在）</summary>
    public static bool RuleExists()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh",
                $"advfirewall firewall show rule name=\"{RuleName}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(5000)) return false;
            return output.Contains(RuleName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>添加入站 UDP 放行规则（UAC 提权；用户拒绝/失败返回 false）</summary>
    public static bool TryAddRule()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh",
                $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=UDP localport={LanDiscoveryService.DefaultPort} profile=private")
            {
                Verb = "runas",
                UseShellExecute = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(30_000); // 等 netsh 写完规则再验证
            return RuleExists();
        }
        catch (Exception) { return false; } // Win32Exception(1223) = 用户取消 UAC
    }

    /// <summary>手动放行文案（提权被拒/失败时的兜底引导）</summary>
    public static string ManualHint() =>
        $"Windows 搜索「Windows Defender 防火墙」→ 高级设置 → 入站规则 → 新建规则：端口（UDP {LanDiscoveryService.DefaultPort}）→ 允许连接 → 配置文件勾选「专用」。";
}
