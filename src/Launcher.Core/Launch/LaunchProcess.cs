using System.Diagnostics;

namespace Launcher.Core.Launch;

/// <summary>
/// 游戏进程管理：启动 JVM + 实时日志管道 + 退出检测。
/// </summary>
public sealed class LaunchProcess
{
    public sealed record LaunchResult(Process Process, string ExitStatusFilePath);

    /// <summary>启动游戏进程。日志行通过 onLog 回调实时输出。</summary>
    public static LaunchResult Start(
        JavaArgumentsBuilder.LaunchProfile profile,
        Action<string>? onLog = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = profile.JavaPath,
            WorkingDirectory = profile.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in profile.JvmArgs) psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("-cp");
        psi.ArgumentList.Add(profile.ClassPath);
        psi.ArgumentList.Add(profile.MainClass);
        foreach (var arg in profile.GameArgs) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLog?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLog?.Invoke(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 1.17+ 的 exitStatus 文件：不预写 0（避免掩盖崩溃）；游戏正常退出时自己写入
        var exitFile = Path.Combine(profile.WorkingDirectory, "exitStatus");
        try { if (File.Exists(exitFile)) File.Delete(exitFile); } catch { }

        return new LaunchResult(process, exitFile);
    }

    /// <summary>读取退出状态（-1 = 崩溃/文件缺失）</summary>
    public static int ReadExitStatus(string path)
    {
        try { return int.Parse(File.ReadAllText(path)); }
        catch { return -1; }
    }

    /// <summary>综合退出码：进程 ExitCode 非 0（JVM 崩溃/OOM/被杀）优先，否则游戏写入的 exitStatus</summary>
    public static int GetExitCode(LaunchResult result)
    {
        try
        {
            if (result.Process.HasExited && result.Process.ExitCode != 0)
                return result.Process.ExitCode;
        }
        catch { }
        return ReadExitStatus(result.ExitStatusFilePath);
    }
}
