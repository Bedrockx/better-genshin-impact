namespace BgiCoordinatorServer.Services;

/// <summary>
/// 同步点补发（replay AllArrived）决策的纯函数集合
/// （fastsync-claim-short-circuit-premature-release-fix / OQ-5=a）。
///
/// 与 bgi-implementation-patterns.md §1「决策函数纯化」模式对齐：无外部依赖、
/// 不持有 room / logger / SignalR 状态，便于 FsCheck PBT 直接撒输入跑性质。
/// </summary>
public static class SyncReplayDecisions
{
    /// <summary>
    /// 判定是否应对当前 WaitForAllPlayers 调用方单独补发 AllArrived。
    ///
    /// 规则：当且仅当该 syncId 本轮已广播过（syncIdAlreadyBroadcastedThisRound=true）时补发。
    /// 「已广播过」⇔「该 syncId 本轮确已全员放行过」（广播与 ClearArrivalSet 1:1 配对），
    /// 因此补发不会误放本不该放行的调用方（Property 3）。
    /// 未广播过则返回 false，调用方走正常的 RecordArrival + 全量重评估路径。
    /// </summary>
    public static bool ShouldReplayAllArrived(bool syncIdAlreadyBroadcastedThisRound)
    {
        return syncIdAlreadyBroadcastedThisRound;
    }
}
