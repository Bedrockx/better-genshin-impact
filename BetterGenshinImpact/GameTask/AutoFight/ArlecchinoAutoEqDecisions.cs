namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 阿蕾奇诺普攻自动EQ 放 Q 决策纯函数（PBT 友好，无外部依赖、无屏幕采样）。
/// 详见 .kiro/specs/arlecchino-auto-eq-cd-threshold-config/design.md §Testing Strategy。
/// </summary>
public static class ArlecchinoAutoEqDecisions
{
    /// <summary>
    /// 契空放 Q 的 CD 门控：当前元素战技（E）剩余 CD 严格大于阈值时才允许放 Q。
    /// 语义等价于 Avatar.cs 中原硬编码的 (cc &gt; SkillCdForQ)。
    /// </summary>
    /// <param name="currentSkillCd">当前元素战技（E）剩余冷却时间。</param>
    /// <param name="skillCdForQ">契空放 Q 所需的 E 技能 CD 阈值（AutoFightConfig.SkillCdForQ）。</param>
    public static bool ShouldReleaseQByCd(double currentSkillCd, int skillCdForQ)
        => currentSkillCd > skillCdForQ;
}
