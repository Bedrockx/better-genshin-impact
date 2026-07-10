#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

public class PlayerInfo
{
    public string ConnectionId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string PlayerUid { get; set; } = "";
    public PlayerStatus Status { get; set; } = PlayerStatus.Waiting;

    /// <summary>
    /// 是否为房主（根据 PlayerUid 判断）
    /// </summary>
    public bool IsHost { get; set; }

    /// <summary>
    /// 段级进度（hoeing-multiplayer-lagging-member-catchup spec）：
    /// 全局单调递增 long = 路线 × 1e6 + 段 × 1e3 + 路点。
    /// 服务端在每个同步点 WaitForAllPlayers 与跳线 ReportMemberProgress 刷新后，
    /// 经追加的 PlayerListUpdated 广播推送，客户端 CurrentPlayerList 缓存接住，
    /// 供落后追赶判定（TryGetLaggingCatchUpDecision）读取大部队段级进度。
    /// 初始 -1 表示未上报/不可得。旧服务端不推送该字段 → 保持默认 -1。
    /// </summary>
    public long CurrentProgress { get; set; } = -1;
}
