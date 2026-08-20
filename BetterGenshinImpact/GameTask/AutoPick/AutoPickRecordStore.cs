using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 自动拾取记录器（仅莫版拾取路径记录，原版拾取不记录）。
/// 记录每次拾取交互的物品名称与时间戳，供 JS 侧通过 <c>dispatcher.getPickRecords()</c> 获取。
///
/// JS 侧用法（注意兼容旧版 C#，使用特性检测/可选链，旧版无此方法时返回空数组不报错）：
/// <code>
/// // 获取自上次调用以来的拾取记录（取走后自动清空）
/// const records = typeof dispatcher.getPickRecords === 'function' ? dispatcher.getPickRecords() : [];
/// // 或更简洁：const records = dispatcher.getPickRecords?.() ?? [];
/// for (const r of records) {
///     log.info(`拾取: ${r.Name} @ ${r.Time}`);
/// }
/// </code>
/// </summary>
public static class AutoPickRecordStore
{
    /// <summary>记录上限，超出后丢弃最旧记录，防止无限增长。</summary>
    private const int MaxRecords = 100;

    /// <summary>线程安全队列：拾取记录（时间戳 + 物品名），拾取循环线程写入、JS 请求线程读出。</summary>
    private static readonly ConcurrentQueue<PickRecord> Records = new();

    /// <summary>单条拾取记录。</summary>
    public sealed record PickRecord(DateTime Time, string Name);

    /// <summary>记录一次拾取交互（仅由莫版拾取路径调用，线程安全）。</summary>
    public static void Record(string name)
    {
        Records.Enqueue(new PickRecord(DateTime.Now, name));
        while (Records.Count > MaxRecords && Records.TryDequeue(out _))
        {
        }
    }

    /// <summary>取出全部记录并清空（线程安全），JS 侧通过 dispatcher.getPickRecords() 调用。</summary>
    public static PickRecord[] Drain()
    {
        var list = new List<PickRecord>(Records.Count);
        while (Records.TryDequeue(out var record))
        {
            list.Add(record);
        }

        return list.ToArray();
    }
}
