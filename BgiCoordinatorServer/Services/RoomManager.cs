using System.Collections.Concurrent;
using System.Linq;
using BgiCoordinatorServer.Models;
using Microsoft.Extensions.Logging;

namespace BgiCoordinatorServer.Services;

public class RoomManager
{
    private const string CodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int CodeLength = 6;
    private const int MaxPlayers = 4;

    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    // connectionId → roomCode 反向索引，加速查找
    private readonly ConcurrentDictionary<string, string> _connectionRoomMap = new();
    private readonly ConcurrentDictionary<string, List<PlayerInfo>> _lastRemovedPlayers = new();
    private readonly int _maxRooms;
    private readonly ILogger<RoomManager>? _logger;

    public RoomManager(int maxRooms = 50, ILogger<RoomManager>? logger = null)
    {
        _maxRooms = maxRooms;
        _logger = logger;
    }

    /// <summary>创建房间，返回唯一6位字母数字房间码。同一 UID 只保留最新房间。</summary>
    public string CreateRoom(string hostConnectionId, string playerName = "", List<string>? whitelist = null, string playerUid = "", int expectedPlayerCount = 4)
    {
        if (_rooms.Count >= _maxRooms)
            throw new InvalidOperationException("服务器房间数已达上限");

        // 同一 UID 只保留最新房间，关闭旧房间
        if (!string.IsNullOrEmpty(playerUid))
        {
            var oldRoomCodes = _rooms
                .Where(kv => kv.Value.Players.Count > 0
                    && kv.Value.Players[0].PlayerUid == playerUid
                    && kv.Value.HostConnectionId != hostConnectionId)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var oldCode in oldRoomCodes)
            {
                if (_rooms.TryRemove(oldCode, out var oldRoom))
                {
                    lock (oldRoom)
                    {
                        foreach (var p in oldRoom.Players)
                            _connectionRoomMap.TryRemove(p.ConnectionId, out _);
                    }
                }
            }
        }

        string code;
        do
        {
            code = GenerateCode();
        } while (!_rooms.TryAdd(code, new Room
        {
            Code = code,
            HostConnectionId = hostConnectionId,
            CreatedAt = DateTime.UtcNow,
            Whitelist = whitelist ?? [],
            ExpectedPlayerCount = expectedPlayerCount,
            Players =
            [
                new PlayerInfo
                {
                    ConnectionId = hostConnectionId,
                    PlayerId = hostConnectionId,
                    PlayerName = string.IsNullOrEmpty(playerName) ? "房主" : playerName,
                    PlayerUid = playerUid,
                    Status = PlayerStatus.Waiting,
                    LastHeartbeat = DateTime.UtcNow
                }
            ]
        }));

