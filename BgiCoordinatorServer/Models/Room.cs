namespace BgiCoordinatorServer.Models;

public class Room
{
    public string Code { get; set; } = "";
    public string HostConnectionId { get; set; } = "";
    public List<PlayerInfo> Players { get; set; } = [];
    public DateTime CreatedAt { get; set; }

    /// <summary>syncPointId → 已到达的 connectionId 集合</summary>
    public Dictionary<string, HashSet<string>> ArrivalSets { get; set; } = [];

    /// <summary>syncPointId → 已完成战斗的 connectionId 集合</summary>
    public Dictionary<string, HashSet<string>> FightDoneSets { get; set; } = [];

    /// <summary>房主筛选后的最终路线文件名列表（按执行顺序）</summary>
    public List<string> HostRouteList { get; set; } = [];

    /// <summary>房间白名单</summary>
    public List<string> Whitelist { get; set; } = [];

    /// <summary>已完成路线验证的 connectionId 集合</summary>
    public HashSet<string> RouteVerificationDoneSet { get; set; } = [];

    /// <summary>已加入世界的 connectionId 集合</summary>
    public HashSet<string> WorldJoinedSet { get; set; } = [];

    /// <summary>房间期望人数</summary>
    public int ExpectedPlayerCount { get; set; } = 4;

    /// <summary>房主锄地配置</summary>
    public RoomConfig? HostConfig { get; set; }

    /// <summary>房主是否已进入等待状态</summary>
    public bool HostReady { get; set; } = false;

    /// <summary>
    /// 房间是否已开锄。房主调用 MarkRoomStarted 后置 true，从此 JoinRoom 拒绝非重连新玩家。
    /// 一旦 true 在房间销毁前不复位（多世界轮换由新房间天然 IsStarted=false 承担解锁）。
    /// 不进入 RoomSummary（GetOnlineRooms 已在服务端做完过滤，前端无须感知）。
    /// 详见 spec lock-room-after-start。
    /// </summary>
    public bool IsStarted { get; set; } = false;

    /// <summary>当前世界轮次（多轮世界支持）</summary>
    public int CurrentWorldRound { get; set; } = 0;

    /// <summary>玩家等待点上报缓存：playerUid → WaitPointReport</summary>
    public Dictionary<string, WaitPointReport> WaitPoints { get; set; } = [];

    /// <summary>协调后的统一等待点</summary>
    public CoordinatedWaitPoint? CoordinatedWaitPoint { get; set; }

    // === 异常等待协调机制字段（multiplayer-abnormal-wait-coordination spec）===
    // Validates: Requirements 1.1, 1.3, 1.4

    /// <summary>
    /// 玩家异常状态：playerUid → AbnormalPlayerState
    /// 服务端维护所有玩家的异常状态，用于计算统一等待点
    /// </summary>
    public Dictionary<string, AbnormalPlayerState> AbnormalPlayerStates { get; set; } = new();

    /// <summary>
    /// 当前统一等待点（服务端计算）
    /// 指示正常玩家应在何处等待异常玩家
    /// </summary>
    public UnifiedWaitPoint? CurrentUnifiedWaitPoint { get; set; }

    /// <summary>
    /// 等待点到达记录：syncPointId → 已到达的 playerUid 集合
    /// 用于追踪哪些玩家已到达统一等待点
    /// </summary>
    public Dictionary<string, HashSet<string>> WaitPointArrivals { get; set; } = new();

    // === 异常中断重对齐机制字段（multiplayer-abort-and-realign spec）===

    /// <summary>
    /// 当前重对齐流程（null 表示没有进行中的重对齐）
    /// 当检测到异常玩家时创建，所有玩家对齐完成后清除
    /// </summary>
    public RealignProcess? CurrentRealignProcess { get; set; }
    
    // === 联机锄地异常同步机制字段（multiplayer-abnormal-sync-server spec）===
    // Validates: Requirements REQ-3.1

    /// <summary>
    /// 异常玩家状态：playerUid → AbnormalPlayerInfo
    /// 用于联机锄地场景下追踪异常玩家状态
    /// </summary>
    public Dictionary<string, AbnormalPlayerInfo> AbnormalPlayerInfos { get; set; } = new();
    
