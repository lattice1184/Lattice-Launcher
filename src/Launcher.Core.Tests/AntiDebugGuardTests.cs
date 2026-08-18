using Launcher.Core.Utils;

namespace Launcher.Core.Tests;

/// <summary>入口反调试：委托注入模拟检测（不动真调试器/不调原生层）；xunit 禁并行故静态状态安全</summary>
public class AntiDebugGuardTests : IDisposable
{
    private readonly bool _origEnabled = AntiDebugGuard.Enabled;
    private readonly Func<bool>? _origManaged = AntiDebugGuard.ManagedDetector;
    private readonly Func<bool>? _origNative = AntiDebugGuard.NativeDetector;
    private readonly Action? _origExit = AntiDebugGuard.ExitAction;
    private readonly string? _origEnv = Environment.GetEnvironmentVariable("LATTICE_SKIP_ANTIDEBUG");

    public AntiDebugGuardTests()
    {
        Environment.SetEnvironmentVariable("LATTICE_SKIP_ANTIDEBUG", null);
    }

    public void Dispose()
    {
        AntiDebugGuard.Enabled = _origEnabled;
        AntiDebugGuard.ManagedDetector = _origManaged;
        AntiDebugGuard.NativeDetector = _origNative;
        AntiDebugGuard.ExitAction = _origExit;
        Environment.SetEnvironmentVariable("LATTICE_SKIP_ANTIDEBUG", _origEnv);
    }

    [Fact]
    public void Default_NoDetectorAttached_NotDetected()
    {
        // DEBUG 构建 Enabled=false 恒 false；Release 构建下无调试器 attach 也 false（两条路径都过）
        Assert.False(AntiDebugGuard.IsDebuggerDetected());
    }

    [Fact]
    public void Disabled_NotDetected_EvenIfDetectorTrue()
    {
        AntiDebugGuard.Enabled = false;
        AntiDebugGuard.ManagedDetector = () => true;
        Assert.False(AntiDebugGuard.IsDebuggerDetected());
    }

    [Fact]
    public void ManagedDetectorTrue_Detected()
    {
        AntiDebugGuard.Enabled = true;
        AntiDebugGuard.ManagedDetector = () => true;
        Assert.True(AntiDebugGuard.IsDebuggerDetected());
    }

    [Fact]
    public void AllDetectorsFalse_NotDetected()
    {
        AntiDebugGuard.Enabled = true;
        AntiDebugGuard.ManagedDetector = () => false;
        AntiDebugGuard.NativeDetector = () => false;
        Assert.False(AntiDebugGuard.IsDebuggerDetected());
    }

    [Fact]
    public void NativeDetectorTrue_Detected()
    {
        AntiDebugGuard.Enabled = true;
        AntiDebugGuard.ManagedDetector = () => false;
        AntiDebugGuard.NativeDetector = () => true;
        Assert.True(AntiDebugGuard.IsDebuggerDetected());
    }

    [Fact]
    public void ExemptEnv_OverridesDetection()
    {
        AntiDebugGuard.Enabled = true;
        AntiDebugGuard.ManagedDetector = () => true;
        Environment.SetEnvironmentVariable("LATTICE_SKIP_ANTIDEBUG", "1");
        Assert.False(AntiDebugGuard.IsDebuggerDetected());
    }

    [Fact]
    public void ShouldExit_False_WhenNotDetected()
    {
        AntiDebugGuard.Enabled = false;
        Assert.False(AntiDebugGuard.ShouldExit());
    }

    [Fact]
    public async Task LateCheck_Detected_TriggersExitAction()
    {
        AntiDebugGuard.Enabled = true;
        AntiDebugGuard.ManagedDetector = () => true;
        AntiDebugGuard.NativeDetector = () => false;
        var exited = false;
        AntiDebugGuard.ExitAction = () => exited = true;
        AntiDebugGuard.ScheduleLateCheck(TimeSpan.FromMilliseconds(50));
        for (var i = 0; i < 20 && !exited; i++) await Task.Delay(25);
        Assert.True(exited);
    }

    [Fact]
    public async Task LateCheck_NotDetected_NoExitAction()
    {
        AntiDebugGuard.Enabled = true;
        AntiDebugGuard.ManagedDetector = () => false;
        AntiDebugGuard.NativeDetector = () => false;
        var exited = false;
        AntiDebugGuard.ExitAction = () => exited = true;
        AntiDebugGuard.ScheduleLateCheck(TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);
        Assert.False(exited);
    }
}
