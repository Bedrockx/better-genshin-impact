namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// CameraRotateTask 的纯函数决策集合，用于 PBT 守住"抢锁失败不得误判到位"的语义。
/// 详见 .kiro/specs/fight-return-to-point-seek-rotation-conflict-fix/design.md 改动 7 / Property 2 / 3。
/// </summary>
public static class CameraRotateDecisions
{
    /// <summary>
    /// 判定本轮是否"已转到目标角度"。
    /// measuredDiff == null 表示本轮未真实测量到角度（抢锁失败 / 异常）——绝不判到位（返回 false）。
    /// measuredDiff != null 时沿用原判据：|measuredDiff| &lt; maxDiff + count / 2。
    /// 纯函数：无外部依赖，PBT 友好。
    /// </summary>
    public static bool IsRotationArrived(float? measuredDiff, int maxDiff, int count)
    {
        if (measuredDiff is null)
        {
            return false;
        }
        return System.Math.Abs(measuredDiff.Value) < maxDiff + count / 2.0f;
    }
}
