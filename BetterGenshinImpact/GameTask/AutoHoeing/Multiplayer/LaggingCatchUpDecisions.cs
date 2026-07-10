#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 落后成员逐段追赶的决策纯函数（hoeing-multiplayer-lagging-member-catchup spec）。
/// 无外部依赖（不持有 client / logger / coordinator），便于 FsCheck PBT 直接撒输入跑性质。
///
/// 进度为全局单调递增 long：CurrentProgress = 路线 × 1e6 + 段 × 1e3 + 路点。
/// 落后判定区分同路线 / 跨路线：
///   - 跨路线（squad 路线索引 > my 路线索引）：恒触发（大部队进了后面 JSON，铁定该追）。
///   - 同路线：用 (squad - my) / 1e3 计段差，与阈值比。
///   - my >= squad：不触发（防跳过头）。
/// 调用方传入：mySegmentProgress = PathExecutor 本地实时 ComputeProgress；
///             squadSegmentProgress = CurrentPlayerList 缓存归约（房主优先 / 在线最大）。
/// </summary>
public static class LaggingCatchUpDecisions
{
    private const long RouteUnit = 1_000_000; // 路线量级
    private const long SegUnit = 1_000;       // 段量级

    /// <summary>哨兵：大部队进度不可得（无房主、无在线可比）→ 不追赶。</summary>
    public const long Unavailable = -1;

    /// <summary>路线索引 = progress / 1e6。</summary>
    public static long RouteIndex(long progress) => progress / RouteUnit;

    /// <summary>
    /// 段级落后段数：
    ///   - 任一进度 &lt; 0：返回 0（不可比）。
    ///   - 跨路线（squad 路线 &gt; my 路线）：返回一个 &gt;= 任意阈值的大值（long.MaxValue/2），表示"已达触发条件"。
    ///   - 同路线：返回 (squad - my) / 1e3，负数（my 更前）归零。
    /// </summary>
    public static long SegmentsBehind(long squadSegmentProgress, long mySegmentProgress)
    {
        if (squadSegmentProgress < 0 || mySegmentProgress < 0) return 0;
        long squadRoute = RouteIndex(squadSegmentProgress);
        long myRoute = RouteIndex(mySegmentProgress);
        if (squadRoute > myRoute) return long.MaxValue / 2; // 跨路线：恒触发
        if (squadRoute < myRoute) return 0;                 // 我在更后的路线（更前）：不追
        long behind = (squadSegmentProgress - mySegmentProgress) / SegUnit;
        return behind > 0 ? behind : 0;
    }

    /// <summary>是否需要发起追赶。全部守卫满足 + 落后达触发条件 才 true。</summary>
    public static bool ShouldCatchUp(
        bool isMember, bool enabled,
        long mySegmentProgress, long squadSegmentProgress, long lagSegmentThreshold)
    {
        if (!isMember) return false;
        if (!enabled) return false;
        if (squadSegmentProgress < 0) return false;
        if (mySegmentProgress < 0) return false;
        if (lagSegmentThreshold < 1) lagSegmentThreshold = 1;
        return SegmentsBehind(squadSegmentProgress, mySegmentProgress) >= lagSegmentThreshold;
    }

    /// <summary>逐段跳进过程中：是否还要继续跳下一段。与 ShouldCatchUp 同语义（追平即停）。</summary>
    public static bool ShouldContinueSkipping(
        bool isMember, bool enabled,
        long mySegmentProgress, long squadSegmentProgress, long lagSegmentThreshold)
        => ShouldCatchUp(isMember, enabled, mySegmentProgress, squadSegmentProgress, lagSegmentThreshold);

    /// <summary>
    /// 归约大部队段级进度目标：房主进度可得用房主，否则用在线非房主成员最大进度，都没有则 Unavailable。
    /// </summary>
    public static long ResolveSquadSegmentProgress(long? hostSegmentProgress, IReadOnlyCollection<long> peerSegmentProgresses)
    {
        if (hostSegmentProgress.HasValue && hostSegmentProgress.Value >= 0)
            return hostSegmentProgress.Value;
        if (peerSegmentProgresses != null && peerSegmentProgresses.Count > 0)
        {
            var max = peerSegmentProgresses.Where(p => p >= 0).DefaultIfEmpty(Unavailable).Max();
            return max;
        }
        return Unavailable;
    }
}
