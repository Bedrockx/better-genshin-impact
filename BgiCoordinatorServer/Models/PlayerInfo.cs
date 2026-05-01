namespace BgiCoordinatorServer.Models;

public class PlayerInfo
{
    public string ConnectionId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string PlayerUid { get; set; } = "";
    public PlayerStatus Status { get; set; } = PlayerStatus.Waiting;
    public DateTime LastHeartbeat { get; set; }

    // === 路线进度信息（需求 6）===
    /// <summary>当前路线索引（0-based），-1 表示未上报</summary>
    public int CurrentRouteIndex { get; set; } = -1;
    /// <summary>当前路线开始时间（UTC）</summary>
    public DateTime RouteStartTime { get; set; }
    /// <summary>当前路线预估总时间（秒）</summary>
    public double RouteEstimatedSeconds { get; set; }
}
