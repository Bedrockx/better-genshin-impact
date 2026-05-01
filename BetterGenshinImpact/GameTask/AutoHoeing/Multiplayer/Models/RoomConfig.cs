#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

public class RoomConfig
{
    public int SyncTimeoutSeconds { get; set; } = 60;
    public int MinPlayersToSync { get; set; } = 0;
    public double SyncPointMinDistance { get; set; } = 30;
    public int StartRouteIndex { get; set; } = 0;
    public bool UseFixedDebugRoutes { get; set; } = false;
    public string FixedDebugRoutePath { get; set; } = "";
    public bool DebugMode { get; set; } = false;
    public bool ReturnToFightPointAfterBattle { get; set; } = false;
    public int ReturnToFightPointStaySeconds { get; set; } = 5;
    public int KazuhaPlayerIndex { get; set; } = 0;
    public int PartyTimeoutSeconds { get; set; } = 300;
    public bool MultiWorldEnabled { get; set; } = false;
    public int MultiWorldCount { get; set; } = 2;
    public string SelectedBuiltinRoute { get; set; } = "";
    /// <summary>联机模式下的战斗超时时间（秒），由房主设定，覆盖成员本地的 AutoFightConfig.Timeout</summary>
    public int FightTimeoutSeconds { get; set; } = 120;
    /// <summary>战斗额外等待时间（秒），同步点超时后为 Fighting 成员额外等待</summary>
    public int FightExtraWaitSeconds { get; set; } = 60;
    /// <summary>重新加入最大等待时间（秒），同步点超时后为 Rejoining/Reviving 成员额外等待</summary>
    public int RejoinMaxWaitSeconds { get; set; } = 300;
    /// <summary>传送点必同步：所有传送点都作为同步等待点</summary>
    public bool SyncAtEveryTeleport { get; set; } = false;
}
