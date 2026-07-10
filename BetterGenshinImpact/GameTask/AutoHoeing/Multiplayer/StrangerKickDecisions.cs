#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 房主放人阶段"踢陌生人"的决策纯函数（无外部依赖，PBT 友好）。
/// 参考 KazuhaCollectSyncDecisions / HostLeaveEarlyExitDecisions / HostPartyReadinessDecisions 模式。
/// 详见 .kiro/specs/hoeing-party-stranger-kick-race-nodelay-fix/design.md §关键设计决策 A。
/// </summary>
public static class StrangerKickDecisions
{
    /// <summary>连续不匹配达到此阈值才踢，吸收单帧 OCR 抽风。</summary>
    public const int DefaultMissThreshold = 2;

    /// <summary>
    /// 世界是否满员 ⇒ 是否触发逐行成分校验。
    /// worldCount = 踢出按钮数 + 1；不写死按键数，覆盖 2/3/4 人房。
    /// </summary>
    public static bool ShouldTriggerCompositionCheck(int worldCount, int expectedCount)
    {
        if (expectedCount <= 0) return false;   // 期望人数无意义时不触发
        return worldCount >= expectedCount;
    }

    /// <summary>
    /// 推进某身份键的"连续不匹配"计数。
    /// isMatched（在名单中）→ 清零；不匹配 → prev + 1。
    /// 语义对齐 HostLeaveEarlyExitDecisions.NextConsecutiveZero（匹配上清零、否则累加）。
    /// </summary>
    public static int NextConsecutiveMiss(int prev, bool isMatched)
    {
        if (prev < 0) prev = 0;
        return isMatched ? 0 : prev + 1;
    }

    /// <summary>
    /// 连续不匹配达到阈值 ⇒ 才踢。默认阈值 2（连续 2 次不匹配才踢）。
    /// </summary>
    public static bool ShouldKickAfterMisses(int consecutiveMisses, int threshold = DefaultMissThreshold)
    {
        return consecutiveMisses >= threshold;
    }

    /// <summary>
    /// 身份归一：去首尾空格 + 去括号备注 + 全角括号统一，取主名小写串。
    /// 只用于跨轮次的"连续不匹配计数"归属，不参与踢不踢的判定。
    /// </summary>
    public static string NormalizeIdentityKey(string? ocrName)
    {
        if (string.IsNullOrWhiteSpace(ocrName)) return string.Empty;
        var s = ocrName.Trim();
        var bracketIdx = s.IndexOfAny(new[] { '(', '（' });
        if (bracketIdx > 0) s = s[..bracketIdx].Trim();
        return s.ToLowerInvariant();
    }
}

/// <summary>KickStrangersAsync 的扫描结果三态。</summary>
public enum StrangerKickScanResult
{
    /// <summary>未满员 / 名单为空 / 无按钮：本轮不处理。</summary>
    NotFull,
    /// <summary>满员且逐行校验全为名单成员：可以开锄。</summary>
    AllAllowed,
    /// <summary>发现陌生人但连续不匹配未达阈值：本轮不踢，待下一轮确认。</summary>
    PendingStranger,
    /// <summary>本轮踢出了一个陌生人。</summary>
    Kicked,
}
