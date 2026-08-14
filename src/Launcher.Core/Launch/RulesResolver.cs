using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Launcher.Core.Model.Mojang;

namespace Launcher.Core.Launch;

/// <summary>
/// Mojang 版本 JSON 的 rules 求值器。
/// 规则语义：全部规则求值后，最后一条匹配的规则决定 allow/disallow；无规则 = allow。
/// </summary>
public sealed class RulesResolver
{
    /// <summary>当前操作系统名：windows / linux / osx</summary>
    public string OsName { get; init; } = DetectOsName();

    /// <summary>操作系统版本字符串（用于 os.version 正则匹配）</summary>
    public string OsVersion { get; init; } = Environment.OSVersion.VersionString;

    /// <summary>Java 架构名（x86/x64/arm64），由 os.arch 归一化</summary>
    public string Arch { get; init; } = NormalizeArch(RuntimeInformation.ProcessArchitecture);

    /// <summary>功能开关（如 has_custom_resolution），默认全 false</summary>
    public IReadOnlyDictionary<string, bool> Features { get; init; } = new Dictionary<string, bool>();

    public bool IsAllowed(IReadOnlyList<RuleJson>? rules)
    {
        if (rules is null || rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!Match(rule)) continue;
            allowed = rule.Action == "allow";
        }
        return allowed;
    }

    private bool Match(RuleJson rule)
    {
        if (rule.Os is { } os)
        {
            if (os.Name is not null && !os.Name.Equals(OsName, StringComparison.OrdinalIgnoreCase)) return false;
            if (os.Version is not null)
            {
                try { if (!Regex.IsMatch(OsVersion, os.Version)) return false; }
                catch (ArgumentException) { return false; }
            }
            if (os.Arch is not null && !os.Arch.Equals(Arch, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (rule.Features is { } features)
        {
            foreach (var (key, expected) in features)
            {
                if (Features.TryGetValue(key, out var actual) ? actual != expected : expected) return false;
            }
        }
        return true;
    }

    private static string DetectOsName()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "osx";
        return "unknown";
    }

    private static string NormalizeArch(System.Runtime.InteropServices.Architecture arch) => arch switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.X86 => "x86",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        _ => "x64",
    };
}
