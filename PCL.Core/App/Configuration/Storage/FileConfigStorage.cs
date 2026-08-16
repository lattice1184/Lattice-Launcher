using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.UI;

namespace PCL.Core.App.Configuration.Storage;

/// <summary>
/// 文件存取仓库。
/// </summary>
public class FileConfigStorage : ConfigStorage
{
    /// <summary>
    /// 键值文件实例。
    /// </summary>
    public IKeyValueFileProvider File { get; }

    private readonly Channel<(string, Action)> _writeActionChannel;
    private readonly CancellationTokenSource _writeActionCts;
    private readonly ManualResetEventSlim _writeStopEvent = new(true);
    // 8-22 全栈排查严重项：内存树（JsonObject/YamlMappingNode）无锁并发——后台写线程 Sync
    // 序列化树的同时任意线程 Get 读树 → 并发枚举+修改抛 InvalidOperationException 逃出 catch
    // 白名单 → ConfigStorage.Access → ForceShutdown(-2) 整个进程退出。读写锁保护全部 File 访问：
    // Get/Exists 读锁，后台写 action + File.Sync 写锁（读锁内严禁写操作——互等死锁）
    private readonly ReaderWriterLockSlim _fileLock = new();

    public FileConfigStorage(IKeyValueFileProvider file)
    {
        File = file;
        _writeActionChannel = Channel.CreateUnbounded<(string, Action)>();
        _writeActionCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            _writeStopEvent.Reset();
            const long syncInterval = 10000; // ms
            var lastSyncTick = 0L;
            var cancelToken = _writeActionCts.Token;
            var writeActionMap = new Dictionary<string, Action>();
            var reader = _writeActionChannel.Reader;
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    // 读入并合并暂存操作
                    var (key, action) = await reader.ReadAsync(cancelToken);
                    writeActionMap[key] = action;
                    if (Environment.TickCount64 - lastSyncTick < syncInterval || cancelToken.IsCancellationRequested) continue;
                    // 同步文件
                    Sync();
                    lastSyncTick = Environment.TickCount64;
                    writeActionMap.Clear();
                }
            }
            catch (OperationCanceledException) { /* ignoring*/ }
            finally
            {
                // 结束时执行一次同步
                Sync();
            }
            _writeStopEvent.Set();
            return;
            void Sync()
            {
                try
                {
                    // 写锁：写树 + 落盘必须与任何读互斥（读锁内不写树——见 Get 分支注释）
                    _fileLock.EnterWriteLock();
                    try
                    {
                        LogWrapper.Trace("Config", $"正在保存 {File.FilePath}");
                        foreach (var action in writeActionMap.Values) action();
                        File.Sync();
                    }
                    finally { _fileLock.ExitWriteLock(); }
                }
                catch (Exception ex)
                {
                    LogWrapper.Error(ex, "Config", "配置文件保存失败");
                    var summary = Lang.Text("Config.Error.SaveFailed.Message", File.FilePath);
                    var message = ExceptionDetails.Compose(summary, ex);
                    MsgBoxWrapper.Show(
                        message,
                        Lang.Text("Config.Error.SaveFailed.Title"),
                        MsgBoxTheme.Error);
                }
            }
        });
    }

    protected override void OnStop()
    {
        _writeActionCts.Cancel();
        _writeStopEvent.Wait();
        _writeStopEvent.Dispose();
    }

    protected override bool OnAccess<TKey, TValue>(
        StorageAction action,
        ref TKey key,
        [NotNullWhen(true)] ref TValue value,
        object? argument)
    {
        if (key is not string strKey) throw new NotSupportedException($"Key '{key}' is not supported");
#pragma warning disable CS8762 // Parameter must have a non-null value when exiting in some condition.
        switch (action)
        {
            case StorageAction.Get:
            {
                // 读锁保护：与后台写线程（Set/Delete action + Sync 序列化）互斥——
                // 并发枚举+修改会抛 InvalidOperationException 逃出 catch 白名单 → 进程退出
                var cleanup = false;
                var found = false;
                TValue? readResult = default;
                _fileLock.EnterReadLock();
                try
                {
                    if (File.Exists(strKey))
                    {
                        try
                        {
                            readResult = File.Get<TValue>(strKey);
                            found = true;
                        }
                        catch (Exception ex) when (ex is JsonException
                                                       or InvalidCastException
                                                       or FormatException
                                                       or OverflowException
                                                       or ArgumentException
                                                       or KeyNotFoundException
                                                       or InvalidDataException)
                        {
                            LogWrapper.Warn(ex, "Config", $"配置项 {strKey} 读取失败（可能已损坏），重置为默认值");
                            cleanup = true;
                        }
                    }
                }
                finally { _fileLock.ExitReadLock(); }
                if (cleanup)
                {
                    // 清理必须在锁外：读锁内写树 + Sync 落盘会与写锁互等 → 死锁
                    if (!_writeActionChannel.Writer.TryWrite((strKey, () => File.Remove(strKey))))
                    {
                        LogWrapper.Warn("Config", $"配置项 {strKey} 清理任务入队失败，改为同步删除");
                        try
                        {
                            _fileLock.EnterWriteLock();
                            try { File.Remove(strKey); File.Sync(); }
                            finally { _fileLock.ExitWriteLock(); }
                        }
                        catch (Exception cleanupEx)
                        {
                            LogWrapper.Error(cleanupEx, "Config", $"配置项 {strKey} 同步删除失败，可能需人工处理");
                        }
                    }
                }
                if (!found) return false;
                value = readResult!;
                return true;
            }
            case StorageAction.Exists:
            {
                // 由于 Exists 的 value 类型一定是 bool，此处可 unsafe 直接赋值（读锁保护同 Get）
                _fileLock.EnterReadLock();
                try
                {
                    if (typeof(TValue) == typeof(bool)) Unsafe.As<TValue, bool>(ref value) = File.Exists(strKey);
                    else throw new InvalidOperationException($"Storage action '{StorageAction.Exists}' must have a boolean value");
                }
                finally { _fileLock.ExitReadLock(); }
                return true;
            }
            case StorageAction.Set:
                var localValue = value;
                _writeActionChannel.Writer.TryWrite((strKey, () => File.Set(strKey, localValue)));
                return false;
            case StorageAction.Delete:
                _writeActionChannel.Writer.TryWrite((strKey, () => File.Remove(strKey)));
                return false;
            default: throw new InvalidOperationException($"Invalid storage action: {action}");
        }
#pragma warning restore CS8762 // Parameter must have a non-null value when exiting in some condition.
    }

    public override string ToString() => $"{base.ToString()} ({File.FilePath})";
}
