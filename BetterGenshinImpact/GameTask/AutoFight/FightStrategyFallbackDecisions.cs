#nullable enable
using System;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 战斗策略匹配失败兜底决策的纯函数集合。
/// 抽离成 static class 是为了 PBT 友好（无外部依赖、可重复调用）。
/// 由 <see cref="AutoFightTask"/> 在三个注入点（A1/A2/A3）调用决定是否走兜底分支。
/// 详见 design.md §1.3 / §2.1。
/// </summary>
public static class FightStrategyFallbackDecisions
{
    /// <summary>
    /// 判断当前 fight waypoint 是否应进入"内置通用按键宏兜底战斗"分支。
    ///
    /// 三个 C 子条件（design.md §1.2 流程图 与 bugfix.md §Bug Condition Formalization 对齐）：
    /// - C1：联机锄地 + 队伍识别失败 + 未启用 CustomAvatar 兜底
    /// - C2：联机锄地 + 队伍识别成功 + 无任何脚本匹配
    /// - C3：联机锄地 + 队伍识别成功 + 有匹配但脚本里的角色全部不在当前队伍 / 全部被 ban
    ///
    /// 任一条件命中即返回 true。单机路径下 isMultiplayerHoeing=false 始终返回 false（preservation）。
    /// </summary>
    /// <param name="isMultiplayerHoeing">PathingConditionConfig.MultiplayerFightTimeoutOverride.HasValue</param>
    /// <param name="teamRecognized">GetCombatScenesWithRetry 5 次内是否成功（CustomAvatar 兜底也算成功）</param>
    /// <param name="customAvatarEnabled">Config.CustomAvatarConfigOut.CustomAvatarEnabled</param>
    /// <param name="matchedScriptCount">FindCombatScript 第二轮所有脚本中 MatchCount &gt; 0 的脚本数</param>
    /// <param name="availableAvatarCount">commandAvatarNames.Count（脚本角色 ∩ 当前队伍 - 禁用列表）</param>
    /// <returns>true = 进入兜底分支；false = 走正常流程或抛异常（单机路径）</returns>
    public static bool ShouldUseFallback(
        bool isMultiplayerHoeing,
        bool teamRecognized,
        bool customAvatarEnabled,
        int matchedScriptCount,
        int availableAvatarCount)
    {
        if (!isMultiplayerHoeing) return false;

        // C1: 队伍识别失败 + 无 CustomAvatar 兜底
        if (!teamRecognized && !customAvatarEnabled) return true;

        // C2: 队伍识别成功 + 无脚本匹配
        if (teamRecognized && matchedScriptCount == 0) return true;

        // C3: 队伍识别成功 + 有匹配但无可用角色
        if (teamRecognized && matchedScriptCount >= 1 && availableAvatarCount == 0) return true;

        return false;
    }
}
