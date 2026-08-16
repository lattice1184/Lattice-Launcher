using System.Diagnostics;

namespace Launcher.Core.Launch;

/// <summary>安装器子进程（Forge/NeoForge --installClient）：流式输出 + 退出码</summary>
public static class InstallerProcess
{
    public static async Task<int> RunAsync(string javaPath, string[] args, Action<string>? onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine?.Invoke(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 8-22 全栈排查：取消必须杀安装器——否则 java 继续后台写 versions/，
            // 用户立刻重试/装别的版本 → 两安装器并发写同目录装出损坏版本
            try { process.Kill(entireProcessTree: true); } catch { /* 进程已退出 */ }
            throw;
        }
        return process.ExitCode;
    }
}
