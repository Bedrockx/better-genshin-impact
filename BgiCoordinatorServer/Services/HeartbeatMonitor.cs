using BgiCoordinatorServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BgiCoordinatorServer.Services;

public class HeartbeatMonitor : IHostedService, IDisposable
{
    private readonly RoomManager _roomManager;
    private readonly IHubContext<CoordinatorHub> _hubContext;
    private readonly ILogger<HeartbeatMonitor> _logger;
    private Timer? _timer;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PlayerTimeout = TimeSpan.FromSeconds(30);

    public HeartbeatMonitor(
        RoomManager roomManager,
        IHubContext<CoordinatorHub> hubContext,
        ILogger<HeartbeatMonitor> logger)
    {
        _roomManager = roomManager;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatMonitor 启动，扫描间隔 {Interval}s，超时阈值 {Timeout}s",
            ScanInterval.TotalSeconds, PlayerTimeout.TotalSeconds);
        _timer = new Timer(Scan, null, ScanInterval, ScanInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatMonitor 停止");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void Scan(object? state)
    {
        try
        {
            var affectedRooms = _roomManager.RemoveDeadPlayers(PlayerTimeout);
            foreach (var roomCode in affectedRooms)
            {
                var removedPlayers = _roomManager.GetLastRemovedPlayers(roomCode);
                var room = _roomManager.GetRoom(roomCode);

                foreach (var removedPlayer in removedPlayers)
                {
                    // Check if the removed player was the host
                    if (room != null && removedPlayer.ConnectionId == room.HostConnectionId)
                    {
                        // Host was removed: broadcast RoomClosed and delete the room
                        _logger.LogWarning("房间 {RoomCode} 房主 {PlayerName} 心跳超时，关闭房间",
                            roomCode, removedPlayer.PlayerName);

                        _ = _hubContext.Clients.Group(roomCode)
                            .SendAsync("RoomClosed", "房主心跳超时");

                        _roomManager.DeleteRoom(roomCode);
                        break; // Room is deleted, no need to process remaining removed players
                    }
                    else
                    {
                        // Member was removed: broadcast MemberStatusChanged
                        _logger.LogWarning("房间 {RoomCode} 成员 {PlayerName}({Uid}) 心跳超时，标记离线",
                            roomCode, removedPlayer.PlayerName, removedPlayer.PlayerUid);

                        _ = _hubContext.Clients.Group(roomCode)
                            .SendAsync("MemberStatusChanged", removedPlayer.PlayerUid, "Offline", long.MaxValue);
                    }
                }

                // Still send PlayerListUpdated for remaining players
                if (room != null)
                {
                    var players = room.Players ?? [];
                    _logger.LogInformation("房间 {RoomCode} 有玩家超时断线，当前剩余 {Count} 人", roomCode, players.Count);

                    _ = _hubContext.Clients.Group(roomCode)
                        .SendAsync("PlayerListUpdated", players);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HeartbeatMonitor 扫描时发生异常");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
