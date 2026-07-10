using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 集合点抢报时机决策的纯函数集合（fastsync-preclaim-fires-after-rendezvous-fix spec / OQ-3=a）。
///
/// 与 bgi-implementation-patterns.md §1「决策函数纯化」+ 同目录 FastSyncDecisions 模式对齐：
/// - 仅依赖输入参数，无 logger / SignalR / TaskContext 依赖
/// - 把 PathExecutor 迭代体内「段内下一个未抢报同步点」反查（PathExecutor.cs line ~504-522）与
///   「形式等待相对移动的顺序不变量」固化，供 PBT 撒输入验证
///
/// 不改 FastSyncDecisions.cs 现有签名（Preservation 3.10）；本类为独立新增。
///
/// internal static 可见性 — 复用 BetterGenshinImpact/AssemblyInfo.cs 中已配置的
/// [InternalsVisibleTo("BetterGenshinImpact.UnitTest")]，单元测试可直接调用。
/// </summary>
internal static class FastSyncRendezvousDecisions
{
    /// <summary>
    /// 走向（或位于）集合点时，应作为抢报目标的 syncId / wpIdx。
    /// 复刻 PathExecutor.cs line ~504-522 反查：段内 wpIdx &gt;= currentWpIdx
    /// 且 syncId != null 且未抢报的最小 wpIdx。无候选返回 (null, -1)。
    ///
    /// 关键性质（风险 2）：当 currentWpIdx 自身是集合点且未抢报时，返回 currentWpIdx 自身
    /// （因为判定用 wpIdx &gt;= currentWpIdx 含等号）→ MoveTo(fastSyncWaypoint=wp[K]) 可在趋近时抢报。
    /// </summary>
    public static (string? syncId, int wpIdx) ResolveNextPendingSync(
        IReadOnlyDictionary<int, string?> wpIdxToSyncId,
        int currentWpIdx,
        Func<string, bool> isFastReported)
    {
        if (wpIdxToSyncId == null || wpIdxToSyncId.Count == 0) return (null, -1);
        int bestWpIdx = int.MaxValue;
        string? bestSyncId = null;
        foreach (var kv in wpIdxToSyncId)
        {
            if (kv.Value == null) continue;
            if (kv.Key < currentWpIdx) continue;            // 含等号：currentWpIdx 自身可命中
            if (isFastReported(kv.Value)) continue;
            if (kv.Key < bestWpIdx)
            {
                bestWpIdx = kv.Key;
                bestSyncId = kv.Value;
            }
        }
        if (bestSyncId == null) return (null, -1);
        return (bestSyncId, bestWpIdx);
    }

    /// <summary>
    /// 修复后结构不变量（OQ-1=方案甲）：对任意非传送集合点 waypoint，
    /// 形式集合等待恒在该 waypoint 的 MoveTo/MoveCloseTo 之后触发。
    /// 该函数描述「是否存在抢报先行窗口」——返回 true 表示在该 wpIdx 上
    /// 「先移动（抢报判定先行）后等待」的顺序成立。
    ///
    /// multiplayerCoordinatorNotNull / fastSyncEnabled / isRendezvousWaypoint 任一为 false
    /// → 不进入抢报+形式等待路径，返回 false（无窗口，也无需）。
    /// 三者皆 true → 修复后恒返回 true（移动先于等待，抢报窗口存在），
    /// 无论 prevWpIsTeleport / hasApproachMoveBeforeRendezvous 取值。
    /// </summary>
    public static bool PreClaimWindowExistsBeforeRendezvousWait(
        bool multiplayerCoordinatorNotNull,
        bool fastSyncEnabled,
        bool isRendezvousWaypoint)
    {
        if (!multiplayerCoordinatorNotNull) return false;
        if (!fastSyncEnabled) return false;
        if (!isRendezvousWaypoint) return false;
        // 修复后：MoveTo/MoveCloseTo（含对 fastSyncWaypoint 的抢报判定）恒在形式等待之前执行
        return true;
    }
}