        _connectionRoomMap[hostConnectionId] = code;
        return code;
    }

    /// <summary>加入房间，验证房间存在且人数 &lt; 4</summary>
    public (bool Success, string? Error) JoinRoom(string roomCode, string connectionId, string playerId, string playerName = "", string playerUid = "")
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return (false, "房间不存在");

        lock (room)
        {
            // 白名单检查（按玩家名称），房主自己（同 UID 或同名）跳过检查
            var isHost = room.Players.Count > 0 &&
                ((!string.IsNullOrEmpty(playerUid) && room.Players[0].PlayerUid == playerUid) ||
                 (!string.IsNullOrEmpty(playerName) && room.Players[0].PlayerName == playerName));
            if (!isHost && room.Whitelist.Count > 0 && !room.Whitelist.Contains(playerName))
                return (false, "不在白名单中");

            if (room.Players.Count >= MaxPlayers)
            {
                // Allow replacement if same playerName already exists
                var existing = room.Players.FirstOrDefault(p => p.PlayerName == playerName && !string.IsNullOrEmpty(playerName));
                if (existing == null)
                    return (false, "房间已满（最多4人）");

                // Replace old connection with new one
                _connectionRoomMap.TryRemove(existing.ConnectionId, out _);
                foreach (var set in room.ArrivalSets.Values)
                {
                    if (set.Remove(existing.ConnectionId))
                        set.Add(connectionId);
                }
                foreach (var set in room.FightDoneSets.Values)
                {
                    if (set.Remove(existing.ConnectionId))
                        set.Add(connectionId);
                }
                // 如果被替换的是房主，同步更新 HostConnectionId
                if (room.HostConnectionId == existing.ConnectionId)
                    room.HostConnectionId = connectionId;
                room.Players.Remove(existing);
            }

            // Replace existing player with same name (reconnect scenario)
            var existingByName = room.Players.FirstOrDefault(p => p.PlayerName == playerName && !string.IsNullOrEmpty(playerName));
            if (existingByName != null)
            {
                _connectionRoomMap.TryRemove(existingByName.ConnectionId, out _);
                foreach (var set in room.ArrivalSets.Values)
                {
                    if (set.Remove(existingByName.ConnectionId))
                        set.Add(connectionId);
                }
                foreach (var set in room.FightDoneSets.Values)
                {
                    if (set.Remove(existingByName.ConnectionId))
                        set.Add(connectionId);
                }
                // 如果被替换的是房主，同步更新 HostConnectionId
                if (room.HostConnectionId == existingByName.ConnectionId)
                    room.HostConnectionId = connectionId;
                room.Players.Remove(existingByName);
            }

            if (room.Players.Any(p => p.ConnectionId == connectionId))
                return (false, "已在房间中");

            room.Players.Add(new PlayerInfo
            {
                ConnectionId = connectionId,
                PlayerId = playerId,
                PlayerName = string.IsNullOrEmpty(playerName) ? $"玩家{room.Players.Count + 1}" : playerName,
                PlayerUid = playerUid,
                Status = PlayerStatus.Waiting,
                LastHeartbeat = DateTime.UtcNow
            });
        }

        _connectionRoomMap[connectionId] = roomCode;
        return (true, null);
    }

    /// <summary>从所有房间移除该连接，返回受影响的房间码列表</summary>
    public List<string> LeaveRoom(string connectionId)
    {
        var affected = new List<string>();

        if (_connectionRoomMap.TryRemove(connectionId, out var roomCode))
        {
            if (_rooms.TryGetValue(roomCode, out var room))
            {
                lock (room)
                {
                    room.Players.RemoveAll(p => p.ConnectionId == connectionId);
                    // 清理该连接在各同步集合中的记录
                    foreach (var set in room.ArrivalSets.Values)
                        set.Remove(connectionId);
                    foreach (var set in room.FightDoneSets.Values)
                        set.Remove(connectionId);
                    room.WorldJoinedSet.Remove(connectionId);
                    room.RouteVerificationDoneSet.Remove(connectionId);

                    // 房间空了则删除
                    if (room.Players.Count == 0)
                        _rooms.TryRemove(roomCode, out _);
                }
                affected.Add(roomCode);
            }
        }

        return affected;
    }

    /// <summary>记录到达，当房间内所有在线成员均到达时返回 true</summary>
    public bool RecordArrival(string roomCode, string syncPointId, string connectionId)
    {
        return RecordArrival(roomCode, syncPointId, connectionId, 0);
    }

    /// <summary>
    /// 记录到达，当指定数量的玩家到达时返回 true
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <param name="syncPointId">同步点ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <param name="expectedCount">预期到达人数，0表示使用房间总人数</param>
    /// <returns>是否已达到预期人数</returns>
    public bool RecordArrival(string roomCode, string syncPointId, string connectionId, int expectedCount)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return false;

        lock (room)
        {
            if (!room.ArrivalSets.TryGetValue(syncPointId, out var arrivals))
            {
                arrivals = [];
                room.ArrivalSets[syncPointId] = arrivals;
            }

            arrivals.Add(connectionId);
            
            // 如果指定了预期人数，使用指定人数判断
            if (expectedCount > 0)
            {
                return arrivals.Count >= expectedCount;
            }
            
            // 否则使用原有的"所有在线成员"判断
            return AllOnlineMembersReported(room, arrivals);
        }
    }

    /// <summary>
    /// 清理指定同步点的到达集合（用于新一轮开始时清除旧数据）
    /// </summary>
    public void ClearArrivalSet(string roomCode, string syncPointId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return;

        lock (room)
        {
            room.ArrivalSets.Remove(syncPointId);
        }
    }

    /// <summary>记录战斗完成，当房间内所有在线成员均完成时返回 true</summary>
    public bool RecordFightDone(string roomCode, string syncPointId, string connectionId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return false;

        lock (room)
        {
            if (!room.FightDoneSets.TryGetValue(syncPointId, out var doneSet))
            {
                doneSet = [];
                room.FightDoneSets[syncPointId] = doneSet;
            }

            doneSet.Add(connectionId);
            return AllOnlineMembersReported(room, doneSet);
        }
    }

    /// <summary>记录路线验证完成，当房间内所有在线成员均完成时返回 true</summary>
    public bool RecordRouteVerificationDone(string roomCode, string connectionId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return false;

        lock (room)
        {
            room.RouteVerificationDoneSet.Add(connectionId);
            
            // 清理已离线玩家的验证记录
            var onlineConnectionIds = room.Players
                .Where(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2))
                .Select(p => p.ConnectionId)
                .ToHashSet();
                
            room.RouteVerificationDoneSet.IntersectWith(onlineConnectionIds);
            
            return AllOnlineMembersReported(room, room.RouteVerificationDoneSet);
        }
    }

    public (int OnlineCount, int ReportedCount) GetRouteVerificationStatus(string roomCode)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return (0, 0);

        lock (room)
        {
            var onlineCount = room.Players
                .Count(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2));
            var reportedCount = room.RouteVerificationDoneSet.Count;
            return (onlineCount, reportedCount);
        }
    }

    /// <summary>记录已加入世界，当所有在线成员均加入时返回 true</summary>
    public bool RecordWorldJoined(string roomCode, string connectionId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return false;

        lock (room)
        {
            room.WorldJoinedSet.Add(connectionId);
            return AllOnlineMembersReported(room, room.WorldJoinedSet);
        }
    }

    public void ResetWorldJoinedSet(string roomCode)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return;
        lock (room) { room.WorldJoinedSet.Clear(); }
    }

    public int GetWorldJoinedCount(string roomCode)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return 0;
        lock (room)
        {
            return room.WorldJoinedSet.Count;
        }
    }

    /// <summary>设置万叶玩家索引，clamp 到 0~Players.Count</summary>
    public int SetKazuhaPlayer(string roomCode, int index)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return 0;

        lock (room)
        {
            room.KazuhaPlayerIndex = Math.Clamp(index, 0, room.Players.Count);
            return room.KazuhaPlayerIndex;
        }
    }

    /// <summary>更新房间白名单</summary>
    public void UpdateWhitelist(string roomCode, List<string> whitelist)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return;

        lock (room)
        {
            room.Whitelist = whitelist;
        }
    }

    /// <summary>获取所有未满的在线房间摘要</summary>
    public List<RoomSummary> GetOnlineRooms()
    {
        var result = new List<RoomSummary>();
        foreach (var (code, room) in _rooms)
        {
            lock (room)
            {
                if (room.Players.Count < MaxPlayers)
                {
                    result.Add(new RoomSummary
                    {
                        Code = code,
                        HostName = room.Players.Count > 0 ? room.Players[0].PlayerName : "",
                        HostUid = room.Players.Count > 0 ? room.Players[0].PlayerUid : "",
                        PlayerCount = room.Players.Count,
                        ExpectedPlayerCount = room.ExpectedPlayerCount,
                        MaxPlayers = MaxPlayers
                    });
                }
            }
        }
        return result;
    }

    public Room? GetRoom(string roomCode)
    {
        _rooms.TryGetValue(roomCode, out var room);
        return room;
    }

    /// <summary>删除整个房间及其所有玩家的映射（只删除仍在该房间的玩家映射）</summary>
    public void DeleteRoom(string roomCode)
    {
        if (!_rooms.TryRemove(roomCode, out var room))
            return;
        lock (room)
        {
            foreach (var p in room.Players)
            {
                // 只删除映射值仍指向该房间的条目，避免误删已加入新房间的玩家映射
                if (_connectionRoomMap.TryGetValue(p.ConnectionId, out var mappedRoom) && mappedRoom == roomCode)
                    _connectionRoomMap.TryRemove(p.ConnectionId, out _);
            }
        }
    }

    public (Room? Room, string? RoomCode) GetRoomByConnectionId(string connectionId)
    {
        if (_connectionRoomMap.TryGetValue(connectionId, out var roomCode)
            && _rooms.TryGetValue(roomCode, out var room))
            return (room, roomCode);

        return (null, null);
    }

    public void UpdateHeartbeat(string connectionId)
    {
        if (_connectionRoomMap.TryGetValue(connectionId, out var roomCode)
            && _rooms.TryGetValue(roomCode, out var room))
        {
            lock (room)
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player != null)
                    player.LastHeartbeat = DateTime.UtcNow;
            }
        }
    }

    /// <summary>带路线进度信息的心跳更新（需求 6）</summary>
    public void UpdateHeartbeatWithProgress(string connectionId, int routeIndex, DateTime routeStartTime, double routeEstimatedSeconds)
    {
        if (_connectionRoomMap.TryGetValue(connectionId, out var roomCode)
            && _rooms.TryGetValue(roomCode, out var room))
        {
            lock (room)
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player != null)
                {
                    player.LastHeartbeat = DateTime.UtcNow;
                    player.CurrentRouteIndex = routeIndex;
                    player.RouteStartTime = routeStartTime;
                    player.RouteEstimatedSeconds = routeEstimatedSeconds;
                }
            }
        }
    }

    /// <summary>
    /// 带完整状态的心跳更新（multiplayer-abnormal-wait-coordination 需求 1.2）
    /// 更新玩家心跳时间、路线索引、异常状态和等待点信息
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="routeIndex">当前路线索引</param>
    /// <param name="isAbnormal">是否为异常玩家</param>
    /// <param name="waitPointId">当前等待点ID（异常玩家专用）</param>
    public void UpdateHeartbeatWithState(string connectionId, int routeIndex, bool isAbnormal, string? waitPointId)
    {
        if (_connectionRoomMap.TryGetValue(connectionId, out var roomCode)
            && _rooms.TryGetValue(roomCode, out var room))
        {
            lock (room)
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
                if (player != null)
                {
                    player.LastHeartbeat = DateTime.UtcNow;
                    player.CurrentRouteIndex = routeIndex;
                    player.IsAbnormal = isAbnormal;
                    player.WaitPointId = waitPointId;
                }
            }
        }
    }

    /// <summary>
    /// 记录等待点到达（multiplayer-abnormal-wait-coordination 需求 5.2）
    /// 当所有预期玩家都到达时返回 true
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <param name="syncPointId">同步点ID</param>
    /// <param name="playerUid">玩家UID</param>
    /// <param name="isAbnormal">是否为异常玩家</param>
    /// <returns>是否全员已到达</returns>
    public bool RecordWaitPointArrival(string roomCode, string syncPointId, string playerUid, bool isAbnormal)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            _logger?.LogWarning("[RecordWaitPointArrival] 房间 {RoomCode} 不存在", roomCode);
            return false;
        }

        lock (room)
        {
            // 获取或创建到达记录
            if (!room.WaitPointArrivals.TryGetValue(syncPointId, out var arrivals))
            {
                arrivals = new HashSet<string>();
                room.WaitPointArrivals[syncPointId] = arrivals;
            }
            
            arrivals.Add(playerUid);
            
            // 检查是否全员到达
            var unifiedWaitPoint = room.CurrentUnifiedWaitPoint;
            if (unifiedWaitPoint == null || unifiedWaitPoint.SyncPointId != syncPointId)
            {
                _logger?.LogDebug("[RecordWaitPointArrival] 无匹配的统一等待点或等待点ID不匹配: {SyncPointId}", syncPointId);
                return false;
            }
            
            // 预期人数 = 服务端计算的人数
            int expectedCount = unifiedWaitPoint.ExpectedWaitCount;
            
            _logger?.LogInformation("[RecordWaitPointArrival] 等待点 {SyncPointId} 到达人数: {Arrived}/{Expected}",
                syncPointId, arrivals.Count, expectedCount);
            
            return arrivals.Count >= expectedCount;
        }
    }

    /// <summary>
    /// 获取等待点到达状态（multiplayer-abnormal-wait-coordination 需求 5.2）
    /// 返回已到达人数和预期人数
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <param name="syncPointId">同步点ID</param>
    /// <returns>(已到达人数, 预期人数)</returns>
    public (int Arrived, int Expected) GetWaitPointArrivalStatus(string roomCode, string syncPointId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return (0, 0);

        lock (room)
        {
            int arrived = room.WaitPointArrivals.TryGetValue(syncPointId, out var arrivals) ? arrivals.Count : 0;
            int expected = room.CurrentUnifiedWaitPoint?.SyncPointId == syncPointId 
                ? room.CurrentUnifiedWaitPoint.ExpectedWaitCount 
                : 0;
            return (arrived, expected);
        }
    }

    /// <summary>
    /// 检查等待点是否全员到达（multiplayer-abnormal-wait-coordination 需求 5.2）
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <param name="syncPointId">同步点ID</param>
    /// <returns>是否全员到达</returns>
    public bool CheckAllWaitPointArrived(string roomCode, string syncPointId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return false;

        lock (room)
        {
            var unifiedWaitPoint = room.CurrentUnifiedWaitPoint;
            if (unifiedWaitPoint == null || unifiedWaitPoint.SyncPointId != syncPointId)
                return false;

            int arrived = room.WaitPointArrivals.TryGetValue(syncPointId, out var arrivals) ? arrivals.Count : 0;
            return arrived >= unifiedWaitPoint.ExpectedWaitCount;
        }
    }

    /// <summary>
    /// 清除等待点到达记录（multiplayer-abnormal-wait-coordination 需求 5.4）
    /// 全员到达后调用，清理 WaitPointArrivals 防止后续轮次数据污染
    /// </summary>
    /// <param name="roomCode">房间码</param>
    public void ClearWaitPointArrivals(string roomCode)
    {
        if (!_rooms.TryGetValue(roomCode, out var room))
            return;

        lock (room)
        {
            room.WaitPointArrivals.Clear();
            _logger?.LogDebug("[ClearWaitPointArrivals] 已清除房间 {RoomCode} 的等待点到达记录", roomCode);
        }
    }

    /// <summary>移除超时玩家，返回受影响的房间码列表</summary>
    public List<string> RemoveDeadPlayers(TimeSpan timeout)
    {
        var affected = new List<string>();
        var cutoff = DateTime.UtcNow - timeout;

        foreach (var (code, room) in _rooms)
        {
            List<string> deadConnections;
            lock (room)
            {
                deadConnections = room.Players
                    .Where(p => p.LastHeartbeat < cutoff)
                    .Select(p => p.ConnectionId)
                    .ToList();
            }

            foreach (var connId in deadConnections)
            {
                _connectionRoomMap.TryRemove(connId, out _);
            }

            lock (room)
            {
                var deadPlayers = room.Players
                    .Where(p => p.LastHeartbeat < cutoff)
                    .ToList();

                if (deadPlayers.Count > 0)
                {
                    _lastRemovedPlayers[code] = deadPlayers;
                }

                var removed = room.Players.RemoveAll(p => p.LastHeartbeat < cutoff);
                if (removed > 0)
                {
                    affected.Add(code);
                    if (room.Players.Count == 0)
                        _rooms.TryRemove(code, out _);
                }
            }
        }

        return affected;
    }

    public List<PlayerInfo> GetLastRemovedPlayers(string roomCode)
    {
        _lastRemovedPlayers.TryRemove(roomCode, out var removed);
        return removed ?? new List<PlayerInfo>();
    }

    private static bool AllOnlineMembersReported(Room room, HashSet<string> reported)
    {
        // 只检查最近有心跳的在线玩家
        var onlinePlayers = room.Players
            .Where(p => DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2))
            .ToList();
            
        if (onlinePlayers.Count == 0) return false;
        
        // 所有在线玩家都已上报：reported 集合应该覆盖所有在线玩家的 ConnectionId
        return onlinePlayers.All(p => reported.Contains(p.ConnectionId));
    }
    
    /// <summary>
    /// 添加玩家到房间并注册连接映射（仅用于测试）
    /// </summary>
    public void AddPlayerForTesting(string roomCode, PlayerInfo player)
    {
        if (!_rooms.TryGetValue(roomCode, out var room)) return;
        lock (room)
        {
            room.Players.Add(player);
            _connectionRoomMap[player.ConnectionId] = roomCode;
        }
    }
    
    /// <summary>
    /// 更新指定连接的心跳（仅用于测试，绕过连接映射验证）
    /// </summary>
    public void UpdateHeartbeatForConnectionId(string roomCode, string connectionId)
    {
        if (!_rooms.TryGetValue(roomCode, out var room)) return;
        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player != null)
                player.LastHeartbeat = DateTime.UtcNow;
        }
    }

    private static string GenerateCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
            chars[i] = CodeChars[Random.Shared.Next(CodeChars.Length)];
        return new string(chars);
    }

    /// <summary>
    /// 获取所有房间及其房间码（用于重对齐超时检查）
    /// </summary>
    public IEnumerable<(Room Room, string RoomCode)> GetAllRoomsWithCodes()
    {
        foreach (var (roomCode, room) in _rooms)
        {
            yield return (room, roomCode);
        }
    }

    // === 联机锄地异常同步机制方法（multiplayer-abnormal-sync-server spec）===
    // Validates: Requirements REQ-4, REQ-5, REQ-6

    /// <summary>
    /// 计算指定线路同步点的有效等待人数
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <param name="currentRouteIndex">当前线路索引</param>
    /// <returns>有效等待人数</returns>
    public int GetEffectiveWaitCount(string roomCode, int currentRouteIndex)
    {
        var room = GetRoom(roomCode);
        if (room == null) return 0;

        lock (room)
        {
            // 在线玩家数（2分钟内有心跳）
            int onlineCount = room.Players.Count(p =>
                DateTime.UtcNow - p.LastHeartbeat < TimeSpan.FromMinutes(2));

            // 在其他线路等待的异常玩家数
            int abnormalInOtherRoutes = room.AbnormalPlayerInfos.Values
                .Count(a => a.TargetRouteIndex != currentRouteIndex);

            return Math.Max(1, onlineCount - abnormalInOtherRoutes);
        }
    }

    /// <summary>
    /// 获取房间的同步超时秒数
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <returns>超时秒数（无异常=60，有异常=300）</returns>
    public int GetSyncTimeoutSeconds(string roomCode)
    {
        var room = GetRoom(roomCode);
        if (room == null) return 60;

        lock (room)
        {
            return room.AbnormalPlayerInfos.Count > 0 ? 300 : 60;
        }
    }

    /// <summary>
    /// 重置房间的异常状态（多轮世界新轮次开始时调用）
    /// </summary>
    /// <param name="roomCode">房间码</param>
    public void ResetAbnormalStates(string roomCode)
    {
        var room = GetRoom(roomCode);
        if (room == null) return;

        lock (room)
        {
            room.AbnormalPlayerInfos.Clear();
        }

        _logger?.LogInformation("[ResetAbnormalStates] 房间 {RoomCode} 异常状态已重置", roomCode);
    }
}
