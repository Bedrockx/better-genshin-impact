using BetterGenshinImpact.GameTask.AutoFriendship.Model;

namespace BetterGenshinImpact.GameTask.AutoFriendship;

/// <summary>
/// AutoFriendshipTask 的纯决策函数。
/// 抽出便于 property-based test，不持有 logger / executor / 任何外部状态。
/// </summary>
public static class AutoFriendshipTaskDecisions
{
    /// <summary>
    /// 判定盗宝团清场分支是否应当跳过 ExecuteClearBattleAsync。
    /// 跳过条件：进入了盗宝团清场分支（HilichurlBrigade + 超时 > 0），但 prepare 寻路未实际执行。
    /// </summary>
    /// <param name="enemyType">当前敌人类型</param>
    /// <param name="qiuQiuRenTimeoutSeconds">配置的清原住民超时秒数</param>
    /// <param name="prepareNavigated">AutoPathAsync("盗宝团-准备") 的返回值（是否实际驱动了 PathExecutor.Pathing）</param>
    public static bool ShouldSkipClearBattle(EnemyType enemyType, int qiuQiuRenTimeoutSeconds, bool prepareNavigated)
    {
        if (enemyType != EnemyType.HilichurlBrigade) return false;
        if (qiuQiuRenTimeoutSeconds <= 0) return false;
        return !prepareNavigated;
    }
}
