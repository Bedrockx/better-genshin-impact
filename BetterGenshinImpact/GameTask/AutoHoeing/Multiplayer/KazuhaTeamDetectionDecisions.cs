#nullable enable
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶声明前的"多次识别表决"纯决策函数集合。
/// 抽离成 static class 是为了 PBT 友好（无外部依赖、可重复调用）。
/// 由 <see cref="AutoHoeingTask"/>.DeclareKazuhaCapabilityIfPresentAsync 使用。
/// kazuha-declare-multi-recognition-vote: 把单次识别升级为 3 次识别 + 严格多数表决。
/// </summary>
public static class KazuhaTeamDetectionDecisions
{
    /// <summary>
    /// 依据多次队伍识别的布尔票集，判定是否应当声明本地含万叶。
    /// 决策规则：严格多数（true 票数严格大于 false 票数才返回 true）。
    /// 空序列 / 平票一律返回 false（保守，假阳性优先规避）。
    /// </summary>
    /// <param name="votes">每次 Recognition_Sample 的布尔结果（识别失败/返回 null 由调用方记为 false）</param>
    /// <returns>true 表示判定为含万叶、应声明；false 表示不含、跳过</returns>
    public static bool ShouldDeclareKazuha(IReadOnlyList<bool>? votes)
    {
        if (votes == null || votes.Count == 0) return false;

        var yes = 0;
        for (var i = 0; i < votes.Count; i++)
        {
            if (votes[i]) yes++;
        }

        var no = votes.Count - yes;
        return yes > no; // 严格多数；平票（yes == no）返回 false（保守）
    }
}
