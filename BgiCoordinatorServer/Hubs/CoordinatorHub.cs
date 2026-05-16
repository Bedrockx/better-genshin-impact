using BgiCoordinatorServer.Models;
using BgiCoordinatorServer.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace BgiCoordinatorServer.Hubs;

public class CoordinatorHub : Hub
{
    private readonly RoomManager _roomManager;
    private readonly ILogger<CoordinatorHub> _logger;

    // 每个房间的路线上报缓存：roomCode → (connectionId → routes)
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<RouteHash>>>
        RouteReports = new();

    public CoordinatorHub(RoomManager roomManager, ILogger<CoordinatorHub> logger)
    {
        _roomManager = roomManager;
        _logger = logger;
    }

    /// <summary>创建房间，返回房间码</summary>
    public async Task<string> CreateRoom(string playerName = "", List<string>? whitelist = null, string playerUid = "", int expectedPlayerCount = 4)
    {
        _logger.LogInformation("CreateRoom 收到参数: playerName={Name}, playerUid={Uid}, expectedPlayerCount={Count}, whitelist={WL}",
            playerName, playerUid, expectedPlayerCount, whitelist != null ? string.Join(",", whitelist) : "null");
        var code = _roomManager.CreateRoom(Context.ConnectionId, playerName, whitelist, playerUid, expectedPlayerCount);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        _logger.LogInformation("连接 {ConnId}({Name}) 创建房间 {Code}", Context.ConnectionId, playerName, code);

        var room = _roomManager.GetRoom(code)!;
        await Clients.Group(code).SendAsync("PlayerListUpdated", room.Players);
        return code;
    }

