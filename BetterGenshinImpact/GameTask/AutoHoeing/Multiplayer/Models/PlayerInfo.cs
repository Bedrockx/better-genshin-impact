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
}
