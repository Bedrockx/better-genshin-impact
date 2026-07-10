#nullable enable
namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 战斗结束 join 后台回点循环的决策纯函数集合（PBT 友好，无外部依赖）。
/// 由 fight-end-return-loop-not-joined-movement-overlap-fix spec 引入：
/// 战斗结束时主流程在进入 PathExecutor 战后走回点流程前，需 cancel + join 后台回点循环，
/// 消除两个移动子系统的重叠窗口。本类承载"是否需要 join / join 几个"的判定。
/// </summary>
public static class ReturnLoopJoinDecisions
{
    /// <summary>
    /// 返回战斗结束时需 join 的后台回点循环 Task 数。
    /// 万叶 / 通用循环互斥，最多一个非 null；无循环（单机 / 联机非万叶 + 总开关关）返回 0
    /// → join 段整体跳过（单机零回归）。
    /// </summary>
    public static int JoinTaskCount(bool hasKazuhaTask, bool hasGeneralTask)
    {
        var n = 0;
        if (hasKazuhaTask) n++;
        if (hasGeneralTask) n++;
        return n;
    }

    /// <summary>是否需要执行 join 段（存在任一后台回点循环）。</summary>
    public static bool ShouldJoin(bool hasKazuhaTask, bool hasGeneralTask)
        => JoinTaskCount(hasKazuhaTask, hasGeneralTask) > 0;
}
