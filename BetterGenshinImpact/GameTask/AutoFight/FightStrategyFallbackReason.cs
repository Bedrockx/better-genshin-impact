namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 战斗策略匹配失败兜底的触发原因。
/// 仅用于本地 LogWarning 结构化字段，不上报 SignalR 服务端，不动协议。
/// 详见 bugfix.md §Bug Condition Formalization 三类 C 子条件。
/// </summary>
public enum FightStrategyFallbackReason
{
    /// <summary>队伍识别失败：GetCombatScenesWithRetry 5 次重试均未通过 CheckTeamInitialized 且 CustomAvatarEnabled=false</summary>
    TeamRecognitionFailed,

    /// <summary>无脚本匹配：CombatScriptBag.FindCombatScript 第二轮所有脚本 MatchCount=0</summary>
    NoScriptMatched,

    /// <summary>无可用角色：脚本里的角色全部不在当前队伍 / 全部被 AutoCombatEq 的 XC 列表禁用</summary>
    NoUsableAvatar
}
