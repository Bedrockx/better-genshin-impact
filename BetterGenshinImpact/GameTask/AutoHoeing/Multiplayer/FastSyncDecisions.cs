using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 快速同步点抢报决策的纯函数集合。
///
/// 与 bgi-implementation-patterns.md §1 "决策函数纯化" 模式对齐：
/// - 仅依赖输入参数（AutoHoeingConfig 的字段值 / 直接参数），无外部依赖
/// - 不持有 logger / 不读 SignalR 状态 / 不调 TaskContext
/// - 便于 PBT 直接撒输入跑性质（详见 design.md §6 / §8.1）
///
/// internal static 可见性 — 复用 BetterGenshinImpact/AssemblyInfo.cs 中已配置的
/// [InternalsVisibleTo("BetterGenshinImpact.UnitTest")]，单元测试可直接调用。
/// </summary>
internal static class FastSyncDecisions
{
    /// <summary>
    /// 判定路径同步点抢报是否应该触发（fastsync-redesign-parameter-passing spec / OQ-7=a）。
    ///
    /// 7 个守门条件全 AND，任一不满足返回 false：
    ///   fastSyncEnabled=false → false（用户在配置组/全局关闭了"快速同步点抢报"开关，
    ///                                    fastsync-claim-respect-enable-toggle 修复：
    ///                                    关闭后只走严格等待，绝不 fire-and-forget 抢报）
    ///   alreadyReported=true  → false（一次性 bool 短路，避免重复抢报）
    ///   isMultiplayer=false   → false（单机零回归）
    ///   isConnected=false     → false（断线时不抢报）
    ///   fastSyncId=null       → false（waypoint 不在 _wpIdxToSyncIdCache）
    ///   distance NaN/Inf/&lt;0 → false（Navigation 失败兜底）
    ///   distance ≤ pathingDistanceThreshold → true（命中抢报）
    ///
    /// 取代旧的 ShouldArmPathingWatcher + ShouldFastReport 两层决策（已删）；
    /// 调用方在 MoveTo / MoveCloseTo 已有的 while 循环里直接 inline。
    /// </summary>
    public static bool ShouldFastReportInPathing(
        double distance,
        double pathingDistanceThreshold,
        string? fastSyncId,
        bool isMultiplayer,
        bool isConnected,
        bool alreadyReported,
        bool fastSyncEnabled)
    {
        if (!fastSyncEnabled) return false;
        if (alreadyReported) return false;
        if (!isMultiplayer || !isConnected) return false;
        if (fastSyncId == null) return false;
        if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0) return false;
        return distance <= pathingDistanceThreshold;
    }

    /// <summary>
    /// 把 FastSyncPathingDistance 持久化值 clamp 到合法范围 [5.0, 30.0]。
    /// NaN / Infinity → 默认 10.0（与 AutoHoeingConfig 字段默认值一致）。
    /// Validates: requirements FR25 / FR27
    /// </summary>
    public static double ClampPathingDistance(double raw)
    {
        if (double.IsNaN(raw) || double.IsInfinity(raw)) return 10.0;
        return Math.Clamp(raw, 5.0, 30.0);
    }

    /// <summary>
    /// 把 FastSyncTeleportLoadingDelayMs 持久化值 clamp 到合法范围 [0, 3000]。
    /// Validates: requirements FR26 / FR27
    /// </summary>
    public static int ClampTeleportDelay(int raw)
    {
        return Math.Clamp(raw, 0, 3000);
    }

    /// <summary>
    /// 判定一个同步点 syncId 是否为 strict（严格抢报）模式。
    /// 规则：syncId 以序数比较（Ordinal，大小写敏感）包含小写子串 "strict" 即为 strict。
    ///   - "strict_1" / "mystrict2" / "s1_strict" → true
    ///   - "Strict_1" / "STRICT" / "fight_1"      → false
    ///   - null / ""                              → false
    /// 详见 .kiro/specs/hoeing-strict-syncpoint-no-lookahead-preclaim/requirements.md Requirement 1。
    /// </summary>
    public static bool IsStrictSyncId(string? syncId)
    {
        if (string.IsNullOrEmpty(syncId)) return false;
        return syncId.IndexOf("strict", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// 判定一个 look-ahead 候选同步点是否允许作为「提前抢报」目标。
    ///
    /// 普通同步点：恒允许（保持现有 look-ahead 行为）。
    /// strict 同步点：仅当角色当前正处理的节点就是该候选节点本身（candidateWpIdx == currentWpIdx）才允许；
    ///                否则不允许（关闭前序节点的提前抢报，方案甲）。
    ///
    /// 返回 false 表示：调用方应跳过该候选，继续向后查找下一个候选（OQ-1 方案 a）。
    /// 详见 design.md §1.2。
    /// </summary>
    public static bool IsLookAheadAllowedForCandidate(string? candidateSyncId, int candidateWpIdx, int currentWpIdx)
    {
        if (!IsStrictSyncId(candidateSyncId)) return true;
        return candidateWpIdx == currentWpIdx;
    }
}