    // === 强制线路同步机制字段（multiplayer-route-enforcement spec）===
    
    /// <summary>
    /// 是否启用强制线路同步（默认启用）
    /// 启用后服务器会定期检测线路偏差并强制同步
    /// </summary>
    public bool RouteEnforcementEnabled { get; set; } = true;
    
    /// <summary>
    /// 线路偏差阈值（默认 1）
    /// 当玩家之间线路索引差异超过此阈值时触发强制同步
    /// </summary>
    public int RouteEnforcementThreshold { get; set; } = 1;

    // === 万叶聚物同步机制字段（multiplayer-kazuha-collect-sync + kazuha-player-auto-detection）===
    /// <summary>
    /// 万叶聚物候选玩家列表，按 SignalR 调用到达顺序保存所有声明过候选身份的玩家。
    /// 由 CoordinatorHub.DeclareKazuhaCapability 追加；OnDisconnectedAsync 移除断线者。
    /// 第一个候选默认成为当前 Kazuha；当前 Kazuha 断线时按列表顺序选下一个仍在线者接管。
    /// </summary>
    public List<KazuhaCandidate> KazuhaCandidates { get; set; } = [];

    /// <summary>
    /// 万叶聚物同步房间状态：跟踪当前周期内已到达战斗点的玩家、终态广播守卫等。
    /// 设计见 design.md "Data Models §2 服务端房间维度状态"。
    /// </summary>
    public KazuhaCollectRoomState KazuhaCollect { get; set; } = new();
}

/// <summary>
/// 万叶聚物候选玩家（kazuha-player-auto-detection）。
/// 客户端识别本地联机队伍含万叶后，调用 DeclareKazuhaCapability 追加到 Room.KazuhaCandidates。
/// </summary>
public class KazuhaCandidate
{
    public string ConnectionId { get; set; } = "";
    public string PlayerUid { get; set; } = "";
}

/// <summary>
/// 单个房间内"万叶聚物同步"的服务端状态聚合。
/// 通过 TerminalBroadcasted 守卫保证同一周期内 KazuhaCollectFinished / KazuhaCollectSkipped 至多广播一次（Property 8）。
/// </summary>
public class KazuhaCollectRoomState
{
    /// <summary>当前周期 ID。每收到首个 NotifyKazuhaArrivedAtFightPoint 且上一周期 TerminalBroadcasted=true 时递增。</summary>
    public long CurrentCycleId { get; set; } = 0;

    /// <summary>当前周期已上报 AtFightPoint 的玩家 ConnectionId 集合。</summary>
    public HashSet<string> ArrivedAtFightPoint { get; set; } = new();

    /// <summary>当前周期是否已广播终态事件（Finished / Skipped 二选一）。</summary>
    public bool TerminalBroadcasted { get; set; } = false;

    /// <summary>当前周期终态类型："Finished" / "Skipped" / null（未广播）。</summary>
    public string? TerminalKind { get; set; }

    /// <summary>
    /// 当前周期"万叶玩家"的 ConnectionId。
    /// kazuha-player-auto-detection: 由 KazuhaCandidates 按声明先后顺序选第一个在线者填充；
    /// 当前 Kazuha 断线时切换到下一个在线候选；候选耗尽时置 null。
    /// 用于 OnDisconnectedAsync 检测万叶离线时的自动 Skipped 广播。
    /// </summary>
    public string? KazuhaConnectionId { get; set; }

    /// <summary>
    /// 当前周期的 syncKey（由首个 NotifyKazuhaArrivedAtFightPoint 调用方传入）。
    /// 在 KazuhaCollectStarted / KazuhaCollectFinished / KazuhaCollectSkipped 三个广播中携带，
    /// 让落后客户端进入 WaitAsNonKazuhaAsync 时能精确判断"上次终态广播是不是当前 syncKey"，
    /// 避免错过事件后空等 KazuhaSyncTimeoutSeconds 超时（multiplayer-kazuha-pre-cast-positioning Q3）。
    /// </summary>
    public string? CurrentSyncKey { get; set; }
}
