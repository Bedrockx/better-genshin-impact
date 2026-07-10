#nullable enable
using System;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶聚物同步功能的纯决策函数集合。
/// 抽离成 static class 是为了 PBT 友好（无外部依赖、可重复调用）。
/// 由 <see cref="KazuhaCollectSyncCoordinator"/> 使用。
/// kazuha-player-auto-detection: 删除原 ResolveKazuhaUid，改为运行时声明协议（DeclareKazuhaCapabilityAsync）。
/// </summary>
public static class KazuhaCollectSyncDecisions
{
    /// <summary>
    /// kazuha-collect-min-buffer-before-stay: 非万叶成员收到二段聚物点坐标后，
    /// 进入 KazuhaSyncWaitSeconds 停留前的最小缓冲时长（固定硬编码，不做配置项）。
    /// </summary>
    public const int MinBufferBeforeStayMs = 1500;

    /// <summary>
    /// 判断当前周期是否需要启用万叶聚物同步流程。
    /// 等价于：<c>EnableKazuhaSync ∧ isConnected</c>。
    /// kazuha-player-auto-detection: 删除 KazuhaPlayerIndex ∈ [1,4] 判定，改为 EnableKazuhaSync 布尔门控。
    /// </summary>
    /// <param name="config">联机锄地配置（房主同步过来的，已包含 EnableKazuhaSync 字段）</param>
    /// <param name="isConnected">SignalR 客户端当前是否已连接服务器</param>
    /// <returns>true 表示启用万叶聚物同步流程</returns>
    public static bool IsEnabled(AutoHoeingConfig config, bool isConnected)
    {
        if (config == null) return false;
        if (!isConnected) return false;
        return config.EnableKazuhaSync;
    }

    /// <summary>
    /// PathExecutor 在战后回点判断是否走"回战斗点 → 进入 WaitAtFightPointAsync"分支的纯决策函数。
    /// 与 <see cref="IsEnabled"/> 区别：本函数不感知 SignalR 连接状态，
    /// 让 IsConnected==false 的临时断连仍能进 WaitAtFightPointAsync 走兜底 Delay（preservation）。
    ///
    /// PathExecutor 必须经由 <see cref="KazuhaCollectSyncCoordinator.IsConfigEnabled"/>（持有 AutoHoeingTask
    /// 拷贝/覆盖后的 _config）调用，不能直接读 TaskContext.Instance().Config.AutoHoeingConfig（全局未应用配置组覆盖）。
    /// </summary>
    public static bool IsConfigEnabledForPathExecutor(AutoHoeingConfig? config)
    {
        return config?.EnableKazuhaSync == true;
    }

    /// <summary>
    /// 非万叶玩家二段精接近完成后应停留的毫秒数，统一返回 <c>Math.Max(0, KazuhaSyncWaitSeconds) * 1000</c>。
    /// hoeing-kazuha-collect-drop-terminal-signal: 砍终态信号闭环后取代死重的 ComputePostTerminalWaitMs，
    /// 不再吃 TerminalKind 参数——离开时机仅由"二段完成 + 固定停留"决定，与终态无关（design.md Property 1）。
    /// </summary>
    public static int ComputePostSecondApproachWaitMs(AutoHoeingConfig config)
    {
        if (config == null) return 0;
        return Math.Max(0, config.KazuhaSyncWaitSeconds) * 1000;
    }

    /// <summary>
    /// BeginPreparationAsync 入口处的三连判 gate：
    /// 仅 IsEnabled ∧ IsCurrentPlayerKazuha ∧ HasCombatScenesCached 全为 true 时才执行实际后台预备；
    /// 任意一项 false 都立即返回 PreparationResult.Skipped，避免走 ReturnMainUiTask 分支按 ESC 中断 MoveCloseTo 走位。
    /// 详见 design.md §3.3 / §6 PBT-3。
    /// </summary>
    public static bool ShouldRunBackgroundPreparation(
        bool isEnabled,
        bool isCurrentPlayerKazuha,
        bool hasCombatScenesCached)
    {
        return isEnabled && isCurrentPlayerKazuha && hasCombatScenesCached;
    }

    /// <summary>
    /// kazuha-collect-min-buffer-before-stay: "收到二段聚物点坐标后至少 1.5 秒才进入 KazuhaSyncWaitSeconds 停留"
    /// 的补足毫秒数纯计算。
    /// 仅走向 A（receivedCollectPoint == true，本周期收到有效坐标）生效：
    ///   - elapsedMsSinceCollectPoint >= 1500 → 返回 0（已满，不额外等）
    ///   - elapsedMsSinceCollectPoint &lt;  1500 → 返回 ceil(1500 - elapsed)（向上取整、非负、&lt;=1500）
    /// 走向 B（receivedCollectPoint == false，没收到坐标 / syncKey 不匹配）→ 恒返回 0（不补足，保持现状）。
    /// 缓冲是"额外前置"，不替代 ComputePostSecondApproachWaitMs 的停留。无外部依赖（PBT 友好）。
    /// </summary>
    /// <param name="receivedCollectPoint">本周期是否收到有效二段聚物点坐标（走向 A==true）</param>
    /// <param name="elapsedMsSinceCollectPoint">从收到坐标到现在经过的毫秒数（调用方用 UtcNow - _lastCollectPointTimeUtc 算得）</param>
    public static int ComputeMinBufferRemainMs(bool receivedCollectPoint, double elapsedMsSinceCollectPoint)
    {
        if (!receivedCollectPoint) return 0;
        var remain = MinBufferBeforeStayMs - elapsedMsSinceCollectPoint;
        return remain <= 0 ? 0 : (int)Math.Ceiling(remain);
    }
}
