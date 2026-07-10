namespace BetterGenshinImpact.GameTask.AutoSwitchRoles;

/// <summary>
/// 联机切角色已确定的运行时常量。候选坐标 = 单机 4 候选（OQ-5 确认相同），
/// 探测等待/重试为 OQ-5 确定值。无任何待填占位。
/// 配队页打开判定不在此（直接复用 QuickTeleportAssets.Instance.MapCloseButtonRo，OQ-4）。
/// </summary>
public static class MultiplayerSwitchConstants
{
    /// <summary>OQ-5：候选格子坐标（1080P 基准），与单机相同，从左到右 4 个。</summary>
    public static (double X, double Y)[] CandidateCoords { get; } =
    {
        (460, 538), (792, 538), (1130, 538), (1462, 538)
    };

    /// <summary>OQ-5/R7.2：每点击一个候选后、检测 MapCloseButton 前的等待毫秒数。</summary>
    public const int ProbeClickWaitMs = 300;

    /// <summary>OQ-5/R7.6：探测序列最大重试次数（4 候选全未命中算一轮，最多重试 2 次）。</summary>
    public const int ProbeMaxRetries = 2;
}
