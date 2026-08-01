using Avalonia.Threading;
using PCL.Core.UI.Animation.UIAccessProvider;

namespace Launcher.Animation;

/// <summary>
/// Avalonia 适配的 UI 线程访问提供器，注入给 PCL.Core 动画引擎（AnimationService.UIAccessProviderFactory）。
/// </summary>
public sealed class AvaloniaUIAccessProvider : IUIAccessProvider
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Invoke(Action action) => Dispatcher.UIThread.Post(action);

    public Task InvokeAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        Dispatcher.UIThread.Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task InvokeAsync(Func<Task> func)
    {
        var tcs = new TaskCompletionSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await func(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> func)
    {
        var tcs = new TaskCompletionSource<T>();
        Dispatcher.UIThread.Post(async () =>
        {
            try { tcs.SetResult(await func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    // 帧时钟由 AnimationService 内部的 WinMMClock 驱动，FrameTick 适配留待 M4 动画接入
    public event EventHandler? FrameTick { add { } remove { } }
}
