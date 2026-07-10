namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// LeaveWorldAsync 单轮决策枚举。
/// </summary>
public enum LeaveWorldDecision
{
    /// <summary>循环开头检测到已在自己世界，直接返回成功（短路）</summary>
    ShortCircuitSuccess,
    /// <summary>本轮"开 F2 + 点 (1600,1020) + 等加载"流程后复核成功，返回 true</summary>
    FinalSuccess,
    /// <summary>本轮未确认成功，进入下一轮重试</summary>
    ProceedRetry,
    /// <summary>5 次重试全部失败，返回 false</summary>
    FinalFailure,
}

/// <summary>
/// LeaveWorldAsync 单轮基于纯布尔信号的决策函数（PBT 友好）。
/// 把"判定 / 控制流"与"UI 副作用"解耦：副作用仍由 LeaveWorldAsync 触发，
/// 但决策逻辑可在测试中以纯输入域穷尽。
/// </summary>
public static class LeaveWorldDecisions
{
    /// <summary>
    /// 单轮决策。
    /// </summary>
    /// <param name="attempt">当前轮次（1..maxAttempts）</param>
    /// <param name="maxAttempts">最大轮次（生产值 5）</param>
    /// <param name="mainUiBeforeFlow">回主界面 + 短路探测时是否在主界面（WaitForMainUi 返回值）</param>
    /// <param name="backInOwnWorldOnEntry">短路探测：当前已在自己单人世界</param>
    /// <param name="openCoOpScreenOk">主流程"打开 F2"是否成功</param>
    /// <param name="mainUiAfterLeaveClick">点 (1600,1020) + 弹窗处理后 WaitForMainUi(10) 是否成功</param>
    /// <param name="backInOwnWorldAfterLeave">复核阶段是否已回到自己单人世界</param>
    public static LeaveWorldDecision Decide(
        int attempt,
        int maxAttempts,
        bool mainUiBeforeFlow,
        bool backInOwnWorldOnEntry,
        bool openCoOpScreenOk,
        bool mainUiAfterLeaveClick,
        bool backInOwnWorldAfterLeave)
    {
        // 回主界面失败 → 重试（5 轮内）/ 失败兜底
        if (!mainUiBeforeFlow)
            return attempt >= maxAttempts ? LeaveWorldDecision.FinalFailure : LeaveWorldDecision.ProceedRetry;

        // 强成功判据：已在自己世界（任一信号源）→ 直接成功
        // 短路判据：循环开头探测到入参即在自己世界。
        if (backInOwnWorldOnEntry)
            return LeaveWorldDecision.ShortCircuitSuccess;
        // 复核判据：本轮主流程结束后探测到已在自己世界。
        // 该判据胜过"openCoOpScreenOk / mainUiAfterLeaveClick"等过程信号——
        // 即使中间某步看起来"失败"，只要最终事实是"已回到自己世界"就报告成功，
        // 避免旧实现"开 F2 是否成功"这种脏代理在按键被吞 / 加载中间帧时误判重试。
        if (backInOwnWorldAfterLeave)
            return LeaveWorldDecision.FinalSuccess;

        // 否则：本轮过程信号任一失败 → 重试 / 兜底失败
        if (!openCoOpScreenOk)
            return attempt >= maxAttempts ? LeaveWorldDecision.FinalFailure : LeaveWorldDecision.ProceedRetry;
        if (!mainUiAfterLeaveClick)
            return attempt >= maxAttempts ? LeaveWorldDecision.FinalFailure : LeaveWorldDecision.ProceedRetry;

        // 主流程跑完但未确认回到自己世界 → 重试
        return attempt >= maxAttempts ? LeaveWorldDecision.FinalFailure : LeaveWorldDecision.ProceedRetry;
    }
}
