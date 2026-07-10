#nullable enable

using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 联机锄地共享战斗"配额(quorum)结束同步"决策纯函数集合（PBT 友好，无外部依赖）。
/// 由 spec multiplayer-shared-fight-end-quorum-sync 引入。
/// 详见 design.md §5。
/// </summary>
public static class SharedFightEndQuorumDecisions
{
    /// <summary>
    /// 配额是否达成：doneCount ≥ ⌈participantCount × ratio⌉。
    /// </summary>
    /// <param name="doneCount">已上报 done 的人数（≥0）</param>
    /// <param name="participantCount">战斗参与者人数（≥0）</param>
    /// <param name="ratio">配额比例，钳制到 [0.0, 1.0]，默认 0.5</param>
    public static bool IsQuorumReached(int doneCount, int participantCount, double ratio)
    {
        if (participantCount <= 0) return false;   // 无参与者 → 不广播（防 0/0）
        if (doneCount <= 0) return false;
        var r = ClampRatio(ratio);
        var threshold = (int)Math.Ceiling(participantCount * r);
        if (threshold < 1) threshold = 1;          // 至少 1 人
        return doneCount >= threshold;
    }

    /// <summary>比例钳制到 [0,1]；NaN → 0.5（STJ/Newtonsoft 浮点兜底）。</summary>
    public static double ClampRatio(double ratio)
        => double.IsNaN(ratio) ? 0.5 : Math.Clamp(ratio, 0.0, 1.0);

    /// <summary>客户端启用门控（单机/断连/开关关 → false）。</summary>
    public static bool IsEnabled(bool coordinatorPresent, bool isConnected, bool configEnabled)
        => coordinatorPresent && isConnected && configEnabled;
}
