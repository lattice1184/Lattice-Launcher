using System.Text.RegularExpressions;

namespace Launcher.App.Services;

/// <summary>
/// 日志动态诊断：按实际日志内容正则匹配已知错误模式，逐条补中文说明与建议。
/// 共用方：导出报告（LogExportHelper 生成 诊断说明.txt）+ 服务端异常退出弹窗（ServerViewModel）。
/// 扩展方式：往 Patterns 追加 (正则, 中文说明) 即可。
/// </summary>
public static class LogDiagnostics
{
    private static readonly (Regex Re, string Explanation)[] Patterns =
    [
        (new Regex(@"OutOfMemoryError|Java heap space", RegexOptions.IgnoreCase),
            "内存不足（Java 堆溢出）：分配的内存不够用。可在设置页调高「内存分配」，或关闭占用内存大的程序。"),
        (new Regex(@"Failed to allocate memory|Native memory allocation \(mmap\) failed|Cannot allocate memory", RegexOptions.IgnoreCase),
            "系统内存不足：物理内存不够分配。请关闭其他程序后重试，或调低内存分配。"),
        (new Regex(@"UnsupportedClassVersionError|class file version \d+\.\d+ is invalid", RegexOptions.IgnoreCase),
            "Java 版本过低：该版本需要更高版本的 Java。请在设置页更换新版 Java 路径。"),
        (new Regex(@"Could not create the Java Virtual Machine|Unable to start the JVM", RegexOptions.IgnoreCase),
            "JVM 创建失败：内存参数或 Java 安装异常。检查「内存分配」与 Java 路径。"),
        (new Regex(@"Invalid maximum heap size|Invalid initial heap size", RegexOptions.IgnoreCase),
            "内存参数无效：分配值超出上限或格式错误。请调整「内存分配」。"),
        (new Regex(@"Could not find or load main class", RegexOptions.IgnoreCase),
            "主类加载失败：版本文件或启动参数损坏。请重新下载该版本。"),
        (new Regex(@"UnsatisfiedLinkError|Could not load library|Failed to extract natives", RegexOptions.IgnoreCase),
            "本地库（natives）加载失败：文件缺失或损坏。请删除该版本后重新下载安装。"),
        (new Regex(@"Exception in thread .* GLFW|GLFW error|Failed to init GLFW", RegexOptions.IgnoreCase),
            "图形窗口初始化失败（GLFW）：显卡驱动或窗口环境异常。更新显卡驱动后重试。"),
        (new Regex(@"OpenGL.*(not supported|error|failed)|Error creating GL context", RegexOptions.IgnoreCase),
            "OpenGL 创建失败：显卡驱动过旧或不支持所需版本。请更新显卡驱动。"),
        (new Regex(@"Unexpected error while creating framebuffer|Draw buffers \[\d+, \d+\] Status", RegexOptions.IgnoreCase),
            "渲染帧缓冲创建失败：常见于 Iris 光影与显卡驱动冲突。可尝试关闭光影或更新显卡驱动。"),
        (new Regex(@"Missing or unsupported mandatory dependencies|Could not find required mod|requires .* that is missing|would be incompatible", RegexOptions.IgnoreCase),
            "模组依赖缺失或不兼容：缺少依赖模组或版本冲突。请补全依赖或移除冲突模组。"),
        (new Regex(@"BindException|Address already in use|Port \d+ was already in use", RegexOptions.IgnoreCase),
            "端口被占用：服务端口已被其他程序（或另一个服务端）占用。修改 server.properties 的 server-port 后重试。"),
        (new Regex(@"Segmentation fault|SIGSEGV", RegexOptions.IgnoreCase),
            "程序段错误崩溃：底层崩溃，多为驱动或内存问题。尝试更新驱动或降低渲染设置。"),
        (new Regex(@"java\.lang\.NoClassDefFoundError", RegexOptions.IgnoreCase),
            "缺少类定义：模组或库文件损坏/缺失。请重装对应模组或版本。"),
        (new Regex(@"A fatal error has been detected by the Java Runtime Environment", RegexOptions.IgnoreCase),
            "JVM 致命错误（hs_err）：底层崩溃，多为驱动或硬件问题。可将崩溃文件一并反馈。"),
        (new Regex(@"The required mods are missing|It appears .* did not load correctly|Failed to load mod", RegexOptions.IgnoreCase),
            "模组加载失败：模组文件损坏或与当前版本不兼容。请检查最近安装的模组。"),
    ];

    /// <summary>对日志文本诊断：返回「匹配原文 → 中文说明」列表（同模式只报一次）</summary>
    public static List<string> Diagnose(string logText)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(logText)) return result;
        var seen = new HashSet<string>();
        foreach (var (re, explanation) in Patterns)
        {
            var m = re.Match(logText);
            if (!m.Success) continue;
            if (!seen.Add(explanation)) continue;
            var snippet = m.Value.Trim();
            if (snippet.Length > 80) snippet = snippet[..80];
            result.Add($"▸ 匹配：{snippet}\n  说明：{explanation}");
        }
        return result;
    }
}
