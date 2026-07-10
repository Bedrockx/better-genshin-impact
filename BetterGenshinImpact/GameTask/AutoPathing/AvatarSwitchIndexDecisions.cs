namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 低血量切人序号纯函数决策（PBT 友好，无外部依赖）。
/// 详见 .kiro/specs/hoeing-low-hp-avatar-switch-index-out-of-range-fix/design.md
/// §Fix Implementation / Correctness Properties。
/// </summary>
public static class AvatarSwitchIndexDecisions
{
    /// <summary>
    /// 队伍有效人数：null / 0 / 负数兜底为 4（保持硬编码 4 的旧行为，最小回归）。
    /// </summary>
    public static int EffectiveAvatarCount(int? actualCount)
        => (actualCount is int c && c > 0) ? c : 4;

    /// <summary>
    /// 下一个角色编号：(current % N) + 1，N 为有效人数。
    /// N == 4 时与原式 (current % 4) + 1 逐字节等价。
    /// </summary>
    public static int NextAvatarIndex(int currentIndex, int effectiveCount)
        => (currentIndex % effectiveCount) + 1;
}
