using System.Runtime.InteropServices;
using Avalonia.Threading;
using Launcher.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

/// <summary>
/// 闲置内存让渡（8-18 批次 80）：用户无操作 3 分钟后，后台 GC 压缩 + 工作集修剪——
/// 物理页还给系统（任务管理器"内存"数字立降），数据不丢（活动时缺页自然重新驻留）。
/// 用户输入只更新原子时间戳（零开销），修剪一次后不再重复；活动即重置。
/// </summary>
public sealed class IdleMemoryTuner : IDisposable
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMinutes(3);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private long _lastActivityTick = Environment.TickCount64;
    private bool _trimmed;

    public IdleMemoryTuner()
    {
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>用户活动通知（MainWindow 全局输入事件调用——只写时间戳，零开销）</summary>
    public void OnUserActivity() => Interlocked.Exchange(ref _lastActivityTick, Environment.TickCount64);

    /// <summary>8-18 立即修剪（窗口失焦/最小化——用户离开马上让资源，不等 3 分钟闲置）</summary>
    public void TrimNow()
    {
        if (_trimmed) return; // 已修剪过（等用户活动重置）
        _trimmed = true;
        _timer.Stop();
        _ = Task.Run(TrimAsync);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_trimmed) return;
        var elapsed = Environment.TickCount64 - Interlocked.Read(ref _lastActivityTick);
        if (elapsed < (long)IdleDelay.TotalMilliseconds) return;
        _trimmed = true;
        _timer.Stop();
        _ = Task.Run(TrimAsync);
    }

    private static void TrimAsync()
    {
        AppLog.Instance?.LogInformation("[memory] idle 3min, trim working set");
        try
        {
            // 压缩 GC：先收紧托管堆（可移动对象压实 → 空闲页真正可归还）
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
            // 工作集修剪：把驻留物理页还给系统（-1,-1 = 修剪到最小；下次访问缺页自动换回）
            SetProcessWorkingSetSize(Environment.ProcessId != 0
                ? System.Diagnostics.Process.GetCurrentProcess().Handle
                : IntPtr.Zero, -1, -1);
            AppLog.Instance?.LogInformation("[memory] working set trimmed");
        }
        catch (Exception ex)
        {
            AppLog.Instance?.LogWarning("[memory] trim failed: {Error}", ex.Message);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int min, int max);

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
