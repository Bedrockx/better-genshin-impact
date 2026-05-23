namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// PathExecutor 内部决策的纯函数集合，用于 PBT 守住决策语义。
/// 详见 .kiro/specs/multiplayer-kazuha-pre-cast-positioning/design.md §6 PBT-2。
/// 与同 namespace 下 PathExecutor.cs 文件底部的 internal MoveCloseToDecisions 不同——
/// 后者负责 MoveCloseTo 内部停步 / 尾部 Delay 决策；本类负责"是否启动二段式 MoveTo"决策。
/// </summary>
public static class PathExecutorDecisions
{
    /// <summary>
    /// 联机战后聚物分支二段式接近：判定是否应在 MoveCloseTo 之前先调 MoveTo 粗接近。
    /// 仅当当前距离 > threshold（默认 4.0，由 Resolved Decisions Q1 决议）时返回 true。
    /// </summary>
    public static bool ShouldPreMoveTo(double distance, double threshold)
    {
        return distance > threshold;
    }

    /// <summary>
    /// 联机战后聚物分支跳过判定：当前 waypoint 是 Fight 且**下一个** waypoint 也是 Fight
    /// 且**两个 waypoint 距离 &lt; nearbyDistanceThreshold**（默认 10.0）时返回 true，
    /// 调用方应跳过整个万叶聚物流程（回点 / BeginPreparationAsync / WaitAtFightPointAsync），
    /// 等下一段战斗结束再聚物——同一战斗点连续两次战斗时只在第二次后聚物，避免重复触发。
    /// 距离 ≥ 阈值时认为是两个独立战斗点，仍正常聚物。
    /// 全员一致：路线由房主同步给所有客户端，所有玩家在同一战斗点看到相同的 nextAction / 距离，决策必然一致。
    /// </summary>
    /// <param name="currentAction">当前 waypoint.Action（"fight" 即战斗节点）</param>
    /// <param name="nextAction">下一个 waypoint.Action（null 表示最后一个节点）</param>
    /// <param name="distanceToNext">当前 waypoint 到下一个 waypoint 的距离（基于 X/Y 坐标）；
    /// nextAction == null 时调用方应传 double.PositiveInfinity 或一个大于阈值的值</param>
    /// <param name="nearbyDistanceThreshold">两战斗点视为"同一战斗点"的距离阈值（默认 10.0）</param>
    /// <param name="fightActionCode">ActionEnum.Fight.Code 取值（避免决策函数依赖具体常量）</param>
    public static bool ShouldSkipKazuhaCollectWhenNextIsFight(
        string? currentAction,
        string? nextAction,
        double distanceToNext,
        double nearbyDistanceThreshold,
        string fightActionCode)
    {
        return currentAction == fightActionCode
               && nextAction == fightActionCode
               && distanceToNext < nearbyDistanceThreshold;
    }
}
