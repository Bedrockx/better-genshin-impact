using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 房主退出当前多人世界等待阶段的决策纯函数（无外部依赖，PBT 友好）。
/// 替换原 LeaveCurrentWorldAsync 房主分支的固定 Task.Delay(waitMs)。
/// </summary>
public enum HostLeaveWaitDecision
{
    /// <summary>继续等待，下一轮重新检测踢出按钮。</summary>
    ContinueWaiting,

    /// <summary>连续 N 次检测到 0 踢出按钮，房主世界已无其他成员，提前退出等待。</summary>
    EarlyExit,

    /// <summary>已达兜底上限，强制停止等待（关房被动踢出残留成员）。</summary>
    FallbackForceExit,
}

public static class HostLeaveEarlyExitDecisions
{
    /// <summary>
    /// 推进"连续检测到 0 踢出按钮"的计数。
    /// kickCount == 0（无成员）→ prev + 1；
    /// kickCount &gt; 0（有残留）或 &lt; 0（不可观测/加载中）→ 清零。
    /// </summary>
    public static int NextConsecutiveZero(int prev, int kickCount)
    {
        if (prev < 0) prev = 0;
        return kickCount == 0 ? prev + 1 : 0;
    }

    /// <summary>
    /// 房主等待阶段决策。优先级：兜底超时 &gt; 连续归零提前退出 &gt; 继续等待。
    /// </summary>
    /// <param name="kickCount">本轮检测到的踢出按钮数，&lt;0 表示不可观测/加载中。</param>
    /// <param name="consecutiveZeroCount">含本轮的连续 0 计数（由调用方用 NextConsecutiveZero 维护）。</param>
    /// <param name="elapsedMs">已等待真实墙钟时长（ms）。</param>
    /// <param name="fallbackMs">兜底等待上限（ms）。</param>
    /// <param name="requiredConsecutiveZeros">确认提前退出所需连续归零次数（=5）。</param>
    public static HostLeaveWaitDecision Decide(
        int kickCount,
        int consecutiveZeroCount,
        long elapsedMs,
        long fallbackMs,
        int requiredConsecutiveZeros)
    {
        // 兜底优先级最高（Requirement 2.1 / 3.2 / 3.5）
        if (elapsedMs >= fallbackMs)
            return HostLeaveWaitDecision.FallbackForceExit;

        // 连续归零确认 → 提前退出（Requirement 1.2 / 3.3）
        if (consecutiveZeroCount >= requiredConsecutiveZeros)
            return HostLeaveWaitDecision.EarlyExit;

        // 否则继续等待（Requirement 1.3 / 3.4）
        return HostLeaveWaitDecision.ContinueWaiting;
    }
}
