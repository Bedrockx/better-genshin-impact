using System;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 焦点恢复决策（PBT 友好的纯函数）。
/// 单纯判断"是否需要恢复 + 怎么恢复"，不再有放弃 / 节流路径——
/// 用户开 RestoreFocusOnLostEnabled 即明示"持续抢焦点直到成功"。
///
/// **References**: spec focus-recovery-no-budget-limit / bugfix.md §4 EB-1/EB-2
/// </summary>
public enum FocusRecoveryDecision
{
    /// <summary>跳过恢复（Cfg=Off 旧分支由调用方处理 / 焦点已回到原神）。</summary>
    Skip,

    /// <summary>原神窗口处于 Iconic（最小化），需要 ShowWindow(SW_RESTORE) 恢复显示。</summary>
    TryRestoreIconic,

    /// <summary>原神窗口已可见但不在前台，需要 FocusWindow 切前台。</summary>
    TryFocus,
}

/// <summary>
/// 焦点恢复决策输入快照（简化为 3 字段，无预算追踪）。
/// </summary>
public readonly record struct FocusRecoveryState(
    bool RestoreFocusOnLost,
    bool ForegroundIsGenshin,
    bool GameWindowMinimized);

/// <summary>
/// 焦点恢复决策表（纯函数）。无 N/T/Cooldown 预算上限。
/// </summary>
public static class FocusRecoveryDecisions
{
    /// <summary>主决策：根据 state 路由到 Skip / TryRestoreIconic / TryFocus。</summary>
    public static FocusRecoveryDecision Decide(FocusRecoveryState s)
    {
        if (!s.RestoreFocusOnLost) return FocusRecoveryDecision.Skip;
        if (s.ForegroundIsGenshin) return FocusRecoveryDecision.Skip;
        if (s.GameWindowMinimized) return FocusRecoveryDecision.TryRestoreIconic;
        return FocusRecoveryDecision.TryFocus;
    }
}
