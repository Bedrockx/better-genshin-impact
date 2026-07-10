#nullable enable
using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// F2 诊断日志的纯节流决策（策略 D：变化立即输出 + 无变化每 N 秒兜底心跳）。
/// 抽成 static class 是为了 PBT 友好：无 logger/client 依赖，时间由参数注入。
/// 由 AutoPartyTask.WaitForMembersAsync 的非主界面分支调用。
/// </summary>
public static class F2DiagnosticLogThrottle
{
    /// <summary>无变化时兜底心跳间隔，固定 5 秒。</summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>本轮 F2 检测的人数对比四元组，必须与诊断日志参数逐字一致。</summary>
    public readonly record struct F2Metrics(int KickCount, int F2Count, int ExpectedCount, int SignalRCount);

    /// <summary>
    /// 判定本轮是否应输出 F2 诊断日志（策略 D）：
    ///   1. 无上次记录（首轮）→ true（建立基线）
    ///   2. metrics 任一字段不同于上次输出 → true（变化立即输出，与时间无关）
    ///   3. metrics 完全相同且 (now - lastEmitTime) >= heartbeatInterval → true（兜底心跳）
    ///   4. 其余（相同且未到心跳间隔）→ false
    /// </summary>
    /// <param name="current">本轮 metrics</param>
    /// <param name="lastEmitted">上次已输出的 metrics；null 表示尚无记录（首轮）</param>
    /// <param name="lastEmitTime">上次输出时间（仅 lastEmitted 非 null 时有意义）</param>
    /// <param name="now">当前时间（由调用方传入，PBT 可注入）</param>
    /// <param name="heartbeatInterval">心跳间隔，生产固定 DefaultHeartbeatInterval(5s)</param>
    public static bool ShouldEmit(
        F2Metrics current,
        F2Metrics? lastEmitted,
        DateTime lastEmitTime,
        DateTime now,
        TimeSpan heartbeatInterval)
    {
        if (lastEmitted is null) return true;                // 规则 1：首轮
        if (!current.Equals(lastEmitted.Value)) return true; // 规则 2：变化立即输出
        return (now - lastEmitTime) >= heartbeatInterval;    // 规则 3/4：心跳兜底
    }
}
