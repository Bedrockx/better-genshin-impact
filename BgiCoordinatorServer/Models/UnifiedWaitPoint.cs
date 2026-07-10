#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace BgiCoordinatorServer.Models;

/// <summary>
/// 统一等待点（multiplayer-abnormal-wait-coordination spec）
/// 服务端计算的等待位置，指示正常玩家应在何处等待异常玩家
/// Validates: Requirements 1.1, 1.3, 2.1
/// </summary>
public class UnifiedWaitPoint
{
    /// <summary>
    /// 同步点标识（必须是 _tp_ 格式的传送点）
    /// </summary>
    public string SyncPointId { get; set; } = "";

    /// <summary>
    /// 路线标识
    /// </summary>
    public string RouteId { get; set; } = "";

    /// <summary>
    /// 当前世界轮次（多轮世界支持）
    /// </summary>
    public int WorldRound { get; set; }

    /// <summary>
    /// 需要等待的异常玩家UID列表
    /// </summary>
    public List<string> AbnormalPlayerUids { get; set; } = new();

    /// <summary>
    /// 服务端计算的预期等待人数
    /// = 已到达该线路的正常玩家数 + 异常玩家数
    /// </summary>
    public int ExpectedWaitCount { get; set; }

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>
    /// 过期时间（5分钟超时，UTC）
    /// </summary>
    public DateTime ExpiryTime { get; set; }

    /// <summary>
    /// 创建新的统一等待点
    /// </summary>
    public UnifiedWaitPoint()
    {
        CreatedTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddMinutes(5);
    }

    /// <summary>
    /// 创建新的统一等待点（带参数）
    /// </summary>
    public UnifiedWaitPoint(string syncPointId, string routeId, int worldRound, int expectedWaitCount)
    {
        SyncPointId = syncPointId;
        RouteId = routeId;
        WorldRound = worldRound;
        ExpectedWaitCount = expectedWaitCount;
        CreatedTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddMinutes(5);
    }

    /// <summary>
    /// 验证统一等待点是否有效
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrEmpty(SyncPointId)) return false;
        if (string.IsNullOrEmpty(RouteId)) return false;
        if (WorldRound < 0) return false;
        if (ExpectedWaitCount < 0) return false;

        // 验证必须是传送点格式
        if (!IsValidTeleportPoint())
            return false;

        // 检查是否过期
        if (DateTime.UtcNow > ExpiryTime)
            return false;

        return true;
    }

    /// <summary>
    /// 验证同步点是否为传送点格式（_tp_）
    /// </summary>
    public bool IsValidTeleportPoint()
    {
        return !string.IsNullOrEmpty(SyncPointId) && SyncPointId.Contains("_tp_");
    }

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiryTime;
    }

    /// <summary>
    /// 检查是否仍在有效期内
    /// </summary>
    public bool IsActive()
    {
        return !IsExpired();
    }

    /// <summary>
    /// 添加异常玩家
    /// </summary>
    public void AddAbnormalPlayer(string playerUid)
    {
        if (!AbnormalPlayerUids.Contains(playerUid))
        {
            AbnormalPlayerUids.Add(playerUid);
        }
    }

    /// <summary>
    /// 移除异常玩家
    /// </summary>
    public void RemoveAbnormalPlayer(string playerUid)
    {
        AbnormalPlayerUids.Remove(playerUid);
    }

    /// <summary>
    /// 检查是否包含指定异常玩家
    /// </summary>
    public bool ContainsAbnormalPlayer(string playerUid)
    {
        return AbnormalPlayerUids.Contains(playerUid);
    }

    /// <summary>
    /// 延长过期时间
    /// </summary>
    public void ExtendExpiry(TimeSpan extension)
    {
        ExpiryTime = ExpiryTime.Add(extension);
    }

    /// <summary>
    /// 获取已到达该等待点的异常玩家数量
    /// </summary>
    public int GetAbnormalPlayerCount()
    {
        return AbnormalPlayerUids.Count;
    }

    /// <summary>
    /// 转换为字符串表示（用于日志）
    /// </summary>
    public override string ToString()
    {
        return $"UnifiedWaitPoint[SyncPoint={SyncPointId}, Route={RouteId}, Round={WorldRound}, AbnormalPlayers={string.Join(",", AbnormalPlayerUids)}, Expected={ExpectedWaitCount}, Expires={ExpiryTime:HH:mm:ss}]";
    }

    /// <summary>
    /// 创建深拷贝
    /// </summary>
    public UnifiedWaitPoint Clone()
    {
        return new UnifiedWaitPoint
        {
            SyncPointId = SyncPointId,
            RouteId = RouteId,
            WorldRound = WorldRound,
            AbnormalPlayerUids = new List<string>(AbnormalPlayerUids),
            ExpectedWaitCount = ExpectedWaitCount,
            CreatedTime = CreatedTime,
            ExpiryTime = ExpiryTime
        };
    }

    /// <summary>
    /// 静态工厂方法：从异常玩家状态创建统一等待点
    /// </summary>
    public static UnifiedWaitPoint FromAbnormalPlayerState(AbnormalPlayerState state, int expectedWaitCount)
    {
        return new UnifiedWaitPoint
        {
            SyncPointId = state.WaitPointId,
            RouteId = state.RouteId,
            WorldRound = state.WorldRound,
            AbnormalPlayerUids = new List<string> { state.PlayerUid },
            ExpectedWaitCount = expectedWaitCount,
            CreatedTime = DateTime.UtcNow,
            ExpiryTime = DateTime.UtcNow.AddMinutes(5)
        };
    }

    /// <summary>
    /// 静态工厂方法：从多个异常玩家状态合并创建统一等待点
    /// 采用"最落后玩家"原则：选择路线索引最小的等待点
    /// </summary>
    public static UnifiedWaitPoint? FromAbnormalPlayerStates(IEnumerable<AbnormalPlayerState> states, Func<string, int> getRouteIndex, int expectedWaitCount)
    {
        var validStates = states.Where(s => s.IsActive()).ToList();
        if (validStates.Count == 0)
            return null;

        // 按路线索引排序，取最落后的（索引最小的）
        var targetState = validStates
            .OrderBy(s => getRouteIndex(s.RouteId))
            .First();

        var unified = FromAbnormalPlayerState(targetState, expectedWaitCount);

        // 合并所有异常玩家UID
        foreach (var state in validStates)
        {
            unified.AddAbnormalPlayer(state.PlayerUid);
        }

        return unified;
    }
}
