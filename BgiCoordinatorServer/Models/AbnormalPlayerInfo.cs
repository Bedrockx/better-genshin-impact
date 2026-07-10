namespace BgiCoordinatorServer.Models;

/// <summary>
/// 服务器端异常玩家信息（multiplayer-abnormal-sync-server spec）
/// 用于联机锄地场景下追踪异常玩家状态
/// Validates: Requirements REQ-3.2
/// </summary>
public class AbnormalPlayerInfo
{
    /// <summary>
    /// 玩家 UID
    /// </summary>
    public string PlayerUid { get; set; } = "";

    /// <summary>
    /// 异常发生时所在的线路索引
    /// </summary>
    public int RouteIndex { get; set; }

    /// <summary>
    /// 是否已过本线路的同步点（第一个传送点）
    /// </summary>
    public bool PassedSyncPoint { get; set; }

    /// <summary>
    /// 目标汇合线路索引
    /// - passedSyncPoint=true: routeIndex + 1
    /// - passedSyncPoint=false: routeIndex
    /// </summary>
    public int TargetRouteIndex { get; set; }

    /// <summary>
    /// 异常上报时间（UTC）
    /// </summary>
    public DateTime ReportTime { get; set; }
}
