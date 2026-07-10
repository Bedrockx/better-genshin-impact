using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 反复复苏追踪器：持有路线生命周期内的复苏时间戳列表，
/// 委托纯函数 RevivalRecurrenceDecisions 做决策。
/// 生命周期 = 路线生命周期：路线开始 / 多世界轮换进入新世界时 Reset。
/// 线程安全：Track 由 AnomalyDetector 后台线程调用，加 lock 保护内部 List。
/// </summary>
public class RevivalRecurrenceTracker
{
    private readonly object _lock = new();
    private readonly List<DateTime> _timestamps = new();

    /// <summary>清空所有时间戳（路线开始 / 多世界轮换调用）。</summary>
    public void Reset()
    {
        lock (_lock) { _timestamps.Clear(); }
    }

    /// <summary>当前累计的复苏次数（仅供日志/诊断）。</summary>
    public int Count
    {
        get { lock (_lock) { return _timestamps.Count; } }
    }

    /// <summary>
    /// 记录一次复苏并返回决策动作。
    /// </summary>
    /// <param name="now">当前时刻，由调用方传入（便于测试时注入）。</param>
    public RevivalEscalationAction Track(DateTime now, int windowSeconds, int rapidThreshold, int routeCap)
    {
        lock (_lock)
        {
            _timestamps.Add(now);
            return RevivalRecurrenceDecisions.Decide(_timestamps, now, windowSeconds, rapidThreshold, routeCap);
        }
    }
}
