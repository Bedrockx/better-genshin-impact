#nullable enable

using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 客户端上报到服务端的"单条路线 schema 摘要"（route-variant-sync-by-logical-id spec / D-4）。
/// 与 BgiCoordinatorServer/Models/RouteVariantSchemaItem.cs 严格对称。
///
/// LogicalRouteId 为空字符串时表示老路径，服务端按 R8.3 跳过该项。
/// 不同玩家的 ActualVariantFileName 在同一 LogicalRouteId 下可以不相等（不同玩家跑不同变体是核心价值）。
/// 服务端仅按 LogicalRouteId 分组比对 SyncPointList + TeleportSyncPointSequence。
/// </summary>
public class RouteVariantSchemaItem
{
    public string LogicalRouteId { get; set; } = string.Empty;
    public string ActualVariantFileName { get; set; } = string.Empty;

    /// <summary>本路线手动模式下，按 waypoint 顺序收集到的 SyncPointId 字符串序列。</summary>
    public List<string> SyncPointList { get; set; } = new();

    /// <summary>
    /// 本路线手动模式下，按 waypoint 顺序收集到的传送点 (listIdx, wpIdx) 序列。
    /// 用 int[2] 而非 ValueTuple&lt;int,int&gt; —— SignalR / STJ 序列化对元组支持不稳；用 int[] 兼容性最好。
    /// </summary>
    public List<int[]> TeleportSyncPointSequence { get; set; } = new();
}
