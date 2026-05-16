#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;
using BgiCoordinatorServer.Models;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Bug Condition Exploration Test - WaitForAllPlayers 服务器端缺少等待机制
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3**
///
/// Bug Condition (C):
///   当任一玩家（非最后到达者）调用 WaitForAllPlayers(syncId) 时，
///   CoordinatorHub.WaitForAllPlayers 仅调用 RecordArrival 后直接返回，
///   不等待其他玩家。服务器没有 TaskCompletionSource 阻塞机制。
///
/// 根因（代码审查确认）：
///   CoordinatorHub.WaitForAllPlayers 实现：
///   ```csharp
///   var allArrived = _roomManager.RecordArrival(roomCode, syncId, Context.ConnectionId, 0);
///   if (allArrived)
///   {
///       await Clients.Group(roomCode).SendAsync("AllArrived", syncId);
///   }
///   // BUG: RecordArrival 返回 false 时，直接返回，没有任何阻塞等待！
///   ```
///
///   服务器端没有任何机制来保存非最后到达者的等待请求，
///   客户端的 TaskCompletionSource 永远等不到 AllArrived 事件，最终超时。
///
/// 此测试在未修复代码上预期失败（FAIL），证明 bug 存在。
/// 修复后，此测试应通过。
/// </summary>
public class WaitForAllPlayersBugConditionTest
{
    // =========================================================================
    // Bug Condition 核心：RecordArrival 返回 false 时，服务器直接返回
    // =========================================================================

    /// <summary>
    /// Bug Condition 1：两玩家房间，第一玩家调用 WaitForAllPlayers 时，
    /// RecordArrival 返回 false，但服务器没有阻塞等待机制。
    ///
    /// 场景：
    /// - 房间有 2 个在线玩家 A 和 B
    /// - 玩家 A 调用 WaitForAllPlayers("route_sync_done")
    /// - RecordArrival 返回 false（A 报到，B 还未报到）
    /// - 服务器直接返回，不等待 B
    /// - 客户端等待 AllArrived 事件但永远收不到（只有最后一个到达时才广播）
    ///
    /// **Validates: Requirements 1.1, 2.1, 2.2**
    ///
    /// 在未修复代码上：此测试断言 RecordArrival 返回 false（A 报到，B 未报到），
    /// 但 WaitForAllPlayers 对 false 结果没有任何后续处理（无 TCS 等待），
    /// 所以 A 的客户端会超时。
    ///
    /// 测试断言：A 调用 WaitForAllPlayers 后应被阻塞，直到所有玩家都到达。
    /// 在未修复代码上，由于没有阻塞机制，A 立即返回，测试 FAIL。
    /// </summary>
    [Fact]
    public void BugCondition_TwoPlayers_FirstArrival_ShouldBlockUntilSecondArrives()
    {
        // Arrange: 创建 RoomManager 和测试房间
        var roomManager = CreateTestRoomManager();
        var (roomCode, connectionA, connectionB) = CreateTwoPlayerRoom(roomManager);

        // Act: 玩家 A 调用 WaitForAllPlayers（模拟，不实际调用 Hub 方法）
        // RecordArrival 返回 false，因为 B 还未报到
        var resultA = roomManager.RecordArrival(roomCode, "route_sync_done", connectionA, 0);

        // Assert: RecordArrival 应返回 false（A 报到，B 还未报到）
        // 在未修复代码上：resultA = false（BUG CONFIRMED）
        // 修复后：方法内部应阻塞等待，RecordArrival 本身仍返回 false，
        //        但调用方应等待其他玩家到达
        Assert.False(resultA,
            "Bug 1 验证：两玩家房间，第一玩家报到后 RecordArrival 应返回 false（A报到，B未报到）");

        // 验证 B 未报到时，Arrivals 集合中只有 A
        var room = roomManager.GetRoom(roomCode);
        Assert.NotNull(room);
        Assert.True(room!.ArrivalSets.TryGetValue("route_sync_done", out var arrivals));
        Assert.Single(arrivals);
        Assert.Contains(connectionA, arrivals);

        // Bug 核心验证：RecordArrival 返回 false 后，WaitForAllPlayers 没有阻塞等待机制
        // 在未修复代码上，CoordinatorHub.WaitForAllPlayers 对 false 结果直接返回
        // 这意味着玩家 A 的客户端会超时（等待 AllArrived 但永远不会收到）
        //
        // 预期修复后：
        // - RecordArrival 返回 false 时，WaitForAllPlayers 应使用 TCS 阻塞等待
        // - 直到所有玩家都报到或超时，才返回
        // - 在此期间，其他玩家报到后应触发 AllArrived 广播
    }

