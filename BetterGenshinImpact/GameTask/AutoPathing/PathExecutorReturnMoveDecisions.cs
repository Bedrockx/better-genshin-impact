namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 回点 MoveTo 粗接近段的提前结束决策(纯函数,无外部依赖,便于 PBT)。
/// hoeing-return-fightpoint-moveto-stuck-sync-timeout-fix
/// </summary>
public static class PathExecutorReturnMoveDecisions
{
    /// <summary>
    /// 判定回点 MoveTo 粗接近段是否应因"聚物同步预算耗尽"而提前结束(KeyUp + return)。
    /// </summary>
    /// <param name="budgetSeconds">
    /// 回点移动段预算秒数 = KazuhaSyncTimeoutSeconds。null 表示非回点调用(不启用预算),恒返回 false。
    /// </param>
    /// <param name="elapsedSeconds">该 MoveTo 段已耗时(秒)。</param>
    /// <returns>true = 预算已耗尽,应提前结束移动段;false = 继续(含 null / 非正预算 / 未到预算)。</returns>
    public static bool ShouldEndReturnMove(double? budgetSeconds, double elapsedSeconds)
    {
        if (!budgetSeconds.HasValue) return false;   // 非回点调用,旧行为
        if (budgetSeconds.Value <= 0) return false;  // 防御:无效/零预算不提前结束,退化到 240s 兜底
        return elapsedSeconds > budgetSeconds.Value;
    }
}
