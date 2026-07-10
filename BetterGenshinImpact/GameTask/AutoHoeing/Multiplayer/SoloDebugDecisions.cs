namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 单人调试模式决策纯函数（无外部依赖，PBT 友好）。
/// 与 ReturnMainUiRecoveryDecisions / RoundSwitchTimeoutDecisions 同模式：
/// 不持有 _client / _logger / 任何可变状态，仅根据传入标志计算决策。
/// 详见 .kiro/specs/hoeing-multiplayer-solo-debug-mode/design.md §Correctness Properties。
/// </summary>
public static class SoloDebugDecisions
{
    /// <summary>
    /// 是否应跳过 WorldStateMonitor 的"connected-but-not-in-game"被踢出终止判定。
    /// 当且仅当单人调试模式开启时返回 true。
    /// soloDebugMode=false 时恒返回 false → 现有终止逻辑逐字节不变（见 §Preservation）。
    /// </summary>
    public static bool ShouldBypassConnectedNotInGameExit(bool soloDebugMode)
    {
        return soloDebugMode;
    }

    /// <summary>
    /// 是否应在线路完整完成时记录 CD（hoeing-multiplayer-solo-debug-mode Req5）。
    /// 规则：单人调试模式（soloDebugMode=true）恒不记 CD（返回 false），避免调试跑污染当天正常锄地；
    /// 否则沿用原逻辑：单机（!multiplayerEnabled）或联机房主（isHost）记 CD，联机成员不记。
    /// 等价于 (!multiplayerEnabled || isHost) &amp;&amp; !soloDebugMode。
    /// soloDebugMode=false 时与改动前 shouldRecordCd 逐字节等价（见 §Preservation UB9）。
    /// </summary>
    public static bool ShouldRecordCd(bool multiplayerEnabled, bool isHost, bool soloDebugMode)
    {
        return (!multiplayerEnabled || isHost) && !soloDebugMode;
    }
}