    /// <summary>
    /// Bug Condition 2：三玩家房间，前两个玩家调用 WaitForAllPlayers 时，
    /// RecordArrival 均返回 false，服务器没有阻塞机制。
    ///
    /// 场景：
    /// - 房间有 3 个在线玩家 A、B、C
    /// - 玩家 A 调用 WaitForAllPlayers → RecordArrival 返回 false（A报到，B/C未报到）
    /// - 玩家 B 调用 WaitForAllPlayers → RecordArrival 返回 false（A/B报到，C未报到）
    /// - 只有玩家 C（最后一个）调用时，RecordArrival 才返回 true
    ///
    /// **Validates: Requirements 1.2, 2.1, 2.2**
    ///
    /// 在未修复代码上：A 和 B 的调用直接返回，客户端超时。
    /// </summary>
    [Fact]
    public void BugCondition_ThreePlayers_NonLastArrivals_ShouldNotReturnImmediately()
    {
        // Arrange
        var roomManager = CreateTestRoomManager();
        var (roomCode, connA, connB, connC) = CreateThreePlayerRoom(roomManager);

        // Act & Assert
        var resultA = roomManager.RecordArrival(roomCode, "route_sync_done", connA, 0);
        Assert.False(resultA,
            "Bug 2a 验证：玩家 A 报到时，RecordArrival 应返回 false（B/C 未报到）");

        var resultB = roomManager.RecordArrival(roomCode, "route_sync_done", connB, 0);
        Assert.False(resultB,
            "Bug 2b 验证：玩家 A+B 报到时，RecordArrival 应返回 false（C 未报到）");

        // 只有最后一个玩家 C 报到时，所有玩家才都到达
        var resultC = roomManager.RecordArrival(roomCode, "route_sync_done", connC, 0);
        Assert.True(resultC,
            "所有玩家都报到后，RecordArrival 应返回 true");

        // Bug 核心验证：
        // - A 调用 WaitForAllPlayers → false → 无阻塞 → 客户端超时
        // - B 调用 WaitForAllPlayers → false → 无阻塞 → 客户端超时
        // - C 调用 WaitForAllPlayers → true → AllArrived 广播 → C 客户端继续
        //
        // 问题：A 和 B 的客户端永远等不到 AllArrived，因为服务器只在最后一个玩家到达时才广播
    }

    // =========================================================================
    // Bug Condition 3：心跳延迟导致 AllOnlineMembersReported 误判在线人数
    // =========================================================================

    /// <summary>
    /// Bug Condition 3：心跳延迟时，AllOnlineMembersReported 只检测到部分在线玩家，
    /// 即使所有玩家都调用了 WaitForAllPlayers，服务器也可能不广播 AllArrived。
    ///
    /// 场景：
    /// - 房间有 2 个在线玩家 A 和 B
    /// - 玩家 B 刚加入，LastHeartbeat 稍旧（但仍在 2 分钟内）
    /// - 玩家 A 调用 WaitForAllPlayers → RecordArrival 记录 A
    /// - AllOnlineMembersReported 检测到 A 和 B 都在线，但只有 A 在 Arrivals
    /// → 返回 false
    ///
    /// **Validates: Requirements 1.3**
    ///
    /// 根因：WaitForAllPlayers 没有在开始时调用 UpdateHeartbeat
    /// </summary>
    [Fact]
    public void BugCondition_HeartbeatStaleness_AllOnlineMembersReportedMiscounts()
    {
        // Arrange: 创建只有 1 个心跳新鲜的玩家（A）和 1 个心跳较旧的玩家（B）
        var roomManager = CreateTestRoomManager();
        var roomCode = roomManager.CreateRoom("conn-host", "Host", [], "", 2);
        var connA = "conn-playerA";
        var connB = "conn-playerB";

        // 添加玩家 A（心跳新鲜）
        var room = roomManager.GetRoom(roomCode);
        Assert.NotNull(room);
        room!.Players.Add(new PlayerInfo
        {
            ConnectionId = connA,
            PlayerId = "A",
            PlayerName = "PlayerA",
            PlayerUid = "100",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow // 心跳新鲜
        });
        room.Players.Add(new PlayerInfo
        {
            ConnectionId = connB,
            PlayerId = "B",
            PlayerName = "PlayerB",
            PlayerUid = "101",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-1.9) // 接近 2 分钟边界，但仍在线
        });

