using System;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 截图重试决策枚举。
///
/// **References**: spec capture-failure-suspend-signal / bugfix.md §5 D1-D6 / design.md §2.1
/// </summary>
public enum CaptureRetryDecision
{
    /// <summary>当次 Capture() 已返回非 null Mat，调用方直接 return。</summary>
    ReturnImage,

    /// <summary>仍在 MaxRecoveryWait 时长内，应继续 Sleep(RetryDelay) 后重试。</summary>
    RetryAfterDelay,

    /// <summary>已达 MaxRecoveryWait 时长，应放弃（调用方 SHALL throw RetryException）。</summary>
    Abandon,
}

/// <summary>
/// 截图重试决策表（纯函数，PBT 友好）。
/// 不持有任何外部依赖（无 logger / GameCapture / Mat）。
///
/// 决策基于 <c>elapsed</c>（自首次失败以来的等待时长）而非"重试次数"：
/// 30 秒恢复窗口 + 200 ms 重试间隔覆盖典型场景（UAC / Win+L / 切桌面 / 短弹窗）。
/// </summary>
public static class CaptureRetryDecisions
{
    /// <summary>D1：恢复等待最大时长（30 秒）。覆盖 UAC / Win+L / 切桌面 / 长弹窗。</summary>
    public static readonly TimeSpan MaxRecoveryWait = TimeSpan.FromSeconds(30);

    /// <summary>D2：每次重试前的等待间隔（200 ms）。30 s / 200 ms = 最多 ~150 次 Capture() 机会。</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>D3：恢复期内进度 Debug 日志间隔（5 秒）。30 s 最多 6 条 Debug，不刷屏。</summary>
    public static readonly TimeSpan ProgressLogInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Capture 持续 null 多久后触发 capture session 重启（2s）。
    /// 见 spec graphics-capture-session-auto-restart / bugfix.md §6 D2。
    /// </summary>
    public static readonly TimeSpan CaptureRestartThreshold = TimeSpan.FromSeconds(2);

    /// <summary>主决策：根据 elapsed 路由到 ReturnImage / RetryAfterDelay / Abandon。</summary>
    public static CaptureRetryDecision Decide(bool lastAttemptSucceeded, TimeSpan elapsed)
    {
        if (lastAttemptSucceeded) return CaptureRetryDecision.ReturnImage;
        if (elapsed >= MaxRecoveryWait) return CaptureRetryDecision.Abandon;
        return CaptureRetryDecision.RetryAfterDelay;
    }

    /// <summary>是否到了打进度 Debug 的时机（每 ProgressLogInterval 一次）。</summary>
    public static bool ShouldLogProgress(TimeSpan elapsed, TimeSpan lastLoggedAt)
    {
        return (elapsed - lastLoggedAt) >= ProgressLogInterval;
    }
}
