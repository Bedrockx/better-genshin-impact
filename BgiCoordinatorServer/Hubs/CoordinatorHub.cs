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
        var allArrived = _roomManager.RecordArrival(roomCode, syncPointId, Context.ConnectionId);

        if (allArrived)
        {
            _logger.LogInformation("房间 {Code} 同步点 {SyncId} 全员到达", roomCode, syncPointId);
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

    /// <summary>
    /// 上报成员异常恢复状态（需求 7）。
    /// 服务器透传状态和版本号给房间内所有成员，不做版本管理。
    /// </summary>
    public async Task ReportMemberStatus(string status, long version)
    {
        var (room, roomCode) = _roomManager.GetRoomByConnectionId(Context.ConnectionId);
        if (room == null || roomCode == null) return;

        _roomManager.UpdateHeartbeat(Context.ConnectionId);

        // 在 Players 列表中查找当前连接对应的 PlayerUid
        string? playerUid;
        lock (room)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            if (player == null) return;
            playerUid = player.PlayerUid;
        }

        // lock 外 await，避免死锁
        await Clients.Group(roomCode!).SendAsync("MemberStatusChanged", playerUid, status, version);
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
        var affectedCodes = _roomManager.LeaveRoom(Context.ConnectionId);

        foreach (var code in affectedCodes)
        {
            var updatedRoom = _roomManager.GetRoom(code);
            var players = updatedRoom?.Players ?? [];
            await Clients.Group(code).SendAsync("PlayerListUpdated", players);
        }

        _logger.LogInformation("连接 {ConnId} 断开，影响房间：{Rooms}",
            Context.ConnectionId, string.Join(", ", affectedCodes));

        await base.OnDisconnectedAsync(exception);
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
}