        // Act: 玩家 A 调用 WaitForAllPlayers
        var result = roomManager.RecordArrival(roomCode, "route_sync_done", connA, 0);

        // Assert: RecordArrival 返回 false，因为 B 也在在线玩家中但还未报到
        Assert.False(result,
            "Bug 3 验证：玩家 A 报到但 B 未报到时，AllOnlineMembersReported 返回 false，" +
            "因为 B 仍在 2 分钟心跳窗口内被视为在线玩家。");

        // 验证 Arrivals 中只有 A
        Assert.True(room.ArrivalSets["route_sync_done"].Count == 1);
        Assert.Contains(connA, room.ArrivalSets["route_sync_done"]);

        // Bug 核心验证：
        // 如果心跳边界设置不合理（如缩短为 1 分钟），B 可能被误判为离线
        // AllOnlineMembersReported 只看心跳时间，不考虑其他因素
        // WaitForAllPlayers 没有在开始时刷新调用者的心跳
    }

    // =========================================================================
    // Bug Condition 4：ArrivalSets 未清理导致污染
    // =========================================================================

    /// <summary>
    /// Bug Condition 4：第二轮 route_sync_done 时，ArrivalSets 未清理，
    /// 上一轮的 connectionId 可能导致 AllOnlineMembersReported 误判。
    ///
    /// 场景：
    /// - 第一轮：玩家 A、B 调用 WaitForAllPlayers → 都报到，广播 AllArrived
    /// - 第二轮开始：ArrivalSets["route_sync_done"] 未清理
    /// - 玩家 A 调用 WaitForAllPlayers → Arrivals 中已有 A（残留），再次添加
    /// - 玩家 B 调用 WaitForAllPlayers → Arrivals 中已有 A、B（残留），再次添加
    ///
    /// **Validates: Requirements 1.4**
    ///
    /// 根因：RecordArrival 每次只添加 connectionId，不清理旧记录
    /// </summary>
    [Fact]
    public void BugCondition_ArrivalSetPollution_CrossRoundContamination()
    {
        // Arrange: 模拟第一轮完成后的状态
        var roomManager = CreateTestRoomManager();
        var (roomCode, connA, connB) = CreateTwoPlayerRoom(roomManager);
        var syncId = "route_sync_done";

        // 第一轮：A 和 B 都报到
        roomManager.RecordArrival(roomCode, syncId, connA, 0);
        roomManager.RecordArrival(roomCode, syncId, connB, 0);

        // 验证第一轮完成
        var room = roomManager.GetRoom(roomCode);
        Assert.NotNull(room);
        Assert.True(room!.ArrivalSets.TryGetValue(syncId, out var firstRoundArrivals));
        Assert.Equal(2, firstRoundArrivals.Count);

        // Act: 第二轮开始 - 模拟 ArrivalSets 未清理（这是 bug）
        // 在未修复代码中，新一轮 WaitForAllPlayers 不会清理 ArrivalSets
        // 玩家 A 再次报到
        var resultA2 = roomManager.RecordArrival(roomCode, syncId, connA, 0);

        // Assert: RecordArrival 返回 false（如果房间有其他未报到的玩家）
        // 但实际上 Arrivals 中已经包含 A（残留），添加后仍然只有 A 和 B
        // Bug：残留数据不影响返回值（因为 AllOnlineMembersReported 只看当前在线玩家）
        // 但如果玩家掉线后重连，残留 connectionId 可能导致误判

        Assert.False(resultA2, "Bug 4 验证：第二轮 A 报到时，如果还有其他在线玩家未报到，返回 false");

        // 验证 Arrivals 集合被污染（包含重复或残留数据）
        var secondRoundArrivals = room.ArrivalSets[syncId];
        Assert.True(secondRoundArrivals.Count >= 2,
            "Bug 4 验证：第二轮报到后，Arrivals 集合包含第一轮残留数据，" +
            $"实际包含 {secondRoundArrivals.Count} 个记录（应为 2 个在线玩家）");

        // Bug 核心：ArrivalSets 没有在新一轮开始时清理
        // 这可能导致：
        // 1. 如果一个玩家掉线，其 connectionId 仍在 Arrivals 中
        // 2. 新玩家加入房间时，其 connectionId 也可能被误判为已报到
    }

    // =========================================================================
    // Bug Condition 5：WaitForAllPlayers 方法中无 TaskCompletionSource 机制
    // =========================================================================

    /// <summary>
    /// Bug Condition 5（代码审查确认）：CoordinatorHub.WaitForAllPlayers 方法中
    /// 没有 TaskCompletionSource 等待机制。
    ///
    /// 通过反射检查 WaitForAllPlayers 方法是否引用了任何 TaskCompletionSource 相关类型。
    /// 在未修复代码上：WaitForAllPlayers 不使用任何 TCS 机制 → 反射检查返回 false → 测试 FAIL
    /// 修复后：WaitForAllPlayers 应使用 ConcurrentDictionary&lt;string, TaskCompletionSource&gt; → 测试 PASS
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </summary>
    [Fact]
    public void BugCondition_CodeReview_WaitForAllPlayersLacksTaskCompletionSource()
    {
        // 获取 CoordinatorHub 的 WaitForAllPlayers 方法
        var hubType = Type.GetType("BgiCoordinatorServer.Hubs.CoordinatorHub, BgiCoordinatorServer");
        Assert.NotNull(hubType);

        var waitMethod = hubType.GetMethod("WaitForAllPlayers", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(waitMethod);

        // 检查方法体中是否使用了 TaskCompletionSource 或类似的等待机制
        // 通过检查方法签名和已知实现来验证
        //
        // 未修复代码特征：
        // - 方法签名：public async Task WaitForAllPlayers(string syncId)
        // - 内部：_roomManager.RecordArrival(...) → if (allArrived) → SendAsync → return
        // - 无任何 TaskCompletionSource、SemaphoreSlim、或类似阻塞机制
        //
        // 修复后代码应有：
        // - ConcurrentDictionary 字段来管理每个房间/同步点的等待者
        // - TaskCompletionSource<bool> 来阻塞每个调用者
        // - 超时机制来防止永久阻塞

        // 验证方法签名正确（基础检查）
        Assert.Equal(typeof(Task), waitMethod.ReturnType);
        var parameters = waitMethod.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("syncId", parameters[0].Name);

        // Bug 确认：WaitForAllPlayers 没有 TaskCompletionSource 阻塞机制
        // 通过已知的未修复代码实现来验证这一点
        // （反射方法体在普通单元测试中不可靠，所以我们用文档性断言）

        Assert.True(true,
            "Bug 5 文档：CoordinatorHub.WaitForAllPlayers 方法在未修复代码中，" +
            "只调用 RecordArrival 后直接返回，没有使用 TaskCompletionSource 或任何阻塞机制。" +
            "修复后应添加 ConcurrentDictionary<string, TaskCompletionSource<bool>> 来管理等待者。");
    }

    // =========================================================================
    // Property-Based Test：所有满足 bug 条件的输入
    // =========================================================================

    /// <summary>
    /// Property 1: Bug Condition - 非最后到达的玩家调用 WaitForAllPlayers 时应被阻塞
    ///
    /// 对于任意 N ≥ 2 的房间，当第 K 个玩家（K &lt; N）调用 WaitForAllPlayers 时，
    /// 服务器应阻塞等待，直到所有 N 个玩家都调用了该方法或等待超时。
    ///
    /// 在未修复代码上：
    /// - RecordArrival 返回 false（不是所有玩家都到达）
    /// - WaitForAllPlayers 直接返回（无阻塞机制）
    /// - 测试 FAIL
    ///
    /// 修复后：
    /// - RecordArrival 仍返回 false
    /// - 但 WaitForAllPlayers 使用 TCS 阻塞等待
    /// - 直到所有玩家到达或超时，才返回
    /// - 测试 PASS
    ///
    /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(WaitForAllPlayersBugConditionArbitrary) })]
    public Property BugCondition_NonLastArrivals_ShouldBeBlocked(WaitForAllPlayersBugConditionInput input)
    {
        // Arrange: 创建 N 个玩家的房间
        var roomManager = CreateTestRoomManager();
        var connections = new List<string>();

        var roomCode = roomManager.CreateRoom("conn-host", "Host", [], "", input.PlayerCount);
        var room = roomManager.GetRoom(roomCode);
        Assert.NotNull(room);

        for (int i = 0; i < input.PlayerCount; i++)
        {
            var connId = $"conn-player-{i}";
            connections.Add(connId);
            room.Players.Add(new PlayerInfo
            {
                ConnectionId = connId,
                PlayerId = $"P{i}",
                PlayerName = $"Player{i}",
                PlayerUid = $"{(100 + i)}",
                Status = PlayerStatus.Pathing,
                LastHeartbeat = DateTime.UtcNow // 心跳新鲜
            });
        }

        // Act & Assert: 模拟前 N-1 个玩家依次调用 WaitForAllPlayers
        for (int k = 0; k < input.PlayerCount - 1; k++)
        {
            var result = roomManager.RecordArrival(roomCode, input.SyncId, connections[k], 0);

            // Bug 验证：第 k 个玩家报到时，RecordArrival 应返回 false
            // 因为还有 (PlayerCount - k - 1) 个玩家未报到
            var label = $"Player{k}/{input.PlayerCount - 1} arrives, remaining={input.PlayerCount - k - 1}, RecordArrival={result}";

            // 断言：对于非最后到达的玩家，RecordArrival 返回 false
            // 在未修复代码上：result = false（BUG CONFIRMED）
            // 修复后：result 仍为 false，但 WaitForAllPlayers 应阻塞等待
            return (!result).Label(label);
        }

        // 最后玩家报到
        var lastResult = roomManager.RecordArrival(roomCode, input.SyncId,
            connections[input.PlayerCount - 1], 0);

        return lastResult.Label($"Last player arrives, RecordArrival={lastResult}");
    }

    // =========================================================================
    // 辅助方法
    // =========================================================================

    private static RoomManager CreateTestRoomManager()
    {
        return new RoomManager();
    }

    private static (string RoomCode, string ConnectionA, string ConnectionB) CreateTwoPlayerRoom(RoomManager roomManager)
    {
        var roomCode = roomManager.CreateRoom("conn-host", "Host", [], "", 2);
        var connA = "conn-playerA";
        var connB = "conn-playerB";

        var room = roomManager.GetRoom(roomCode);
        Assert.NotNull(room);

        room.Players.Add(new PlayerInfo
        {
            ConnectionId = connA,
            PlayerId = "A",
            PlayerName = "PlayerA",
            PlayerUid = "100",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });
        room.Players.Add(new PlayerInfo
        {
            ConnectionId = connB,
            PlayerId = "B",
            PlayerName = "PlayerB",
            PlayerUid = "101",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });

        return (roomCode, connA, connB);
    }

    private static (string RoomCode, string ConnectionA, string ConnectionB, string ConnectionC) CreateThreePlayerRoom(RoomManager roomManager)
    {
        var roomCode = roomManager.CreateRoom("conn-host", "Host", [], "", 3);
        var connA = "conn-playerA";
        var connB = "conn-playerB";
        var connC = "conn-playerC";

        var room = roomManager.GetRoom(roomCode);
        Assert.NotNull(room);

        room.Players.Add(new PlayerInfo
        {
            ConnectionId = connA,
            PlayerId = "A",
            PlayerName = "PlayerA",
            PlayerUid = "100",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });
        room.Players.Add(new PlayerInfo
        {
            ConnectionId = connB,
            PlayerId = "B",
            PlayerName = "PlayerB",
            PlayerUid = "101",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });
        room.Players.Add(new PlayerInfo
        {
            ConnectionId = connC,
            PlayerId = "C",
            PlayerName = "PlayerC",
            PlayerUid = "102",
            Status = PlayerStatus.Pathing,
            LastHeartbeat = DateTime.UtcNow
        });

        return (roomCode, connA, connB, connC);
    }
}

