#nullable enable

using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

/// <summary>
/// PathingTask 纯函数辅助（route-variant-sync-by-logical-id spec / R2.1）。
/// </summary>
public static class PathingTaskHelper
{
    /// <summary>
    /// 判断路线是否进入"手动同步模式"。
    /// 触发条件：LogicalRouteId 非空 OR 任意 waypoint 的 SyncPointId 非空。
    /// 单机模式不调用本函数；联机模式 PathExecutor 在路线段循环开始前调用一次。
    /// </summary>
    public static bool IsManualMode(PathingTask task)
    {
        if (task == null) return false;
        if (!string.IsNullOrEmpty(task.LogicalRouteId)) return true;
        return task.Positions.Any(w => !string.IsNullOrEmpty(w.SyncPointId));
    }
}
