namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// PathExecutor 中 GetPositionAndTime 的 previousDetectedPoint 兜底决策纯函数集合。
///
/// 守住"prePosition 5 秒新鲜窗口 + mapKey 一致"两条件的等价语义，PBT 友好。
///
/// 详见 .kiro/specs/pathexecutor-teleport-fresh-position-fallback-fix/design.md §Correctness Properties。
/// </summary>
public static class PrePositionFallbackDecisions
{
    /// <summary>
    /// 5 秒兜底新鲜窗口（毫秒）。原 PathExecutor.GetPositionAndTime 内联判定 `(UtcNow - _prePositionUpdateTime).TotalSeconds <= 5` 等价。
    /// </summary>
    public const int FreshWindowMs = 5000;

    /// <summary>
    /// 决策"previousDetectedPoint 兜底是否可用"：
    /// - prePositionEmpty=false（即 prePosition 已被刷新过）
    /// - ageMs ≤ FreshWindowMs（5 秒新鲜）
    /// - sameMapKey=true（mapKey 一致，避免跨地图复用 stale 坐标）
    /// 三者全成立时返回 true，否则 false（调用方走 Navigation.Reset() + prePosition=default + mapKey=string.Empty）。
    /// </summary>
    /// <param name="prePositionEmpty">prePosition == default</param>
    /// <param name="ageMs">UtcNow - _prePositionUpdateTime, ms</param>
    /// <param name="sameMapKey">_prePositionMapKey == 当前 waypoint mapKey</param>
    public static bool IsFallbackUsable(bool prePositionEmpty, double ageMs, bool sameMapKey)
    {
        if (prePositionEmpty) return false;
        if (ageMs > FreshWindowMs) return false;
        if (!sameMapKey) return false;
        return true;
    }
}
