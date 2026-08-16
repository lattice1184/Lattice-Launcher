using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PCL.Core.App.Configuration.Storage;

public class DynamicCacheConfigStorage : ConfigStorage
{
    private readonly Dictionary<object, ConfigStorage> _cache = [];
    private ConfigStorage? _nullContextCache;
    // 8-22 全栈排查严重项（同族）：普通 Dictionary 无锁并发（首次访问写缓存 vs 其他线程读/Invalidate）
    // → 字典损坏/异常 → ForceShutdown(-2)。锁只护缓存字典本身；storage.Access（长操作）在锁外
    private readonly object _cacheLock = new();

    /// <summary>
    /// 存取仓库工厂。在没有匹配的上下文实例时将被调用，以创建新的上下文实例。
    /// </summary>
    public required Func<object?, ConfigStorage> StorageFactory { get; init; }

    protected override bool OnAccess<TKey, TValue>(StorageAction action, ref TKey key, [NotNullWhen(true)] ref TValue value, object? context)
    {
        ConfigStorage storage;
        lock (_cacheLock)
        {
            if (context is null) storage = _nullContextCache!;
            else _cache.TryGetValue(context, out storage!);
            if (storage is null)
            {
                try
                {
                    storage = StorageFactory(context);
                    if (context is null) _nullContextCache = storage;
                    else _cache[context] = storage;
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to invoke storage factory", ex);
                }
            }
        }
        return storage.Access(action, ref key, ref value, context); // 锁外执行（可能 IO/网络）
    }

    protected override void OnStop()
    {
        ConfigStorage[] all;
        lock (_cacheLock)
        {
            all = [.. _cache.Values];
            _cache.Clear();
            _nullContextCache = null;
        }
        foreach (var item in all) item.Stop(); // 锁外 Stop（可能 IO）
    }

    public bool InvalidateCache(object context)
    {
        ConfigStorage? center;
        bool result;
        lock (_cacheLock)
        {
            result = _cache.TryGetValue(context, out center);
            if (result) _cache.Remove(context);
        }
        center?.Stop(); // 锁外 Stop（可能 IO——持锁 Stop 会让并发 Access 卡死）
        return result;
    }
}
