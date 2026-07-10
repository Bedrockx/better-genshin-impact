#nullable enable

using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 联机复苏信号位的纯逻辑助手（PBT 友好）。
/// 把"set + CAS 消费 + reset"语义抽离为可测试的 static 方法，
/// PathExecutor 内的字段调用这些方法即可。
///
/// 不包含线程同步以外的逻辑（不调 TpStatueOfTheSeven、不抛 RetryException）——
/// 那些副作用由 PathExecutor 在调用方处理。
///
/// 设计参考 design.md "Property-Based Testing Plan / 抽取 helper" 节。
/// 对应 bugfix.md BC1 / BC2 / BC3 修复中"CAS 消费唯一性"性质（Fix 性质 2）。
/// </summary>
public static class MultiplayerRevivalGate
{
    /// <summary>
    /// 原子置位：写 1。多次调用 idempotent（slot 已为 1 时再次写 1 等效）。
    /// 由 AnomalyDetector 通过 PathExecutor.SignalMultiplayerRevival() 间接调用。
    /// </summary>
    public static void Signal(ref int slot)
    {
        Interlocked.Exchange(ref slot, 1);
    }

    /// <summary>
    /// 原子消费：仅当原值 == 1 且 isMultiplayer == true 时返回 true 并将值置 0。
    /// 否则返回 false，slot 不变。
    ///
    /// 调用方典型用法：
    ///     if (MultiplayerRevivalGate.TryConsume(ref _slot, MultiplayerCoordinator != null))
    ///     {
    ///         await TpStatueOfTheSeven();
    ///         throw new RetryException("...");
    ///     }
    /// </summary>
    public static bool TryConsume(ref int slot, bool isMultiplayer)
    {
        if (!isMultiplayer) return false;
        return Interlocked.CompareExchange(ref slot, 0, 1) == 1;
    }

    /// <summary>
    /// 原子重置：写 0。段循环入口防御性使用，避免跨段残留。
    /// </summary>
    public static void Reset(ref int slot)
    {
        Interlocked.Exchange(ref slot, 0);
    }
}
