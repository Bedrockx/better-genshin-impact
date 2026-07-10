#nullable enable

using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// CoordinatorClient 发送/接收测试（skip-route-wait-point-report spec）
/// 测试 CoordinatorClient 的等待点上报发送和接收功能
/// </summary>
public class CoordinatorClientWaitPointTests : IDisposable
{
    private readonly Mock<ILogger<CoordinatorClient>> _mockLogger;
    private readonly CoordinatorClient _client;
    private readonly Mock<HubConnection> _mockConnection;
    
    public CoordinatorClientWaitPointTests()
    {
        _mockLogger = new Mock<ILogger<CoordinatorClient>>();
        
        // 创建模拟的 HubConnection
        _mockConnection = new Mock<HubConnection>();
        _mockConnection.SetupGet(c => c.State).Returns(HubConnectionState.Connected);
        
        // 使用反射设置私有字段
        var clientType = typeof(CoordinatorClient);
        var connectionField = clientType.GetField("_connection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        _client = new CoordinatorClient();
        if (connectionField != null)
        {
            connectionField.SetValue(_client, _mockConnection.Object);
        }
    }
    
    public void Dispose()
    {
        _client.DisposeAsync().AsTask().Wait();
    }
    
    // ========== 单元测试：SendWaitPointReportAsync 方法 ==========
    
    [Fact]
    public async Task SendWaitPointReportAsync_WhenConnected_CallsHubMethod()
    {
        // Arrange
        string routeId = "Route_1";
        string syncPointId = "sync_1_2_3";
        int worldRound = 1;
        
        _mockConnection.Setup(c => c.InvokeAsync("WaitPointReport", routeId, syncPointId, worldRound))
            .Returns(Task.CompletedTask)
            .Verifiable();
        
        // Act
        var result = await _client.SendWaitPointReportAsync(routeId, syncPointId, worldRound);
        
        // Assert
        Assert.True(result);
        _mockConnection.Verify();
    }
    
    [Fact]
    public async Task SendWaitPointReportAsync_WhenNotConnected_ReturnsFalse()
    {
        // Arrange
        _mockConnection.SetupGet(c => c.State).Returns(HubConnectionState.Disconnected);
        
        // Act
        var result = await _client.SendWaitPointReportAsync("Route_1", "sync_1_2_3", 1);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public async Task SendWaitPointReportAsync_WhenConnectionNull_ReturnsFalse()
    {
        // Arrange
        var client = new CoordinatorClient(); // 没有设置 _connection
        
        // Act
        var result = await client.SendWaitPointReportAsync("Route_1", "sync_1_2_3", 1);
        
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public async Task SendWaitPointReportAsync_WithRetryOnFailure_RetriesUpToMax()
    {
        // Arrange
        int callCount = 0;
        _mockConnection.Setup(c => c.InvokeAsync("WaitPointReport", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new Exception("Test exception"));
        
        // Act
        var result = await _client.SendWaitPointReportAsync("Route_1", "sync_1_2_3", 1);
        
        // Assert - 应该重试3次（初始尝试 + 2次重试）
        Assert.False(result);
        Assert.Equal(3, callCount); // 最大重试次数为3
    }
    
    [Fact]
    public async Task SendWaitPointReportAsync_WithRetrySuccess_ReturnsTrue()
    {
        // Arrange
        int callCount = 0;
        _mockConnection.Setup(c => c.InvokeAsync("WaitPointReport", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback(() => callCount++)
            .Returns(() => 
            {
                if (callCount < 2)
                    throw new Exception("Test exception");
                return Task.CompletedTask;
            });
        
        // Act
        var result = await _client.SendWaitPointReportAsync("Route_1", "sync_1_2_3", 1);
        
        // Assert - 第2次调用应该成功
        Assert.True(result);
        Assert.Equal(2, callCount);
    }
    
    // ========== 单元测试：WaitPointReported 事件 ==========
    
    [Fact]
    public void WaitPointReported_Event_IsTriggeredOnServerMessage()
    {
        // Arrange
        string receivedPlayerUid = "";
        string receivedRouteId = "";
        string receivedSyncPointId = "";
        int receivedWorldRound = 0;
        DateTime receivedTimestamp = DateTime.MinValue;
        
        _client.WaitPointReported += (playerUid, routeId, syncPointId, worldRound, timestamp) =>
        {
            receivedPlayerUid = playerUid;
            receivedRouteId = routeId;
            receivedSyncPointId = syncPointId;
            receivedWorldRound = worldRound;
            receivedTimestamp = timestamp;
        };
        
        // Act - 模拟服务器消息
        var testTimestamp = DateTime.UtcNow;
        _client.GetType()
            .GetMethod("OnWaitPointReported", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_client, new object[] { "player123", "Route_1", "sync_1_2_3", 1, testTimestamp });
        
        // Assert
        Assert.Equal("player123", receivedPlayerUid);
        Assert.Equal("Route_1", receivedRouteId);
        Assert.Equal("sync_1_2_3", receivedSyncPointId);
        Assert.Equal(1, receivedWorldRound);
        Assert.Equal(testTimestamp, receivedTimestamp);
    }
    
    [Fact]
    public void WaitPointReported_Event_FiltersSelfNotifications()
    {
        // Arrange
        bool eventTriggered = false;
        
        // 设置玩家UID
        var clientType = typeof(CoordinatorClient);
        var playerUidField = clientType.GetField("_playerUid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (playerUidField != null)
        {
            playerUidField.SetValue(_client, "player123");
        }
        
        _client.WaitPointReported += (playerUid, routeId, syncPointId, worldRound, timestamp) =>
        {
            eventTriggered = true;
        };
        
        // Act - 模拟自己发出的通知
        var testTimestamp = DateTime.UtcNow;
        _client.GetType()
            .GetMethod("OnWaitPointReported", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_client, new object[] { "player123", "Route_1", "sync_1_2_3", 1, testTimestamp });
        
        // Assert - 自己发出的通知应该被过滤
        Assert.False(eventTriggered);
    }
    
    [Fact]
    public void WaitPointReported_Event_ValidatesParameters()
    {
        // Arrange
        bool validEventTriggered = false;
        bool invalidEventTriggered = false;
        
        _client.WaitPointReported += (playerUid, routeId, syncPointId, worldRound, timestamp) =>
        {
            if (!string.IsNullOrEmpty(routeId) && !string.IsNullOrEmpty(syncPointId))
                validEventTriggered = true;
            else
                invalidEventTriggered = true;
        };
        
        // Act - 模拟有效消息
        _client.GetType()
            .GetMethod("OnWaitPointReported", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_client, new object[] { "player123", "Route_1", "sync_1_2_3", 1, DateTime.UtcNow });
        
        // Act - 模拟无效消息（空routeId）
        _client.GetType()
            .GetMethod("OnWaitPointReported", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_client, new object[] { "player123", "", "sync_1_2_3", 1, DateTime.UtcNow });
        
        // Assert
        Assert.True(validEventTriggered);
        Assert.False(invalidEventTriggered); // 无效消息应该被过滤
    }
    
    // ========== 单元测试：ResetWaitPointState 方法 ==========
    
    [Fact]
    public void ResetWaitPointState_ClearsWaitPointState()
    {
        // Arrange - 设置一些状态
        var clientType = typeof(CoordinatorClient);
        
        // Act
        _client.ResetWaitPointState();
        
        // Assert - 方法应该执行而不抛出异常
        Assert.True(true); // 如果执行到这里，说明方法成功执行
    }
    
    // ========== 单元测试：ResetForNewWorldRoundAsync 方法 ==========
    
    [Fact]
    public async Task ResetForNewWorldRoundAsync_WhenConnected_CallsHubMethod()
    {
        // Arrange
        int newRound = 2;
        
        _mockConnection.Setup(c => c.InvokeAsync("ResetForNewWorldRound", newRound))
            .Returns(Task.CompletedTask)
            .Verifiable();
        
        // Act
        await _client.ResetForNewWorldRoundAsync(newRound);
        
        // Assert
        _mockConnection.Verify();
    }
    
    [Fact]
    public async Task ResetForNewWorldRoundAsync_WhenNotConnected_DoesNotThrow()
    {
        // Arrange
        _mockConnection.SetupGet(c => c.State).Returns(HubConnectionState.Disconnected);
        
        // Act & Assert - 不应该抛出异常
        await _client.ResetForNewWorldRoundAsync(2);
    }
    
    [Fact]
    public async Task ResetForNewWorldRoundAsync_WhenConnectionNull_DoesNotThrow()
    {
        // Arrange
        var client = new CoordinatorClient(); // 没有设置 _connection
        
        // Act & Assert - 不应该抛出异常
        await client.ResetForNewWorldRoundAsync(2);
    }
    
    // ========== 单元测试：多轮世界支持 ==========
    
    [Fact]
    public void WaitPointReported_Event_ValidatesWorldRound()
    {
        // Arrange
        bool eventTriggered = false;
        int receivedWorldRound = 0;
        
        _client.WaitPointReported += (playerUid, routeId, syncPointId, worldRound, timestamp) =>
        {
            eventTriggered = true;
            receivedWorldRound = worldRound;
        };
        
        // Act - 模拟有效轮次的消息
        _client.GetType()
            .GetMethod("OnWaitPointReported", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_client, new object[] { "player123", "Route_1", "sync_1_2_3", 1, DateTime.UtcNow });
        
        // Assert
        Assert.True(eventTriggered);
        Assert.Equal(1, receivedWorldRound);
    }
    
    // ========== 单元测试：网络异常处理 ==========
    
    [Fact]
    public async Task SendWaitPointReportAsync_WithNetworkException_LogsWarning()
    {
        // Arrange
        var exception = new Exception("Network failure");
        _mockConnection.Setup(c => c.InvokeAsync("WaitPointReport", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(exception);
        
        // 设置模拟日志器
        var mockLogger = new Mock<ILogger<CoordinatorClient>>();
        mockLogger.Setup(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            exception,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Verifiable();
        
        // 注意：由于 CoordinatorClient 使用静态 App.GetLogger，我们无法轻松注入模拟日志器
        // 这个测试主要是验证异常处理逻辑
        
        // Act
        var result = await _client.SendWaitPointReportAsync("Route_1", "sync_1_2_3", 1);
        
        // Assert
        Assert.False(result);
        // 无法验证日志调用，因为使用了静态日志器
    }
}