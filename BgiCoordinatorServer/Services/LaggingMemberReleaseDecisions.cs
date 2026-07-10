using System.Collections.Generic;
using System.Linq;

namespace BgiCoordinatorServer.Services;

/// <summary>
/// 落后成员放行（release lagging caller）决策的纯函数集合
/// （hoeing-multiplayer-member-syncpoint-timeout-falling-behind-fix / OQ-2 = A+B）。
///
/// 与 bgi-implementation-patterns.md §1「决策函数纯化」模式对齐：无外部依赖、
/// 不持有 room / logger / SignalR 状态，便于 FsCheck PBT 直接撒输入跑性质。
///
/// 语义：当 caller 的 syncProgress 已【严格小于】所有其他在线玩家的 CurrentProgress 时，
/// caller 是"孤立落后者"——其它人都已走过此进度点、不会再以此 syncId 到达，
/// 应立即放行 caller（方案 A 从 requiredPlayers 剔除已走过者；方案 B 直接补发 AllArrived）。
/// </summary>
public static class LaggingMemberReleaseDecisions
{
    /// <summary>
    /// 判定 caller 是否为"孤立落后者"，应被立即放行。
    ///
    /// 当且仅当以下全部成立时返回 true：
    ///   1. syncProgress >= 0          —— 全局同步点（route_sync_done 等，syncProgress<0）一律不介入，保持"等所有"
    ///   2. otherOnlinePlayersCurrentProgress 非空 —— 无人可比则不豁免
    ///   3. syncProgress < MIN(其它在线玩家 CurrentProgress) —— 【严格小于】防误放：
    ///      进度正好相等(==)的玩家是"马上要到这个点"的正常碰头玩家，必须继续等。
    /// </summary>
    /// <param name="syncProgress">caller 本次到达的同步点全局进度（编码 路线索引×1000000 + 路点偏移）。</param>
    /// <param name="otherOnlinePlayersCurrentProgress">除 caller 外、所有在线玩家（心跳&lt;2min）的 CurrentProgress。</param>
    public static bool ShouldReleaseLaggingCaller(
        long syncProgress,
        IReadOnlyCollection<long> otherOnlinePlayersCurrentProgress)
    {
        // 约束 1：全局同步点不介入（短路）
        if (syncProgress < 0) return false;

        // 约束 2：无其他在线玩家可比，不豁免
        if (otherOnlinePlayersCurrentProgress == null || otherOnlinePlayersCurrentProgress.Count == 0)
            return false;

        // 约束 3：严格小于所有其他在线玩家 —— caller 落后于所有人才放行
        // 等价于 syncProgress < MIN(others)。用 All(strictly greater) 表达严格小于，
        // 进度相等(==)的玩家不满足 ">"，使本函数返回 false（防误放正常碰头）。
        return otherOnlinePlayersCurrentProgress.All(cp => cp > syncProgress);
    }
}
