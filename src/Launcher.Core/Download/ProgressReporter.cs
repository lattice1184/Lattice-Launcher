using System.Diagnostics;

namespace Launcher.Core.Download;

/// <summary>
/// 进度上报统一抽象（REVIEW 治本）：所有阶段（下载/质检/meta/加载器配置/Fabric API/index/assets）
/// 强制经过它——每个阶段必须携带「阶段文字 + 字节进度 + 节流」三件套，从根上消灭「静默段」。
///
/// 为什么需要：历史每轮真机暴露一个「进度无表达窗口」（fabric-api 组路径 progress 参数失效、
/// 质检 10-20s 无 Stage、meta 拉取 2-26s 无进度）——根因是各阶段各自为政，绕过/漏接进度。
/// 本类统一：节流 250ms + 收尾强制补报 + 阶段文字随时可改——接入即获得全部保证。
///
/// 用法：阶段开始时 new ProgressReporter("正在质检…", sink)；进行中 Report(bytes, total)
/// 或 ReportStage("第 3/10 个…")；结束时 Complete()（补报窗口内最后状态）。
/// sink 为 null（无消费端）时全部方法为空操作——调用方无需判空分支。
/// </summary>
public sealed class ProgressReporter
{
    private const long WindowMs = 250;

    private readonly DownloadProgressHandler? _sink;
    private string _stage;
    private readonly Stopwatch _sw = new();
    private long _lastReportMs = long.MinValue;
    private long _bytes;
    private long _total;
    private string? _fileName;

    /// <summary>创建上报器。stage 为阶段文字（必填——「静默段」的根除点）；sink 可空（无消费端时全空操作）。</summary>
    public ProgressReporter(string stage, DownloadProgressHandler? sink, string? fileName = null)
    {
        _stage = stage;
        _sink = sink;
        _fileName = fileName;
        _sw.Start();
        // 阶段开始的首次上报（无进度数字也要让 UI 立即看到阶段文字——不再有「无表达窗口」）
        Emit();
    }

    /// <summary>更新字节进度（自动 250ms 节流——高速下载不刷爆 UI Post 队列）</summary>
    public void Report(long bytes, long total)
    {
        _bytes = bytes;
        _total = total;
        var now = _sw.ElapsedMilliseconds;
        if (now - _lastReportMs >= WindowMs) Emit();
    }

    /// <summary>更新阶段文字（如「下载资源 3/100」「第 5/10 个文件」——节流窗口内立即生效）</summary>
    public void ReportStage(string stage) => ReportStage(stage, _bytes, _total);

    public void ReportStage(string stage, long bytes, long total)
    {
        _stage = stage;
        _bytes = bytes;
        _total = total;
        Emit(); // 阶段文字变化必须立即可见（即使在上次节流窗口内——文字优先于节流）
    }

    /// <summary>收尾：补报窗口内最后一次状态（节流吞掉的最后字节不丢）</summary>
    public void Complete() => Emit();

    private void Emit()
    {
        _lastReportMs = _sw.ElapsedMilliseconds;
        _sink?.Invoke(new DownloadProgress(_stage, _fileName, _bytes, _total,
            _total > 0 ? Math.Min(_bytes * 100.0 / _total, 99) : 0));
    }
}
