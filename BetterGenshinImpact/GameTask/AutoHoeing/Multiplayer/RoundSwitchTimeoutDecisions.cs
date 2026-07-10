using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 轮次切换墙钟兜底超时解析的纯函数（无外部依赖，PBT 友好）。
/// 取代原 WorldStateMonitor 写死的 const RoundSwitchTimeoutSeconds = 120。
/// 让墙钟兜底永远晚于集合点等待超时（PartyTimeoutSeconds + 余量），同时保留安全下限。
/// 详见 design.md §Correctness Properties Property 1/3/4。
/// </summary>
public static class RoundSwitchTimeoutDecisions
{
    /// <summary>安全下限（秒）：等于原 const 行为，结果绝不短于此值。</summary>
    public const int SafeFloorSeconds = 120;

    /// <summary>默认余量（秒）：墙钟兜底相对集合点超时额外保留的收尾时间。</summary>
    public const int DefaultMarginSeconds = 60;

    /// <summary>
    /// 解析最终生效的轮次切换墙钟兜底超时秒数。
    /// 规则：max(SafeFloorSeconds, partyTimeoutSeconds + margin)；
    /// 对负值/溢出钳到 SafeFloorSeconds，绝不返回比原 120s 更短的值。
    /// </summary>
    /// <param name="partyTimeoutSeconds">集合点等待超时（= config.PartyTimeoutSeconds）。</param>
    /// <param name="marginSeconds">额外余量秒数（默认 60）。</param>
    public static int Resolve(int partyTimeoutSeconds, int marginSeconds = DefaultMarginSeconds)
    {
        // 非法输入（负值）钳到安全下限
        if (partyTimeoutSeconds < 0 || marginSeconds < 0)
            return SafeFloorSeconds;

        // 防溢出：long 求和后再钳
        long sum = (long)partyTimeoutSeconds + marginSeconds;
        if (sum > int.MaxValue) return int.MaxValue;

        var combined = (int)sum;
        return Math.Max(SafeFloorSeconds, combined);
    }
}
