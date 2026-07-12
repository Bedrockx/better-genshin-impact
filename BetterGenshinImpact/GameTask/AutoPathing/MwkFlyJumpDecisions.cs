namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 玛薇卡挑飞相关的纯判定/钳制逻辑。无副作用、无外部依赖，便于属性测试（PBT）。
/// </summary>
public static class MwkFlyJumpDecisions
{
    /// <summary>
    /// 将挑飞距离阈值钳制到合法范围：负数归 0，其余原样返回（无上限）。
    /// </summary>
    public static int Clamp(int distance) => distance < 0 ? 0 : distance;

    /// <summary>
    /// 判定是否应触发玛薇卡挑飞动作。
    /// threshold==0 时恒不触发（等效关闭挑飞，R1.4）；
    /// threshold>0 时需同时满足 distance>threshold 且当前非飞行状态（R1.5/R1.9）。
    /// </summary>
    /// <param name="threshold">挑飞触发距离阈值（已钳制的 >=0 值）</param>
    /// <param name="distance">当前与目标点的距离</param>
    /// <param name="isFlying">当前是否处于飞行状态（Bv.GetMotionStatus == MotionStatus.Fly）</param>
    public static bool ShouldTriggerMwkFlyJump(int threshold, double distance, bool isFlying)
    {
        if (threshold <= 0) return false;
        return distance > threshold && !isFlying;
    }
}