    /// <summary>加入房间，广播 PlayerListUpdated</summary>
    public async Task<bool> JoinRoom(string roomCode, string playerName = "", string playerUid = "")
    {
        var playerId = Context.ConnectionId;
        var (success, error) = _roomManager.JoinRoom(roomCode, Context.ConnectionId, playerId, playerName, playerUid);

        if (!success)
        {
            _logger.LogWarning("连接 {ConnId} 加入房间 {Code} 失败：{Error}",
                Context.ConnectionId, roomCode, error);
            return false;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        _logger.LogInformation("连接 {ConnId} 加入房间 {Code}", Context.ConnectionId, roomCode);

        var room = _roomManager.GetRoom(roomCode)!;
        await Clients.Group(roomCode).SendAsync("PlayerListUpdated", room.Players);
        return true;
    }

    /// <summary>离开房间，广播 PlayerListUpdated</summary>
    public async Task LeaveRoom()
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        var affectedCodes = _roomManager.LeaveRoom(Context.ConnectionId);

        foreach (var code in affectedCodes)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, code);
            var updatedRoom = _roomManager.GetRoom(code);
            var players = updatedRoom?.Players ?? [];
            await Clients.Group(code).SendAsync("PlayerListUpdated", players);
        }

        _logger.LogInformation("连接 {ConnId} 离开房间", Context.ConnectionId);
    }

    /// <summary>上报路线清单，所有成员上报后对比 MD5，广播差异或验证通过</summary>
    public async Task ReportRouteList(List<RouteHash> routes)
    {
        _logger.LogInformation("[ReportRouteList] 连接 {ConnId} 上报路线清单，共 {Count} 条", Context.ConnectionId, routes?.Count ?? 0);
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null)
        {
            _logger.LogWarning("[ReportRouteList] 连接 {ConnId} 未在任何房间中，忽略路线上报", Context.ConnectionId);
            return;
        }
        _logger.LogInformation("[ReportRouteList] 连接 {ConnId} 在房间 {Code} 中上报路线", Context.ConnectionId, roomCode);

        var roomReports = RouteReports.GetOrAdd(roomCode, _ => new ConcurrentDictionary<string, List<RouteHash>>());
        roomReports[Context.ConnectionId] = routes;

        // 检查是否所有在线成员都已上报
        List<string> onlineConnIds;
        lock (room)
        {
            onlineConnIds = room.Players.Select(p => p.ConnectionId).ToList();
        }

        if (!onlineConnIds.All(id => roomReports.ContainsKey(id)))
        {
            _logger.LogInformation("[ReportRouteList] 房间 {Code} 等待其他玩家上报，已上报: {Reported}/{Total}",
                roomCode, roomReports.Count, onlineConnIds.Count);
            return; // 还有人未上报
        }

        // 所有人都上报了，开始对比
        var allReports = onlineConnIds
            .Select(id => roomReports[id])
            .ToList();

        var diffFiles = ComputeRouteDiff(allReports);

        if (diffFiles.Count == 0)
        {
            _logger.LogInformation("房间 {Code} 路线验证通过", roomCode);
            await Clients.Group(roomCode).SendAsync("RouteVerificationPassed");
        }
        else
        {
            _logger.LogWarning("房间 {Code} 路线存在差异：{Files}", roomCode, string.Join(", ", diffFiles));
            await Clients.Group(roomCode).SendAsync("RouteDiffReceived", diffFiles);
        }

        // 清理缓存
        RouteReports.TryRemove(roomCode, out _);
    }

    /// <summary>上报到达集合点，全员到达时广播 AllArrived</summary>
    public async Task ReportArrival(string syncPointId)
    {
        var (_, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (roomCode == null) return;

        _roomManager.UpdateHeartbeat(Context.ConnectionId);
        var allArrived = _roomManager.RecordArrival(roomCode, syncPointId, Context.ConnectionId, 0);

        if (allArrived)
        {
            _logger.LogInformation("房间 {Code} 同步点 {SyncId} 全员到达", roomCode, syncPointId);
            await Clients.Group(roomCode).SendAsync("AllArrived", syncPointId);
        }
    }

    /// <summary>
    /// 上报到达集合点（带预期人数），指定人数到达时广播 AllArrived
    /// </summary>
    /// <param name="syncPointId">同步点ID</param>
    /// <param name="expectedCount">预期到达人数，0表示使用房间总人数</param>
    public async Task ReportArrivalWithExpectedCount(string syncPointId, int expectedCount)
    {
        var (_, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (roomCode == null) return;

        _roomManager.UpdateHeartbeat(Context.ConnectionId);
        var allArrived = _roomManager.RecordArrival(roomCode, syncPointId, Context.ConnectionId, expectedCount);

        if (allArrived)
        {
            _logger.LogInformation("房间 {Code} 同步点 {SyncId} 到达人数达到预期 {Expected}，触发 AllArrived", 
                roomCode, syncPointId, expectedCount);
            await Clients.Group(roomCode).SendAsync("AllArrived", syncPointId);
        }
    }

    /// <summary>上报战斗完成，全员完成时广播 AllFightDone</summary>
    public async Task ReportFightDone(string syncPointId)
    {
        var (_, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (roomCode == null) return;

        _roomManager.UpdateHeartbeat(Context.ConnectionId);
        var allDone = _roomManager.RecordFightDone(roomCode, syncPointId, Context.ConnectionId);

        if (allDone)
        {
            _logger.LogInformation("房间 {Code} 同步点 {SyncId} 全员战斗完成", roomCode, syncPointId);
            await Clients.Group(roomCode).SendAsync("AllFightDone", syncPointId);
        }
    }

    /// <summary>心跳，更新 LastHeartbeat</summary>
    public Task Heartbeat()
    {
        _roomManager.UpdateHeartbeat(Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>带路线进度信息的心跳（需求 6）</summary>
    public Task HeartbeatWithProgress(int routeIndex, DateTime routeStartTime, double routeEstimatedSeconds)
    {
        _roomManager.UpdateHeartbeatWithProgress(Context.ConnectionId, routeIndex, routeStartTime, routeEstimatedSeconds);
        return Task.CompletedTask;
    }



    /// <summary>查询指定成员的路线进度（需求 6）</summary>
    public Task<MemberProgress?> GetMemberProgress(string playerUid)
    {
        var (room, _) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null) return Task.FromResult<MemberProgress?>(null);

        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.PlayerUid == playerUid);
            if (player == null || player.CurrentRouteIndex < 0)
                return Task.FromResult<MemberProgress?>(null);

            return Task.FromResult<MemberProgress?>(new MemberProgress
            {
                RouteIndex = player.CurrentRouteIndex,
                RouteStartTime = player.RouteStartTime,
                RouteEstimatedSeconds = player.RouteEstimatedSeconds
            });
        }
    }



    /// <summary>关闭房间（仅房主可操作）</summary>
    public async Task CloseRoom()
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null)
        {
            _logger.LogWarning("[CloseRoom] 连接 {ConnId} 未在任何房间中", Context.ConnectionId);
            return;
        }

        if (room.HostConnectionId != Context.ConnectionId)
        {
            _logger.LogWarning("[CloseRoom] 连接 {ConnId} 不是房主，无法关闭房间 {Code}", Context.ConnectionId, roomCode);
            return;
        }

        _logger.LogInformation("[CloseRoom] 房主 {ConnId} 关闭房间 {Code}", Context.ConnectionId, roomCode);
        await Clients.Group(roomCode).SendAsync("RoomClosed", "房主已关闭房间");
        // 删除整个房间，防止玩家重连后重新加入已关闭的房间
        _roomManager.DeleteRoom(roomCode);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);
    }

    /// <summary>设置万叶玩家（仅房主）</summary>
    public async Task SetKazuhaPlayer(int index = 0)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        if (room.HostConnectionId != Context.ConnectionId)
        {
            _logger.LogWarning("[SetKazuhaPlayer] 连接 {ConnId} 不是房主，忽略", Context.ConnectionId);
            return;
        }

        var clampedIndex = _roomManager.SetKazuhaPlayer(roomCode, index);
        _logger.LogInformation("[SetKazuhaPlayer] 房间 {Code} 万叶玩家索引设为 {Index}", roomCode, clampedIndex);
        await Clients.Group(roomCode).SendAsync("KazuhaPlayerUpdated", clampedIndex);
    }

    /// <summary>上报路线验证完成，全员完成时广播 RouteVerificationAllDone</summary>
    public async Task ReportRouteVerificationDone()
    {
        var (_, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (roomCode == null) return;

        // 更新心跳确保玩家状态为在线
        _roomManager.UpdateHeartbeat(Context.ConnectionId);
        
        var allDone = _roomManager.RecordRouteVerificationDone(roomCode, Context.ConnectionId);

        if (allDone)
        {
            _logger.LogInformation("房间 {Code} 路线验证全员完成", roomCode);
            await Clients.Group(roomCode).SendAsync("RouteVerificationAllDone");
        }
        else
        {
            // 记录当前状态用于调试
            var (onlineCount, reportedCount) = _roomManager.GetRouteVerificationStatus(roomCode);
            _logger.LogDebug("房间 {Code} 路线验证进度: {Reported}/{Online}", roomCode, reportedCount, onlineCount);
        }
    }

    /// <summary>更新白名单（仅房主）</summary>
    public async Task UpdateWhitelist(List<string>? whitelist = null)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        if (room.HostConnectionId != Context.ConnectionId)
        {
            _logger.LogWarning("[UpdateWhitelist] 连接 {ConnId} 不是房主，忽略", Context.ConnectionId);
            return;
        }

        _roomManager.UpdateWhitelist(roomCode, whitelist ?? []);
        _logger.LogInformation("[UpdateWhitelist] 房间 {Code} 白名单已更新", roomCode);
    }

    /// <summary>获取在线房间列表</summary>
    public Task<List<RoomSummary>> GetOnlineRooms()
    {
        return Task.FromResult(_roomManager.GetOnlineRooms());
    }

    /// <summary>房主上传锄地配置</summary>
    public Task SetRoomConfig(RoomConfig config)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room != null && room.HostConnectionId == Context.ConnectionId)
        {
            room.HostConfig = config;
            _logger.LogInformation("房间 {Code} 房主配置已更新", roomCode);
        }
        return Task.CompletedTask;
    }

    /// <summary>成员拉取房主锄地配置</summary>
    public Task<RoomConfig?> GetRoomConfig()
    {
        var (room, _) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        return Task.FromResult(room?.HostConfig);
    }

    /// <summary>房主上报已进入等待状态</summary>
    public async Task ReportHostReady()
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room != null && roomCode != null && room.HostConnectionId == Context.ConnectionId)
        {
            room.HostReady = true;
            _logger.LogInformation("房间 {Code} 房主已就绪", roomCode);
            await Clients.Group(roomCode).SendAsync("HostReadyChanged", true);
        }
    }

    /// <summary>查询房主是否就绪</summary>
    public Task<bool> IsHostReady()
    {
        var (room, _) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        return Task.FromResult(room?.HostReady ?? false);
    }

    /// <summary>房主上传最终路线列表，并广播通知成员</summary>
    public async Task SetHostRouteList(List<string> routeNames)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room != null && room.HostConnectionId == Context.ConnectionId)
        {
            room.HostRouteList = routeNames;
            _logger.LogInformation("房间 {Code} 房主路线列表已上传，共 {Count} 条", roomCode, routeNames.Count);
            // 广播通知成员路线列表已就绪
            await Clients.Group(roomCode).SendAsync("HostRouteListReady", routeNames);
        }
    }

    /// <summary>成员拉取房主路线列表</summary>
    public Task<List<string>> GetHostRouteList()
    {
        var (room, _) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        return Task.FromResult(room?.HostRouteList ?? []);
    }

    /// <summary>上报已加入世界，全员加入时广播 AllWorldJoined</summary>
    public async Task ReportWorldJoined()
    {
        var (_, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (roomCode == null) return;

        var allJoined = _roomManager.RecordWorldJoined(roomCode, Context.ConnectionId);
        _logger.LogInformation("连接 {ConnId} 上报已加入世界，房间 {Code}，全员: {All}",
            Context.ConnectionId, roomCode, allJoined);

        if (allJoined)
        {
            await Clients.Group(roomCode).SendAsync("AllWorldJoined");
        }
    }

    /// <summary>获取已加入世界的人数</summary>
    public Task<int> GetWorldJoinedCount()
    {
        var (_, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (roomCode == null) return Task.FromResult(0);
        return Task.FromResult(_roomManager.GetWorldJoinedCount(roomCode));
    }

    /// <summary>重置已加入世界的记录（多世界模式新轮次开始时调用）</summary>
    public Task ResetWorldJoined()
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room != null && roomCode != null && room.HostConnectionId == Context.ConnectionId)
        {
            _roomManager.ResetWorldJoinedSet(roomCode);
            _logger.LogInformation("[ResetWorldJoined] 房间 {Code} WorldJoinedSet 已重置", roomCode);
        }
        return Task.CompletedTask;
    }



    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // 先获取断线玩家所在的房间信息（在 LeaveRoom 之前）
        var (disconnectedRoom, disconnectedRoomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        var wasHost = disconnectedRoom?.HostConnectionId == Context.ConnectionId;

        var affectedCodes = _roomManager.LeaveRoom(Context.ConnectionId);

        foreach (var code in affectedCodes)
        {
            var updatedRoom = _roomManager.GetRoom(code);
            if (updatedRoom == null) continue;

            var players = updatedRoom.Players ?? [];
            await Clients.Group(code).SendAsync("PlayerListUpdated", players);

            // 如果断线的是房主，优先广播 RoomClosed（风险 4 容错）
            if (wasHost && code == disconnectedRoomCode)
            {
                _logger.LogWarning("[OnDisconnectedAsync] 房主断线，广播 RoomClosed: 房间={RoomCode}", code);
                await Clients.Group(code).SendAsync("RoomClosed", "房主已断开连接");
                continue; // 房主断线后不做 ArrivalSets 重评估（房间即将关闭）
            }

            // 重新评估所有未完成的同步点（风险 3：在 lock 内完成检查）
            List<string> satisfiedSyncIds;
            lock (updatedRoom)
            {
                satisfiedSyncIds = updatedRoom.ArrivalSets
                    .Where(kvp => AllOnlineMembersReportedStatic(updatedRoom, kvp.Value))
                    .Select(kvp => kvp.Key)
                    .ToList();
            }

            // 广播满足条件的同步点（在 lock 外执行 await）
            foreach (var syncId in satisfiedSyncIds)
            {
                _logger.LogInformation("[OnDisconnectedAsync] 玩家断线后重新评估：同步点 {SyncId} 条件满足，广播 AllArrived，房间={RoomCode}",
                    syncId, code);
                await Clients.Group(code).SendAsync("AllArrived", syncId);
                _roomManager.ClearArrivalSet(code, syncId);
            }
        }

        _logger.LogInformation("连接 {ConnId} 断开，影响房间：{Rooms}",
            Context.ConnectionId, string.Join(", ", affectedCodes));

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 静态版本的 AllOnlineMembersReported，用于 OnDisconnectedAsync 中的重新评估。
    /// 必须在 lock(room) 内调用。
    /// </summary>
    private static bool AllOnlineMembersReportedStatic(Room room, HashSet<string> reported)
    {
        var onlinePlayers = room.Players
            .Where(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2))
            .ToList();

        if (onlinePlayers.Count == 0) return false;

        return onlinePlayers.All(p => reported.Contains(p.ConnectionId));
    }

    /// <summary>
    /// 检查所有正常（非异常）在线玩家是否都已到达。
    /// 用于异常玩家上报后的重新评估。必须在 lock(room) 内调用。
    /// </summary>
    private static bool AllNormalOnlineMembersReported(Room room, HashSet<string> reported)
    {
        var normalOnlinePlayers = room.Players
            .Where(p => !p.IsAbnormal)
            .Where(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2))
            .ToList();

        if (normalOnlinePlayers.Count == 0) return false;

        return normalOnlinePlayers.All(p => reported.Contains(p.ConnectionId));
    }

    /// <summary>
    /// 等待点上报（multiplayer-abnormal-wait-coordination 重构）
    /// 玩家跳过线路并在同步点等待时调用
    /// 服务端验证等待点格式、计算统一等待点、广播给所有正常玩家
    /// </summary>
    /// <param name="routeId">路线ID</param>
    /// <param name="syncPointId">同步点ID</param>
    /// <param name="worldRound">世界轮次</param>
    public async Task WaitPointReport(string routeId, string syncPointId, int worldRound)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null)
        {
            _logger.LogWarning("[WaitPointReport] 连接 {ConnId} 未在任何房间中，忽略等待点上报", Context.ConnectionId);
            return;
        }

        string playerUid;
        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null)
            {
                _logger.LogWarning("[WaitPointReport] 连接 {ConnId} 不在房间玩家列表中", Context.ConnectionId);
                return;
            }
            playerUid = player.PlayerUid;
            
            // 多轮世界验证：确保worldRound与房间当前轮次匹配
            if (worldRound != room.CurrentWorldRound)
            {
                _logger.LogWarning("[WaitPointReport] 等待点上报轮次不匹配：玩家{PlayerUid}上报轮次{ReportedRound}，房间轮次{RoomRound}", 
                    playerUid, worldRound, room.CurrentWorldRound);
                return; // 忽略跨轮上报
            }
        }
        
        _logger.LogInformation("[WaitPointReport] 玩家 {Uid} 上报等待点：路线={Route}，同步点={Sync}，轮次={Round}，房间={Code}", 
            playerUid, routeId, syncPointId, worldRound, roomCode);

        // 验证等待点格式（需求 2.2, 7.1 - 7.2）
        if (!ValidateWaitPointIsTeleport(syncPointId, out var validationError))
        {
            _logger.LogWarning("[WaitPointReport] 等待点验证失败: {Error}，尝试选择第一个传送点", validationError);
            // 选择该线路的第一个传送点（需求 7.2 - 7.3）
            syncPointId = GetFirstTeleportPoint(routeId);
        }

        // 计算统一等待点（需求 2.1）
        var unifiedWaitPoint = CalculateUnifiedWaitPoint(routeId, syncPointId);
        
        // 计算预期等待人数（需求 2.3）
        // 更新房间状态
        string finalUnifiedWaitPoint;
        int expectedWaitCount;
        List<string> allAbnormalPlayerUids;
        
        lock (room)
        {
            // 记录异常玩家状态（需求 1.3）
            room.AbnormalPlayerStates[playerUid] = new AbnormalPlayerState(
                playerUid, routeId, unifiedWaitPoint, worldRound
            );

            // 更新玩家异常状态
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player != null)
            {
                player.IsAbnormal = true;
                player.WaitPointId = unifiedWaitPoint;
            }

            // 存储等待点（用于记录和兼容旧逻辑）
            room.WaitPoints[playerUid] = new WaitPointReport
            {
                PlayerUid = playerUid,
                RouteId = routeId,
                SyncPointId = unifiedWaitPoint,
                WorldRound = worldRound,
                ReportedTime = DateTime.UtcNow,
                ExpiryTime = DateTime.UtcNow.AddMinutes(5) // 5分钟超时
            };

            // === 多异常玩家统一等待点计算 ===
            // 选择路线索引最大的等待点作为统一等待点，合并所有异常玩家
            finalUnifiedWaitPoint = CalculateFinalUnifiedWaitPoint(room, unifiedWaitPoint, routeId, playerUid);
            
            // 计算预期等待人数（所有在线玩家）
            expectedWaitCount = CalculateExpectedWaitCountAll(room);
            
            // 获取所有异常玩家UID列表
            allAbnormalPlayerUids = room.AbnormalPlayerStates.Keys.ToList();
            
            // 设置当前统一等待点（需求 2.1）
            room.CurrentUnifiedWaitPoint = new UnifiedWaitPoint(
                finalUnifiedWaitPoint, 
                ExtractRouteIdFromSyncPoint(finalUnifiedWaitPoint), 
                worldRound, 
                expectedWaitCount
            );
            room.CurrentUnifiedWaitPoint.AbnormalPlayerUids.Clear();
            foreach (var uid in allAbnormalPlayerUids)
            {
                room.CurrentUnifiedWaitPoint.AbnormalPlayerUids.Add(uid);
            }

            _logger.LogInformation("[WaitPointReport] 异常玩家{Uid}上报等待点，最终统一等待点={WaitPoint}，所有异常玩家=[{AbnormalPlayers}]，预期人数={Expected}",
                playerUid, finalUnifiedWaitPoint, string.Join(", ", allAbnormalPlayerUids), expectedWaitCount);
        }
        
        // 广播 UnifiedWaitPoint 给所有玩家（需求 2.3）
        // 所有玩家（异常+正常）将收到消息并在指定位置汇合
        // 注意：在 lock 外执行 await，避免死锁
        var finalRouteId = ExtractRouteIdFromSyncPoint(finalUnifiedWaitPoint);
        await Clients.Group(roomCode).SendAsync("UnifiedWaitPoint", 
            finalUnifiedWaitPoint, allAbnormalPlayerUids, expectedWaitCount, finalRouteId);
        
        _logger.LogInformation("[WaitPointReport] 已广播 UnifiedWaitPoint: 房间={RoomCode}, 等待点={WaitPoint}, 异常玩家=[{Players}], 预期人数={Expected}",
            roomCode, finalUnifiedWaitPoint, string.Join(", ", allAbnormalPlayerUids), expectedWaitCount);
    }

    /// <summary>
    /// 多轮世界重置（multiplayer-abnormal-wait-coordination 重构）
    /// 多轮世界新轮次开始时调用，清理所有等待点状态和异常状态
    /// </summary>
    public Task ResetForNewWorldRound(int newRound)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null) return Task.CompletedTask;
        
        lock (room)
        {
            room.CurrentWorldRound = newRound;
            room.WaitPoints.Clear(); // 清理所有等待点
            
            // 清理异常玩家状态（multiplayer-abnormal-wait-coordination 需求 8.5）
            room.AbnormalPlayerStates.Clear();
            room.CurrentUnifiedWaitPoint = null;
            room.WaitPointArrivals.Clear();
            
            // 清理玩家异常状态标记
            foreach (var player in room.Players)
            {
                player.IsAbnormal = false;
                player.WaitPointId = null;
            }
            
            // 清理联机锄地异常同步状态（multiplayer-abnormal-sync-server 需求 REQ-6.1）
            room.AbnormalPlayerInfos.Clear();
            
            _logger.LogInformation("[ResetForNewWorldRound] 房间{RoomCode}进入第{Round}轮，等待点、异常状态已重置", roomCode, newRound);
        }
        
        return Task.CompletedTask;
    }

    // === 等待点验证与计算方法（multiplayer-abnormal-wait-coordination 需求 2、7）===

    /// <summary>
    /// 验证等待点是否为传送点格式（需求 7.1 - 7.2）
    /// 等待点必须包含 _tp_ 标识符
    /// </summary>
    /// <param name="syncPointId">同步点ID</param>
    /// <param name="errorMessage">错误信息（验证失败时填充）</param>
    /// <returns>是否为有效的传送点格式</returns>
    private bool ValidateWaitPointIsTeleport(string syncPointId, out string errorMessage)
    {
        errorMessage = "";
        
        if (string.IsNullOrEmpty(syncPointId))
        {
            errorMessage = "等待点ID为空";
            return false;
        }
        
        // 检查是否包含 _tp_ 标识符（需求 7.1）
        if (!syncPointId.Contains("_tp_"))
        {
            errorMessage = $"等待点 {syncPointId} 不包含 _tp_ 标识符，不是有效的传送点";
            return false;
        }
        
        // 验证格式：{routeId}_tp_{listIdx}_{wpIdx} 或 {fileName}_{routeId}_tp_{listIdx}_{wpIdx}
        var parts = syncPointId.Split('_');
        var tpIndex = Array.IndexOf(parts, "tp");
        
        if (tpIndex < 0 || tpIndex >= parts.Length - 2)
        {
            errorMessage = $"等待点 {syncPointId} 格式不正确，缺少索引部分";
            return false;
        }
        
        // 验证 listIdx 和 wpIdx 是否为数字
        if (!int.TryParse(parts[tpIndex + 1], out _) || !int.TryParse(parts[tpIndex + 2], out _))
        {
            errorMessage = $"等待点 {syncPointId} 的索引部分不是有效数字";
            return false;
        }
        
        return true;
    }

    /// <summary>
    /// 获取指定路线的第一个传送点（需求 7.2 - 7.3）
    /// 优先选择 _tp_0_0 格式
    /// </summary>
    /// <param name="routeId">路线ID</param>
    /// <returns>第一个传送点ID</returns>
    private string GetFirstTeleportPoint(string routeId)
    {
        // 默认返回 _tp_0_0 格式的传送点
        return $"{routeId}_tp_0_0";
    }

    /// <summary>
    /// 计算统一等待点（需求 2.1）
    /// 规则：验证上报的等待点，如果不是传送点则回退到该线路的第一个传送点
    /// </summary>
    /// <param name="routeId">路线ID</param>
    /// <param name="reportedSyncPointId">上报的同步点ID</param>
    /// <returns>统一等待点ID</returns>
    private string CalculateUnifiedWaitPoint(string routeId, string reportedSyncPointId)
    {
        // 验证上报的等待点
        if (!ValidateWaitPointIsTeleport(reportedSyncPointId, out var errorMessage))
        {
            _logger.LogWarning("[CalculateUnifiedWaitPoint] 上报的等待点验证失败: {Error}，回退到该线路的第一个传送点", errorMessage);
            // 回退到该线路的第一个传送点
            return GetFirstTeleportPoint(routeId);
        }

        // 等待点有效，使用该点
        _logger.LogInformation("[CalculateUnifiedWaitPoint] 统一等待点: {SyncPointId}", reportedSyncPointId);
        return reportedSyncPointId;
    }

    /// <summary>
    /// 计算预期等待人数（需求 2.3）
    /// 规则：已到达该线路的正常玩家数 + 异常玩家数
    /// </summary>
    /// <param name="room">房间实例</param>
    /// <param name="abnormalPlayerUid">异常玩家UID</param>
    /// <returns>预期等待人数</returns>
    private int CalculateExpectedWaitCount(Room room, string abnormalPlayerUid)
    {
        lock (room)
        {
            int normalPlayersAtRoute = 0;
            int abnormalPlayersAtRoute = 0;

            foreach (var player in room.Players)
            {
                // 跳过离线玩家（超过2分钟无心跳）
                if (DateTime.UtcNow - player.LastHeartbeat > TimeSpan.FromMinutes(2))
                {
                    _logger.LogDebug("[CalculateExpectedWaitCount] 跳过离线玩家: {PlayerUid}", player.PlayerUid);
                    continue;
                }

                if (player.PlayerUid == abnormalPlayerUid)
                {
                    abnormalPlayersAtRoute++;
                    _logger.LogDebug("[CalculateExpectedWaitCount] 异常玩家: {PlayerUid}", player.PlayerUid);
                }
                else if (!player.IsAbnormal)
                {
                    normalPlayersAtRoute++;
                    _logger.LogDebug("[CalculateExpectedWaitCount] 正常玩家: {PlayerUid}", player.PlayerUid);
                }
            }

            int expectedCount = normalPlayersAtRoute + abnormalPlayersAtRoute;
            _logger.LogInformation("[CalculateExpectedWaitCount] 正常玩家={Normal}, 异常玩家={Abnormal}, 总计={Total}",
                normalPlayersAtRoute, abnormalPlayersAtRoute, expectedCount);

            return Math.Max(1, expectedCount);
        }
    }

    /// <summary>
    /// 到达等待点上报（multiplayer-abnormal-wait-coordination 需求 5）
    /// 正常玩家到达统一等待点时调用，服务端记录到达状态并在全员到达时广播
    /// </summary>
    /// <param name="syncPointId">同步点ID</param>
    public async Task ReportArrivalAtWaitPoint(string syncPointId)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null)
        {
            _logger.LogWarning("[ReportArrivalAtWaitPoint] 连接 {ConnId} 未在任何房间中，忽略到达上报", Context.ConnectionId);
            return;
        }

        string playerUid;
        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null)
            {
                _logger.LogWarning("[ReportArrivalAtWaitPoint] 连接 {ConnId} 不在房间玩家列表中", Context.ConnectionId);
                return;
            }
            playerUid = player.PlayerUid;
            
            // 记录到达状态
            _roomManager.RecordWaitPointArrival(roomCode, syncPointId, playerUid, player.IsAbnormal);
        }
        
        _logger.LogInformation("[ReportArrivalAtWaitPoint] 玩家 {Uid} 到达等待点 {SyncPointId}，房间 {RoomCode}",
            playerUid, syncPointId, roomCode);

        // 检查是否全员到达
        var allArrived = _roomManager.CheckAllWaitPointArrived(roomCode, syncPointId);
        
        if (allArrived)
        {
            _logger.LogInformation("[ReportArrivalAtWaitPoint] 全员到达等待点 {SyncPointId}，房间 {RoomCode}",
                syncPointId, roomCode);
            
            // 清除异常状态（需求 5.4）
            lock (room)
            {
                var unifiedWaitPoint = room.CurrentUnifiedWaitPoint;
                if (unifiedWaitPoint != null && unifiedWaitPoint.SyncPointId == syncPointId)
                {
                    foreach (var uid in unifiedWaitPoint.AbnormalPlayerUids)
                    {
                        if (room.AbnormalPlayerStates.TryGetValue(uid, out var state))
                        {
                            state.MarkAsRecovered();
                            _logger.LogInformation("[ReportArrivalAtWaitPoint] 异常玩家 {Uid} 已恢复正常", uid);
                        }
                        
                        // 更新玩家状态
                        var abnormalPlayer = room.Players.FirstOrDefault(p => p.PlayerUid == uid);
                        if (abnormalPlayer != null)
                        {
                            abnormalPlayer.IsAbnormal = false;
                            abnormalPlayer.WaitPointId = null;
                        }
                    }
                    
                    // 清除当前统一等待点
                    room.CurrentUnifiedWaitPoint = null;
                }
            }
            
            // 清除等待点到达记录，防止后续轮次数据污染
            _roomManager.ClearWaitPointArrivals(roomCode);
            
            // 广播 AllPlayersArrived（需求 5.4）
            await Clients.Group(roomCode).SendAsync("AllPlayersArrived", syncPointId);
            _logger.LogInformation("[ReportArrivalAtWaitPoint] 已广播 AllPlayersArrived: 房间={RoomCode}, 等待点={SyncPointId}",
                roomCode, syncPointId);
        }
        else
        {
            // 记录当前进度
            var (arrived, expected) = _roomManager.GetWaitPointArrivalStatus(roomCode, syncPointId);
            _logger.LogDebug("[ReportArrivalAtWaitPoint] 等待点 {SyncPointId} 到达进度: {Arrived}/{Expected}",
                syncPointId, arrived, expected);
        }
    }

    /// <summary>
    /// 清除异常状态（需求 5.3, 5.5）
    /// 异常玩家恢复正常后调用，服务端更新状态并广播
    /// </summary>
    public async Task ClearAbnormalStatus()
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null)
        {
            _logger.LogWarning("[ClearAbnormalStatus] 连接 {ConnId} 未在任何房间中，忽略状态清除", Context.ConnectionId);
            return;
        }

        string playerUid;
        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null)
            {
                _logger.LogWarning("[ClearAbnormalStatus] 连接 {ConnId} 不在房间玩家列表中", Context.ConnectionId);
                return;
            }
            playerUid = player.PlayerUid;
            
            // 清除异常状态
            if (room.AbnormalPlayerStates.TryGetValue(playerUid, out var state))
            {
                state.MarkAsRecovered();
                _logger.LogInformation("[ClearAbnormalStatus] 异常玩家 {Uid} 的状态已标记为恢复", playerUid);
            }
            
            // 更新玩家信息
            player.IsAbnormal = false;
            player.WaitPointId = null;
        }
        
        _logger.LogInformation("[ClearAbnormalStatus] 异常玩家 {Uid} 已恢复正常", playerUid);
        
        // 广播 AbnormalPlayerRecovered（需求 5.3）
        await Clients.Group(roomCode).SendAsync("AbnormalPlayerRecovered", playerUid);
        _logger.LogInformation("[ClearAbnormalStatus] 已广播 AbnormalPlayerRecovered: 房间={RoomCode}, 玩家={PlayerUid}",
            roomCode, playerUid);
    }

    /// <summary>
    /// 接收玩家异常通知并广播给房间内其他玩家（multiplayer-abnormal-sync-server spec）
    /// Validates: Requirements REQ-1.1, REQ-1.2, REQ-1.3, REQ-1.4, REQ-3.2, REQ-3.3
    /// </summary>
    public async Task PlayerAnomalyNotify(string playerUid, int routeIndex, bool passedSyncPoint)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        // 计算目标汇合线路（需求 REQ-1.3, REQ-1.4）
        int targetRouteIndex = passedSyncPoint ? routeIndex + 1 : routeIndex;

        _logger.LogInformation(
            "[PlayerAnomalyNotify] 房间={RoomCode}, 玩家={PlayerUid}, 线路={RouteIndex}, 已过同步点={Passed}, 目标汇合线路={Target}",
            roomCode, playerUid, routeIndex, passedSyncPoint, targetRouteIndex);

        // 更新服务器端异常状态（需求 REQ-3.2, REQ-3.3）
        lock (room)
        {
            room.AbnormalPlayerInfos[playerUid] = new AbnormalPlayerInfo
            {
                PlayerUid = playerUid,
                RouteIndex = routeIndex,
                PassedSyncPoint = passedSyncPoint,
                TargetRouteIndex = targetRouteIndex,
                ReportTime = DateTime.UtcNow
            };
        }

        // 广播给房间内所有玩家（发送方也会收到，但客户端会过滤自己）（需求 REQ-1.2）
        await Clients.Group(roomCode).SendAsync("PlayerAnomalyNotify", playerUid, routeIndex, passedSyncPoint);
    }

    /// <summary>
    /// 接收玩家异常恢复通知并广播给房间内其他玩家（multiplayer-abnormal-sync-server spec）
    /// Validates: Requirements REQ-2.1, REQ-2.2, REQ-3.4
    /// </summary>
    public async Task PlayerAnomalyRecovered(string playerUid)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        _logger.LogInformation("[PlayerAnomalyRecovered] 房间={RoomCode}, 玩家={PlayerUid}", roomCode, playerUid);

        // 从服务器端异常状态中移除（需求 REQ-3.4）
        lock (room)
        {
            room.AbnormalPlayerInfos.Remove(playerUid);
        }

        // 广播给房间内所有玩家（需求 REQ-2.2）
        await Clients.Group(roomCode).SendAsync("PlayerAnomalyRecovered", playerUid);
    }

    /// <summary>
    /// 更新成员状态。
    /// 当玩家上报 Reviving/Rejoining 时，标记为异常并重新评估 ArrivalSets；
    /// 当玩家上报 Normal 时，清除异常标记。
    /// targetProgress：异常玩家的目标进度值，用于判定其他玩家在某同步点是否需要等他。
    /// </summary>
    public async Task MemberStatusChanged(string playerUid, string status, long targetProgress = -1)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        bool isAbnormalReport = status == "Reviving" || status == "Rejoining";
        bool isNormalReport = status == "Normal";

        // 收集每个同步点的进度值（用于判定）
        // syncId → progress 映射需要从客户端推断，这里用 ArrivalSet 中第一个玩家的 CurrentProgress 作为参考
        // 但更安全的做法是：对每个同步点，用 ShouldBroadcastAllArrived 重新判定
        var satisfiedSyncs = new List<(string syncId, long progress)>();

        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null) return;

            if (Enum.TryParse<PlayerStatus>(status, out var parsedStatus))
            {
                player.Status = parsedStatus;
            }

            if (isAbnormalReport)
            {
                player.IsAbnormal = true;
                player.TargetProgress = targetProgress;
                _logger.LogInformation("[MemberStatusChanged] 玩家={PlayerUid} 上报异常={Status}，目标进度={Target}",
                    playerUid, status, targetProgress);

                // 重新评估所有未完成的同步点
                // 用每个同步点中已到达玩家的最大 CurrentProgress 作为 syncProgress
                _logger.LogInformation("[MemberStatusChanged] 开始重评估，房间 ArrivalSets 数量: {N}", room.ArrivalSets.Count);
                foreach (var kvp in room.ArrivalSets)
                {
                    var syncId = kvp.Key;
                    var arrivals = kvp.Value;

                    // route_sync_done 是全局同步点，使用 -1（按"等所有"处理）
                    long syncProgress = -1;
                    if (syncId != "route_sync_done")
                    {
                        // 用已到达玩家的最大 CurrentProgress
                        syncProgress = room.Players
                            .Where(p => arrivals.Contains(p.ConnectionId))
                            .Select(p => p.CurrentProgress)
                            .DefaultIfEmpty(-1)
                            .Max();
                    }

                    _logger.LogInformation("[MemberStatusChanged] 评估同步点 {SyncId}, syncProgress={SP}, ArrivalSet={Arr}",
                        syncId, syncProgress, string.Join(",", arrivals));

                    if (ShouldBroadcastAllArrived(room, syncId, arrivals, syncProgress))
                    {
                        _logger.LogWarning("[MemberStatusChanged] 同步点 {SyncId} 满足放行条件！", syncId);
                        satisfiedSyncs.Add((syncId, syncProgress));
                    }
                }
            }
            else if (isNormalReport)
            {
                player.IsAbnormal = false;
                player.TargetProgress = -1;
                _logger.LogInformation("[MemberStatusChanged] 玩家={PlayerUid} 恢复正常状态", playerUid);
            }
            else
            {
                _logger.LogDebug("[MemberStatusChanged] 玩家={PlayerUid}, 状态={Status}", playerUid, status);
            }
        }

        // 广播满足条件的同步点（在 lock 外执行 await）
        foreach (var (syncId, progress) in satisfiedSyncs)
        {
            _logger.LogInformation("[MemberStatusChanged] 异常上报后重评估：同步点 {SyncId} 满足条件，广播 AllArrived（房间={RoomCode}, 进度={Progress}）",
                syncId, roomCode, progress);
            await Clients.Group(roomCode).SendAsync("AllArrived", syncId);
            _roomManager.ClearArrivalSet(roomCode, syncId);
        }
    }

    /// <summary>
    /// 记录路线跳过
    /// </summary>
    public Task RouteSkipped(string playerUid, int routeIndex)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return Task.CompletedTask;

        _logger.LogInformation("[RouteSkipped] 房间={RoomCode}, 玩家={PlayerUid}, 路线={RouteIndex}",
            roomCode, playerUid, routeIndex);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 记录等待点到达
    /// </summary>
    public Task WaitPointReached(string playerUid, string syncPointId)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return Task.CompletedTask;

        _logger.LogDebug("[WaitPointReached] 房间={RoomCode}, 玩家={PlayerUid}, 同步点={SyncPointId}",
            roomCode, playerUid, syncPointId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新战斗状态
    /// </summary>
    public Task FightingStatusChanged(string playerUid, bool isFighting)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return Task.CompletedTask;

        _logger.LogDebug("[FightingStatusChanged] 玩家={PlayerUid}, 战斗中={IsFighting}", playerUid, isFighting);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 等待所有玩家到达指定同步点（非阻塞模式：记录到达 → 检查条件 → 广播 → 立即返回）
    /// 客户端通过本地 TCS + AllArrived 事件等待，服务端不阻塞 SignalR 连接。
    /// 
    /// 判定规则（基于全局进度值）：
    ///   对每个异常玩家 P：
    ///     P.TargetProgress == syncProgress → P 正要去 X → 等他
    ///     P.TargetProgress != syncProgress → P 跳过了 X 或不会到 X → 不等他
    /// </summary>
    public async Task WaitForAllPlayers(string syncId, long syncProgress = -1)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        _logger.LogDebug("[WaitForAllPlayers] 房间={RoomCode}, 同步点={SyncId}, 进度={Progress}, 连接={ConnId}",
            roomCode, syncId, syncProgress, Context.ConnectionId);

        // 更新当前玩家的进度
        if (syncProgress >= 0)
        {
            lock (room)
            {
                var caller = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (caller != null)
                {
                    caller.CurrentProgress = syncProgress;
                }
            }
        }

        // 记录当前连接已到达
        _roomManager.RecordArrival(roomCode, syncId, Context.ConnectionId, 0);

        // 判定：检查需要等待的玩家是否都已到达
        bool shouldBroadcast;
        bool isAbnormalRecovered = false;
        lock (room)
        {
            var arrivals = room.ArrivalSets.TryGetValue(syncId, out var set) ? set : new HashSet<string>();
            shouldBroadcast = ShouldBroadcastAllArrived(room, syncId, arrivals, syncProgress);

            // 检查是否是异常玩家在汇合点恢复（异常玩家到达且条件满足，说明汇合了）
            var caller = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (shouldBroadcast && caller != null && caller.IsAbnormal && syncProgress >= 0
                && caller.TargetProgress > 0 && caller.TargetProgress <= syncProgress)
            {
                isAbnormalRecovered = true;
            }
        }

        if (shouldBroadcast)
        {
            _logger.LogInformation("[WaitForAllPlayers] 所需玩家已到达，广播 AllArrived: 房间={RoomCode}, 同步点={SyncId}", roomCode, syncId);
            await Clients.Group(roomCode).SendAsync("AllArrived", syncId);
            _roomManager.ClearArrivalSet(roomCode, syncId);

            // 汇合后清理异常状态：所有 TargetProgress ≤ syncProgress 的异常玩家恢复正常
            // （他们已经到达汇合点或更前面）
            lock (room)
            {
                foreach (var p in room.Players)
                {
                    if (p.IsAbnormal && p.TargetProgress > 0 && syncProgress >= 0 && p.TargetProgress <= syncProgress)
                    {
                        _logger.LogInformation("[WaitForAllPlayers] 异常玩家 {Uid} 已汇合，清除异常状态", p.PlayerUid);
                        p.IsAbnormal = false;
                        p.TargetProgress = -1;
                    }
                }
            }

            // route_sync_done 完成后重置所有玩家的异常状态（防止跨 JSON 残留）
            if (syncId == "route_sync_done")
            {
                lock (room)
                {
                    foreach (var p in room.Players)
                    {
                        p.IsAbnormal = false;
                        p.TargetProgress = -1;
                    }
                }
                _logger.LogDebug("[WaitForAllPlayers] route_sync_done 完成，已重置所有玩家异常状态");
            }
        }
        // 非阻塞：不满足条件时直接返回，客户端通过 AllArrived 事件等待
    }

    /// <summary>
    /// 判定同步点 X 是否应该广播 AllArrived。
    /// 必须在 lock(room) 内调用。
    /// 
    /// 规则：
    ///   X.Progress = syncProgress（当前同步点的全局进度值）
    ///   对每个异常玩家 P：
    ///     P.TargetProgress > syncProgress → P 会到 X → 计入等待
    ///     P.TargetProgress ≤ syncProgress → P 跳过了 X → 不计入
    ///   所有计入的玩家都到达 → 放行
    /// </summary>
    private bool ShouldBroadcastAllArrived(Room room, string syncId, HashSet<string> arrivals, long syncProgress)
    {
        var onlinePlayers = room.Players
            .Where(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2))
            .ToList();

        _logger.LogInformation("[ShouldBroadcast] syncId={SyncId}, syncProgress={SP}, 在线玩家数={Online}, ArrivalSet={Arrivals}",
            syncId, syncProgress, onlinePlayers.Count, string.Join(",", arrivals));

        if (onlinePlayers.Count == 0)
        {
            _logger.LogInformation("[ShouldBroadcast] 无在线玩家，不广播");
            return false;
        }

        foreach (var p in onlinePlayers)
        {
            _logger.LogInformation("[ShouldBroadcast]   玩家={Uid}, ConnId={CID}, IsAbnormal={Abn}, Target={T}, Current={C}, Arrived={Arr}",
                p.PlayerUid, p.ConnectionId, p.IsAbnormal, p.TargetProgress, p.CurrentProgress, arrivals.Contains(p.ConnectionId));
        }

        // 计算需要等待的玩家
        var requiredPlayers = onlinePlayers.Where(p =>
        {
            if (!p.IsAbnormal) return true;
            if (syncProgress < 0) return true;
            return p.TargetProgress == syncProgress;
        }).ToList();

        _logger.LogInformation("[ShouldBroadcast] 需要等待的玩家: {List}",
            string.Join(",", requiredPlayers.Select(p => $"{p.PlayerUid}(Abn={p.IsAbnormal},T={p.TargetProgress})")));

        if (requiredPlayers.Count == 0)
        {
            _logger.LogInformation("[ShouldBroadcast] 无需要等待的玩家，不广播");
            return false;
        }

        var allArrived = requiredPlayers.All(p => arrivals.Contains(p.ConnectionId));
        _logger.LogInformation("[ShouldBroadcast] 全部到达？{Result}", allArrived);
        return allArrived;
    }

    /// <summary>
    /// 静态版本（兼容旧调用），无日志
    /// </summary>
    private static bool ShouldBroadcastAllArrived(Room room, HashSet<string> arrivals, long syncProgress)
    {
        var onlinePlayers = room.Players
            .Where(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2))
            .ToList();

        if (onlinePlayers.Count == 0) return false;

        var requiredPlayers = onlinePlayers.Where(p =>
        {
            if (!p.IsAbnormal) return true;
            if (syncProgress < 0) return true;
            return p.TargetProgress == syncProgress;
        }).ToList();

        if (requiredPlayers.Count == 0) return false;

        return requiredPlayers.All(p => arrivals.Contains(p.ConnectionId));
    }

    /// <summary>计算多份路线清单的差异文件名列表</summary>
    private static List<string> ComputeRouteDiff(List<List<RouteHash>> allReports)
    {
        if (allReports.Count == 0) return [];

        // 以第一份为基准，找出 MD5 不一致或缺失的文件
        var baseline = allReports[0].ToDictionary(r => r.FileName, r => r.Md5);
        var diffFiles = new HashSet<string>();

        // 收集所有文件名
        var allFileNames = allReports
            .SelectMany(r => r.Select(h => h.FileName))
            .ToHashSet();

        foreach (var fileName in allFileNames)
        {
            var md5Values = allReports
                .Select(r => r.FirstOrDefault(h => h.FileName == fileName)?.Md5)
                .ToList();

            // 有任何一份缺失或 MD5 不同则标记为差异
            if (md5Values.Any(m => m == null) || md5Values.Distinct().Count() > 1)
                diffFiles.Add(fileName);
        }

        return [.. diffFiles];
    }

    // === 缺少的辅助方法（暂时添加存根以修复编译错误）===
    // TODO: 这些方法应该在 multiplayer-sync-refactor 清理计划中删除或正确实现

    /// <summary>
    /// 计算最终统一等待点（多异常玩家场景）
    /// </summary>
    private string CalculateFinalUnifiedWaitPoint(Room room, string currentWaitPoint, string routeId, string playerUid)
    {
        // 简单实现：返回当前等待点
        // 完整实现应根据路线索引选择最远的等待点
        return currentWaitPoint;
    }

    /// <summary>
    /// 计算预期等待人数（所有在线玩家）
    /// </summary>
    private int CalculateExpectedWaitCountAll(Room room)
    {
        lock (room)
        {
            return room.Players.Count(p =>
                DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2));
        }
    }

    /// <summary>
    /// 从同步点ID中提取路线ID
    /// </summary>
    private string ExtractRouteIdFromSyncPoint(string syncPointId)
    {
        if (string.IsNullOrEmpty(syncPointId))
            return "";

        // 格式：{routeId}_tp_{listIdx}_{wpIdx} 或 {fileName}_{routeId}_tp_{listIdx}_{wpIdx}
        var parts = syncPointId.Split('_');
        var tpIndex = Array.IndexOf(parts, "tp");

        if (tpIndex > 0)
        {
            // 路线ID在 _tp_ 之前
            return string.Join("_", parts.Take(tpIndex));
        }

        return syncPointId;
    }
}
