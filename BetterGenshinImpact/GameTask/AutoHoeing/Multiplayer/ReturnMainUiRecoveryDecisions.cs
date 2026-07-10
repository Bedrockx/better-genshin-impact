namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 「被踢出」判定路径的回主页自愈决策纯函数（无外部依赖，PBT 友好）。
/// 与 RoundSwitchTimeoutDecisions / KazuhaCollectSyncDecisions 同模式：
/// 只依据 ConnectedNotInGame 计数值计算「本轮是否应尝试回主页」与「本轮是否应确认退出」，
/// 不持有 _client / _logger / 任何可变状态。详见 design.md §Correctness Properties。
/// </summary>
public static class ReturnMainUiRecoveryDecisions
{
    /// <summary>被踢出判定最终阈值（与 WorldStateMonitor.ConnectedNotInGameThreshold 对齐 = 7）。</summary>
    public const int KickoutThreshold = 7;

    /// <summary>
    /// 本轮（_connectedButNotInGame 自增后）是否应尝试回到主界面。
    /// 触发节点精确匹配 {3, 5, 7}（OQ-1 确认）：3/5 给自愈机会，7 为退出前最后一次自愈。
    /// 其余取值（含 0/负数/&gt;7）一律 false。
    /// </summary>
    public static bool ShouldAttemptReturnMainUi(int connectedNotInGameCount)
    {
        return connectedNotInGameCount == 3
            || connectedNotInGameCount == 5
            || connectedNotInGameCount == 7;
    }

    /// <summary>
    /// 本轮（_connectedButNotInGame 自增后）是否应确认退出。
    /// 采用 &gt;= KickoutThreshold，与未改动前 WorldStateMonitor 信号融合分支
    /// `_connectedButNotInGame >= ConnectedNotInGameThreshold` 的判定语义逐字节对齐：
    /// 保持原退出触发边界不变，避免回归（见 §Preservation）。
    /// 正常路径下计数自增不跳变，命中点即为 7；&gt;7 仍判退出是对异常多增的安全兜底。
    /// </summary>
    public static bool ShouldConfirmExit(int connectedNotInGameCount)
    {
        return connectedNotInGameCount >= KickoutThreshold;
    }
}
