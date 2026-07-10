namespace BetterGenshinImpact.GameTask.AutoHoeing;

/// <summary>
/// 联机换队重试的纯函数决策（hoeing-multiplayer-party-switch-combat-fail-statue-fallback-fix）。
///
/// 把"本次失败后是否应传送七天神像 / 是否应继续重试"抽成无副作用的纯函数，
/// 便于 PBT 全输入域验证，并让 <see cref="AutoHoeingTask"/>.SwitchMultiplayerParty() 的
/// 循环体决策集中在一处。
/// </summary>
public static class SwitchPartyRetryDecisions
{
    /// <summary>换队最大重试次数（保持现有 5 次上限）。</summary>
    public const int MaxRetry = 5;

    /// <summary>
    /// 本次尝试失败后，是否应在下一次重试前传送七天神像。
    /// 规则：本次失败（attemptFailed）且仍有后续重试机会（retry &lt; MaxRetry）才传送。
    /// 第 1 次失败起即生效（不留原地重试）；最后一次（retry == MaxRetry）失败后无后续重试，不再传送。
    /// </summary>
    public static bool ShouldTeleportBeforeRetry(int retry, bool attemptFailed)
    {
        if (!attemptFailed) return false;
        return retry < MaxRetry;
    }

    /// <summary>
    /// 是否应继续下一次重试。
    /// 规则：本次成功（switchSuccess）则不再重试；否则在未达上限（retry &lt; MaxRetry）时继续。
    /// </summary>
    public static bool ShouldContinueRetry(int retry, bool switchSuccess)
    {
        if (switchSuccess) return false;
        return retry < MaxRetry;
    }
}
