namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 低血恢复决策纯函数（autoeat-count-overloaded-sentinel-fix spec）。
/// 把散落在 Avatar.cs / PathExecutor.cs 的魔法数字门控（AutoEatCount 的 &lt;1/&lt;2/&gt;=2）
/// 抽为具名、无副作用、可 PBT 的纯函数。三职责拆分：
///   - 用户开关 = PartyConfig.AutoEatEnabled（调用方传入 autoEatEnabled）
///   - 本轮恢复尝试次数 = AutoEatCount（调用方传入 count）
///   - 已放弃嗑药抑制态 = PathingConditionConfig.RecoverSuppressed（调用方传入 suppressed）
/// 本类只做查询，不改任何状态（CQS）。
/// </summary>
public static class AutoEatRecoveryDecisions
{
    /// <summary>嗑小道具次数上限。取代魔法数字 2（原 AutoEatCount &gt;= 2 / &lt; 2）。</summary>
    public const int MaxRecoverAttempts = 2;

    /// <summary>
    /// 是否已达嗑药上限、应直接去七天神像。
    /// 输入域：count ∈ [0, +∞)。等价于原 Avatar.cs 的 AutoEatCount &gt;= 2。
    /// </summary>
    public static bool ShouldGoStatue(int count) => count >= MaxRecoverAttempts;

    /// <summary>
    /// 是否应尝试嗑小道具（QuickUseGadget）。
    /// 输入域：suppressed ∈ {true,false}, count ∈ [0, +∞)。
    /// 抑制态优先：一旦 suppressed = true（已决定去神像）恒返回 false，杜绝去神像期间误触化种匣。
    /// </summary>
    public static bool ShouldUseGadget(bool suppressed, int count)
        => !suppressed && count < MaxRecoverAttempts;

    /// <summary>
    /// 恢复流程后是否应 throw RetryException 重试整条路线（对应 RecoverWhenLowHp 2181/2191）。
    /// 输入域：count ∈ [0, +∞)。语义镜像原 if (AutoEatCount &lt; 2) return; else throw。
    /// 返回 true = 应 throw 重试；false = 应 return 不重试。
    /// </summary>
    public static bool ShouldRetryRoute(int count) => count >= MaxRecoverAttempts;

    /// <summary>
    /// TpStatueOfTheSevenCore 分级恢复：是否进入嗑药子步骤（对应原 count &lt; 2 进入）。
    /// 输入域：autoEatEnabled ∈ {true,false}, count ∈ [0, +∞)。
    /// </summary>
    public static bool ShouldEnterGadgetPhase(bool autoEatEnabled, int count)
        => autoEatEnabled && count < MaxRecoverAttempts;

    /// <summary>
    /// TpStatueOfTheSevenCore 分级恢复：是否在本次真正按 Z（对应原 count &lt; 1）。
    /// 输入域：count ∈ [0, +∞)。
    /// </summary>
    public static bool ShouldFireGadgetThisTick(int count) => count < 1;
}
