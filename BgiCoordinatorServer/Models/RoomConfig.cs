namespace BgiCoordinatorServer.Models;

/// <summary>房主锄地配置，同步给所有成员</summary>
public class RoomConfig
{
    public int SyncTimeoutSeconds { get; set; } = 60;
    public int MinPlayersToSync { get; set; } = 0;
    public double SyncPointMinDistance { get; set; } = 30;
    public int StartRouteIndex { get; set; } = 0;
    public bool UseFixedDebugRoutes { get; set; } = false;
    public string FixedDebugRoutePath { get; set; } = "";
    public bool DebugMode { get; set; } = false;
    /// <summary>
    /// 启用万叶聚物同步流程。默认 false，用户需显式打勾才启用，避免无万叶队伍走无效流程。
    /// 替代旧的 KazuhaPlayerIndex 字段（kazuha-player-auto-detection：从"按索引指定"改为"运行时声明"）。
    /// 启用判定：EnableKazuhaSync ∧ isConnected。
    /// </summary>
    public bool EnableKazuhaSync { get; set; } = false;
    /// <summary>万叶聚物完成后非万叶玩家原地再停留的秒数（让吸过来的物品被己方拾取），范围 [0, 30]，默认 1</summary>
    public int KazuhaSyncWaitSeconds { get; set; } = 1;
    /// <summary>万叶聚物同步流程总超时秒数（在战斗点等待 + 聚物动作的总预算），范围 [5, 120]，默认 20</summary>
    public int KazuhaSyncTimeoutSeconds { get; set; } = 20;
    /// <summary>万叶玩家等待 E 技 CD 的最长上限秒数（超时直接尝试释放，由 OCR + 视觉双判决定成败），范围 [3, 10]，默认 5。需保证小于 KazuhaSyncTimeoutSeconds</summary>
    public int KazuhaWaitSkillCdSeconds { get; set; } = 5;
    public int PartyTimeoutSeconds { get; set; } = 300;
    public bool MultiWorldEnabled { get; set; } = false;
    public int MultiWorldCount { get; set; } = 2;
    public string SelectedBuiltinRoute { get; set; } = "";
    /// <summary>联机模式下的战斗超时时间（秒），由房主设定</summary>
    public int FightTimeoutSeconds { get; set; } = 120;
    /// <summary>战斗额外等待时间（秒），同步点超时后为 Fighting 成员额外等待</summary>
    public int FightExtraWaitSeconds { get; set; } = 60;
    /// <summary>重新加入最大等待时间（秒），同步点超时后为 Rejoining/Reviving 成员额外等待</summary>
    public int RejoinMaxWaitSeconds { get; set; } = 300;
    /// <summary>传送点必同步：所有传送点都作为同步等待点</summary>
    public bool SyncAtEveryTeleport { get; set; } = false;
}
