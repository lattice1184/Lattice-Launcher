using System.Diagnostics;
using System.Text;
using Launcher.Core.Launch;
using Launcher.Core.Utils;

namespace Launcher.Core.Server;

/// <summary>
/// 服务端进程管理：java -Xmx{mem}M -jar server.jar nogui。
/// 输出重定向（stdout/stderr 捕获）、stdin 命令、优雅停止（stop 命令 + 超时强杀）、崩溃检测（退出码）。
/// </summary>
public sealed class ServerProcess : IDisposable
{
    private Process? _process;
    private readonly object _lock = new();

    /// <summary>进程已启动且未退出</summary>
    public bool IsRunning
    {
        get { lock (_lock) return _process is { HasExited: false }; }
    }

    /// <summary>启动命令参数（AL8：供日志/诊断展示；Start 后可用）</summary>
    public string[]? CommandLine { get; private set; }

    /// <summary>输出行（stdout/stderr 合并）</summary>
    public event Action<string>? OutputReceived;

    /// <summary>进程退出（exitCode：0=正常 stop；非 0=崩溃/被杀）</summary>
    public event Action<int>? Exited;

    /// <summary>启动服务端（javaPath 自动选配由调用方解析；此处直用）</summary>
    public void Start(string serverDir, string javaPath, int memoryMb, GamePriority priority = GamePriority.Normal)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
                throw new InvalidOperationException("服务端已在运行");

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = serverDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add($"-Xmx{memoryMb}M");
            psi.ArgumentList.Add("-Xms256M");
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add("server.jar");
            psi.ArgumentList.Add("nogui");
            CommandLine = [.. psi.ArgumentList]; // AL8：记录启动命令供日志展示

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data is { } line) OutputReceived?.Invoke(line); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) OutputReceived?.Invoke(line); };
            _process.Exited += (_, _) =>
            {
                var code = GetExitCode();
                Exited?.Invoke(code);
            };
            if (!_process.Start())
                throw new InvalidOperationException("服务端进程启动失败");
            // 进程优先级（与游戏同设置；失败走控制台提示，不崩）
            try { if (priority != GamePriority.Normal) _process.PriorityClass = LaunchProcess.ToPriorityClass(priority); }
            catch { OutputReceived?.Invoke("§ 设置进程优先级失败（已忽略）"); }
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
    }

    /// <summary>发送控制台命令（stdin）</summary>
    public void SendCommand(string command)
    {
        lock (_lock)
        {
            if (_process is not { HasExited: false } || _process.StandardInput.BaseStream is null) return;
            try { _process.StandardInput.WriteLine(command); _process.StandardInput.Flush(); }
            catch { /* 进程已退出 */ }
        }
    }

    /// <summary>优雅停止：发 stop 命令，30 秒未退出则强杀</summary>
    public void Stop()
    {
        SendCommand("stop");
        try
        {
            if (_process is { HasExited: false } && !_process.WaitForExit(30_000))
                _process.Kill();
        }
        catch { /* 已退出 */ }
    }

    /// <summary>强制终止</summary>
    public void Kill()
    {
        try { _process?.Kill(); } catch { }
    }

    private int GetExitCode()
    {
        try { return _process?.ExitCode ?? -1; }
        catch { return -1; }
    }

    public void Dispose() => Kill();
}
