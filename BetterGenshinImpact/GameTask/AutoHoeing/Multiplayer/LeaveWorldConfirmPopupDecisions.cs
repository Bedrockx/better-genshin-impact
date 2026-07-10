namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// LeaveWorldAsync 内"点击 (1600,1020) 后处理弹窗"段落的单 tick 决策枚举。
/// </summary>
public enum LeaveWorldConfirmPopupTickDecision
{
    /// <summary>第一段轮询：当前 tick 看到弹窗 → 应点击</summary>
    ClickFirstConfirm,
    /// <summary>第一段已点击，等待弹窗消失中 → 当前 tick 弹窗仍在 → 继续等</summary>
    WaitFirstPopupGone,
    /// <summary>第一段已点击，弹窗已消失 → 进入第二段轮询</summary>
    EnterSecondPolling,
    /// <summary>第二段轮询：当前 tick 看到弹窗 → 应点击</summary>
    ClickSecondConfirm,
    /// <summary>本 tick 无操作，下个 tick 继续轮询</summary>
    Idle,
    /// <summary>整段超时（已达总预算）→ 段结束</summary>
    SegmentTimeout,
    /// <summary>第二段轮询超时未发现弹窗（成员单确认） → 段结束（成功状态）</summary>
    SecondPollingTimeoutNoFault,
    /// <summary>双确认完成（第二段点击后） → 段结束</summary>
    BothConfirmsClicked,
}

/// <summary>
/// 单 tick 决策的状态机变量。
/// </summary>
public enum LeaveWorldConfirmPopupPhase
{
    FirstPolling,        // 等待第一弹窗出现
    WaitingForFirstGone, // 第一已点击，等弹窗消失
    SecondPolling,       // 等待第二弹窗出现
    Done,                // 完成
}

public static class LeaveWorldConfirmPopupDecisions
{
    public const int TickIntervalMs = 250;
    public const int FirstPollingMaxMs = 5000;
    public const int FirstPopupGoneMaxMs = 1500;
    public const int SecondPollingMaxMs = 5000;
    public const int SegmentTotalBudgetMs = 8000;

    /// <summary>
    /// 决策单 tick 行为。
    /// </summary>
    /// <param name="phase">当前阶段</param>
    /// <param name="elapsedInPhaseMs">当前阶段已用时</param>
    /// <param name="totalElapsedMs">整段已用时</param>
    /// <param name="popupVisible">本 tick 是否看到 ConfirmBtnRo</param>
    public static LeaveWorldConfirmPopupTickDecision Decide(
        LeaveWorldConfirmPopupPhase phase,
        int elapsedInPhaseMs,
        int totalElapsedMs,
        bool popupVisible)
    {
        // 总预算超限 → 段超时
        if (totalElapsedMs >= SegmentTotalBudgetMs)
            return LeaveWorldConfirmPopupTickDecision.SegmentTimeout;

        switch (phase)
        {
            case LeaveWorldConfirmPopupPhase.FirstPolling:
                if (popupVisible) return LeaveWorldConfirmPopupTickDecision.ClickFirstConfirm;
                if (elapsedInPhaseMs >= FirstPollingMaxMs)
                    return LeaveWorldConfirmPopupTickDecision.SegmentTimeout;
                return LeaveWorldConfirmPopupTickDecision.Idle;

            case LeaveWorldConfirmPopupPhase.WaitingForFirstGone:
                if (!popupVisible) return LeaveWorldConfirmPopupTickDecision.EnterSecondPolling;
                if (elapsedInPhaseMs >= FirstPopupGoneMaxMs)
                    // 等不到消失也强行进入第二段（避免被原弹窗拖死）
                    return LeaveWorldConfirmPopupTickDecision.EnterSecondPolling;
                return LeaveWorldConfirmPopupTickDecision.WaitFirstPopupGone;

            case LeaveWorldConfirmPopupPhase.SecondPolling:
                if (popupVisible) return LeaveWorldConfirmPopupTickDecision.ClickSecondConfirm;
                if (elapsedInPhaseMs >= SecondPollingMaxMs)
                    return LeaveWorldConfirmPopupTickDecision.SecondPollingTimeoutNoFault;
                return LeaveWorldConfirmPopupTickDecision.Idle;

            case LeaveWorldConfirmPopupPhase.Done:
                return LeaveWorldConfirmPopupTickDecision.BothConfirmsClicked;

            default:
                return LeaveWorldConfirmPopupTickDecision.SegmentTimeout;
        }
    }
}
