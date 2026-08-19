using System.Runtime.InteropServices;
using Avalonia.Threading;
using Launcher.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

/// <summary>
/// 闲置内存优化（8-18 批次 80 引入；8-19 第二批重写为「低写盘」设计）。
/// 触发式 + 冷却，不做 PCL 那种定时反复修剪：
/// - 轻度（默认，零盘写）：闲置后后台 GC 压缩——托管对象移动全在 RAM，配合 GCHeapHardLimit 压堆
/// - 工作集修剪（默认关）：GC 后可选 SetProcessWorkingSetSize，把脏页写入页面文件换回物理内存
///   （UI 文案明示会增加硬盘读写——用户要求对硬盘损伤小，故默认关）
/// - 修剪前检查：工作集超阈值才修剪；下载/开服进行中跳过；相邻修剪冷却 10 分钟
/// </summary>
public sealed class IdleMemoryTuner : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private long _lastActivityTick = Environment.TickCount64;
    private bool _trimmed;

    /// <summary>8-19 启动后立即修剪（版本扫描峰值/清单解析中间对象一次性释放）；
    /// 与闲置/失焦逻辑独立（不置 _trimmed），10s 防抖防重复调用</summary>
    public static void TrimStartup()
    {
        var now = Environment.TickCount64;
        if (now - _lastStartupTrimTick < 10_000) return;
        _lastStartupTrimTick = now;
        _ = Task.Run(TrimAsync);
    }
    private static long _lastStartupTrimTick;

    /// <summary>相邻修剪冷却（毫秒）：杜绝频繁摘-换页面导致的缺页抖动与页面文件读写放大</summary>
    private const long TrimCooldownMs = 10 * 60 * 1000;
    private static long _lastTrimTick;

    /// <summary>工作集低于此值不修剪（本来就不大，修剪纯属制造缺页）</summary>
    private const long MinTrimWorkingSetBytes = 200L * 1024 * 1024;

    public IdleMemoryTuner()
    {
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>用户活动通知（MainWindow 全局输入事件调用——只写时间戳，零开销）。
    /// 8-19 第二批修复：活动即复位修剪标记并重启心跳——此前只更新时间戳、修剪后永不再修剪</summary>
    public void OnUserActivity()
    {
        Interlocked.Exchange(ref _lastActivityTick, Environment.TickCount64);
        if (_trimmed)
        {
            _trimmed = false;
            if (!_timer.IsEnabled) _timer.Start();
        }
    }

    /// <summary>立即修剪（窗口失焦/最小化——用户离开马上让资源，不等闲置间隔）</summary>
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
        if (elapsed < (long)IdleMinutes.TotalMilliseconds) return;
        _trimmed = true;
        _timer.Stop();
        _ = Task.Run(TrimAsync);
    }

    /// <summary>闲置多久才修剪（设置可配，默认 5 分钟）</summary>
    private static TimeSpan IdleMinutes
        => TimeSpan.FromMinutes(Math.Clamp(LauncherSettings.Current.MemoryIdleMinutes, 1, 60));

    /// <summary>跳过条件：下载中 / 开服中——不拖累正在进行的任务</summary>
    private static bool ShouldSkip()
    {
        if (Launcher.Core.Download.DownloadManager.Instance.ActiveCount > 0) return true;
        if (Launcher.App.ViewModels.MainViewModel.Current?.IsServerRunning == true) return true;
        return false;
    }

    private static void TrimAsync()
    {
        try
        {
            // 总开关（设置页「内存优化」）
            if (!LauncherSettings.Current.MemoryOptimizeEnabled) return;
            if (ShouldSkip()) return; // 下载/开服中让路
            var now = Environment.TickCount64;
            if (now - _lastTrimTick < TrimCooldownMs) return; // 冷却中（频繁失焦场景不反复修剪）
            _lastTrimTick = now;

            AppLog.Instance?.LogInformation("[memory] idle trim (gc + trim={Trim})",
                LauncherSettings.Current.MemoryTrimEnabled);
            // 轻度：压缩 GC——可移动对象压实，空闲页真正可归还（全程 RAM 内，零盘写）
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
            // 可选：工作集修剪（PCL 式，写页面文件）——默认关，且工作集过小不修剪
            if (LauncherSettings.Current.MemoryTrimEnabled
                && System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 > MinTrimWorkingSetBytes)
            {
                SetProcessWorkingSetSize(Environment.ProcessId != 0
                    ? System.Diagnostics.Process.GetCurrentProcess().Handle
                    : IntPtr.Zero, -1, -1);
                AppLog.Instance?.LogInformation("[memory] working set trimmed");
            }
        }
        catch (Exception ex)
        {
            AppLog.Instance?.LogWarning("[memory] trim failed: {Error}", ex.Message);
        }
    }

    /// <summary>8-19 手动释放（点击式，PCL 百宝箱同款效果）：点击立即执行——
    /// GC 压缩 + 终结器回收 + **工作集修剪**（把驻留物理页还给系统，占用数字立降）。
    /// 用户主动点击 = 明确授权，忽略 MemoryTrimEnabled 开关与冷却；返回释放的物理内存字节数。
    /// 代价：被摘页下次访问需重新驻留（缺页换回），但点击式低频执行可接受</summary>
    public static async Task<long> ManualTrimAsync()
    {
        var before = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        await Task.Run(() =>
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
            GC.WaitForPendingFinalizers(); // 终结器跑完再量，释放量更真实
            // PCL 式工作集修剪：进程工作集压到最小（脏页进 standby，数字立即大幅下降）
            SetProcessWorkingSetSize(Environment.ProcessId != 0
                ? System.Diagnostics.Process.GetCurrentProcess().Handle
                : IntPtr.Zero, -1, -1);
        });
        return Math.Max(0, before - System.Diagnostics.Process.GetCurrentProcess().WorkingSet64);
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int min, int max);

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
