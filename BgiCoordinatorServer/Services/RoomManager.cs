using System.Collections.Concurrent;
using System.Linq;
using BgiCoordinatorServer.Models;

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

    public RoomManager(int maxRooms = 50)
    {
        _maxRooms = maxRooms;
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
            return AllOnlineMembersReported(room, arrivals);
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
            
        return onlinePlayers.Count > 0
               && onlinePlayers.All(p => reported.Contains(p.ConnectionId));
    }

    private static string GenerateCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
            chars[i] = CodeChars[Random.Shared.Next(CodeChars.Length)];
        return new string(chars);
    }
}
