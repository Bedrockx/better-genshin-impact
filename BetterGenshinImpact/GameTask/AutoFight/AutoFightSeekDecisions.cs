namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// AutoFightSeek.SeekAndFightAsync 内部决策的纯函数集合，用于 PBT 守住决策语义。
/// 详见 .kiro/specs/multiplayer-kazuha-pre-cast-positioning/design.md §6 PBT-1。
/// </summary>
public static class AutoFightSeekDecisions
{
    /// <summary>
    /// 判定联机万叶玩家"持续回点"是否应触发。
    /// 仅在距离实时超阈值 ∧ 距上次回点完成已超过最小间隔时返回 true。
    /// </summary>
    public static bool ShouldTriggerContinuousReturn(
        double realtimeDistance,
        double returnDistanceThreshold,
        double elapsedMsSinceLastReturn,
        double returnIntervalMs)
    {
        return realtimeDistance > returnDistanceThreshold
               && elapsedMsSinceLastReturn >= returnIntervalMs;
    }
}
