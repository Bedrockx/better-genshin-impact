namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 网络恢复调度的纯决策函数（PBT 友好，无 IO / 无静态可变状态）。
/// 集中四个决策点：是否发起恢复 / 是否允许启动新恢复 / ping 是否可发起暂停 /
/// ping 成功是否清除暂停。参考 bgi-implementation-patterns.md §1 决策函数纯化模式。
/// spec: network-recovery-statemachine-never-exits-suspend-fix / design.md §Correctness Properties。
/// </summary>
public static class NetworkRecoveryDecisions
{
    /// <summary>
    /// 决策 A：是否应当进入网络恢复块。只要处于任一挂起态即需要恢复
    /// （与原恢复块入口条件 `IsSuspendedByNetwork || IsSuspendedByWindow` 等价）。
    /// </summary>
    public static bool ShouldEnterRecovery(bool isSuspendedByNetwork, bool isSuspendedByWindow)
        => isSuspendedByNetwork || isSuspendedByWindow;

    /// <summary>
    /// 决策 B（单飞门控）：当前是否允许启动一个**新的** NetworkRecovery.Start。
    /// 仅当没有恢复正在进行时才允许；进行中一律跳过（不重启、不重置 RecoveryNetworkDone）。
    /// 编排层用 `_networkRecoveryGate.Wait(0)` 的结果反推 recoveryInProgress，副作用留在编排层。
    /// </summary>
    public static bool ShouldStartNewRecovery(bool recoveryInProgress)
        => !recoveryInProgress;

    /// <summary>
    /// 决策 C：是否应清除网络暂停（IsSuspendedByNetwork=false 且计数清零）。
    /// 权威依据是"恢复成功"（recoverySucceeded==true，即 RecoveryNetworkDone）；
    /// 或在"本轮未进入恢复"且 ping 明确成功（!pingFailed）时，保留原"ping 成功即清零"语义。
    /// 与 ping 抛异常解耦：恢复成功时无视 ping 结果直接清除。
    /// </summary>
    public static bool ShouldClearNetworkSuspend(bool recoverySucceeded, bool enteredRecovery, bool pingFailed)
        => recoverySucceeded || (!enteredRecovery && !pingFailed);

    /// <summary>
    /// 决策 D：是否累加 ping 失败计数（并在 >=3 时做 www.qq.com 二次判定/回置）。
    /// 仅当"本轮未进入恢复"且 ping 失败时才累加——即 ping 只在**未处于恢复语境**下驱动暂停。
    /// 恢复已成功（recoverySucceeded）或本轮进入过恢复（enteredRecovery）时一律不累加、不回置，
    /// 避免恒失败的 ping 覆盖恢复结果（D-3/D-5 解耦）。
    /// </summary>
    public static bool ShouldCountPingFailure(bool recoverySucceeded, bool enteredRecovery, bool pingFailed)
        => !recoverySucceeded && !enteredRecovery && pingFailed;
}
