using BgiCoordinatorServer.Models;
using BgiCoordinatorServer.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BgiCoordinatorServer.Tests;

public class RouteVerificationSyncTests
{
    private readonly RoomManager _roomManager;
    private readonly Mock<ILogger<RoomManager>> _loggerMock;

    public RouteVerificationSyncTests()
    {
        _loggerMock = new Mock<ILogger<RoomManager>>();
        _roomManager = new RoomManager(50, _loggerMock.Object);
    }

    [Fact]
    public void RecordRouteVerificationDone_WithOfflinePlayer_ShouldIgnoreOfflinePlayer()
    {
        // Arrange: 创建房间
        var roomCode = _roomManager.CreateRoom("conn-host", "Host", null, "host-uid", 1);
        var onlineConnectionId = "online-conn";
        var offlineConnectionId = "offline-conn";

        // Make host offline so only the test players are counted
        var room = _roomManager.GetRoom(roomCode);
        Assert.NotNull(room);
        room!.Players[0].LastHeartbeat = DateTime.UtcNow.AddMinutes(-5);

        // Add online player using helper (registers connection mapping)
        _roomManager.AddPlayerForTesting(roomCode, new PlayerInfo
        {
            ConnectionId = onlineConnectionId,
            PlayerId = onlineConnectionId,
            PlayerName = "Online",
            PlayerUid = "100",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });

        // Add offline player (old heartbeat) using helper
        _roomManager.AddPlayerForTesting(roomCode, new PlayerInfo
        {
            ConnectionId = offlineConnectionId,
            PlayerId = offlineConnectionId,
            PlayerName = "Offline",
            PlayerUid = "101",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-5) // 5 minutes ago
        });

        // Act - only online player reports
        var result = _roomManager.RecordRouteVerificationDone(roomCode, onlineConnectionId);

        // Assert - should return true because offline player is ignored
        Assert.True(result);
    }

    [Fact]
    public void RecordRouteVerificationDone_WithAllOnlinePlayersReported_ShouldReturnTrue()
    {
        // Arrange: 创建房间
        var roomCode = _roomManager.CreateRoom("conn-host", "Host", null, "host-uid", 1);
        var conn1 = "conn1";
        var conn2 = "conn2";

        // Make host offline so only the test players are counted
        var room = _roomManager.GetRoom(roomCode);
        Assert.NotNull(room);
        room!.Players[0].LastHeartbeat = DateTime.UtcNow.AddMinutes(-5);

        // Add two online players using helper (registers connection mapping)
        _roomManager.AddPlayerForTesting(roomCode, new PlayerInfo
        {
            ConnectionId = conn1,
            PlayerId = conn1,
            PlayerName = "Player1",
            PlayerUid = "100",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });
        _roomManager.AddPlayerForTesting(roomCode, new PlayerInfo
        {
            ConnectionId = conn2,
            PlayerId = conn2,
            PlayerName = "Player2",
            PlayerUid = "101",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });

        // Act - both players report
        var result1 = _roomManager.RecordRouteVerificationDone(roomCode, conn1);
        var result2 = _roomManager.RecordRouteVerificationDone(roomCode, conn2);

        // Assert
        Assert.False(result1); // First report should not complete
        Assert.True(result2);  // Second report should complete
    }

    [Fact]
    public void GetRouteVerificationStatus_ShouldReturnCorrectCounts()
    {
        // Arrange: 创建房间
        var roomCode = _roomManager.CreateRoom("conn-host", "Host", null, "host-uid", 1);
        var onlineConn = "online";
        var offlineConn = "offline";

        // Make host offline so only the test players are counted
        var room = _roomManager.GetRoom(roomCode);
        Assert.NotNull(room);
        room!.Players[0].LastHeartbeat = DateTime.UtcNow.AddMinutes(-5);

        // Add online player using helper
        _roomManager.AddPlayerForTesting(roomCode, new PlayerInfo
        {
            ConnectionId = onlineConn,
            PlayerId = onlineConn,
            PlayerName = "Online",
            PlayerUid = "100",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });
        // Add offline player using helper
        _roomManager.AddPlayerForTesting(roomCode, new PlayerInfo
        {
            ConnectionId = offlineConn,
            PlayerId = offlineConn,
            PlayerName = "Offline",
            PlayerUid = "101",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-5)
        });

        // Act
        _roomManager.RecordRouteVerificationDone(roomCode, onlineConn);
        var (onlineCount, reportedCount) = _roomManager.GetRouteVerificationStatus(roomCode);

        // Assert
        Assert.Equal(1, onlineCount);  // Only one online player
        Assert.Equal(1, reportedCount); // One reported
    }
}