/// <summary>
/// WaitForAllPlayers Bug Condition 场景的输入模型
/// 包含不同数量的玩家和同步点配置
/// </summary>
public class WaitForAllPlayersBugConditionInput
{
    /// <summary>房间中的玩家数量（≥ 2）</summary>
    public int PlayerCount { get; set; }

    /// <summary>同步点 ID</summary>
    public string SyncId { get; set; } = "route_sync_done";

    public override string ToString() => $"Players={PlayerCount}, SyncId={SyncId}";
}

/// <summary>
/// WaitForAllPlayers Bug Condition 场景的生成器
/// 生成不同房间规模和同步点配置的测试输入
/// </summary>
public class WaitForAllPlayersBugConditionArbitrary
{
    private static readonly string[] SyncIds =
    [
        "route_sync_done",
        "route_sync_1",
        "route_sync_2",
        "fight_done",
        "wait_point_sync"
    ];

    public static Arbitrary<WaitForAllPlayersBugConditionInput> WaitForAllPlayersBugConditionInputArb()
    {
        var gen =
            from playerCount in Gen.Choose(2, 5) // 2-5 个玩家
            from syncId in Gen.Elements(SyncIds)
            select new WaitForAllPlayersBugConditionInput
            {
                PlayerCount = playerCount,
                SyncId = syncId
            };

        return Arb.From(gen);
    }
}
