#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 等待点状态管理器：跟踪每个玩家在路线中的等待点状态。
/// </summary>
public class WaitPointStateManager : IDisposable
{
    private readonly ILogger<WaitPointStateManager> _logger = App.GetLogger<WaitPointStateManager>();
    private readonly ConcurrentDictionary<string, WaitPointState> _states = new();
    private readonly ConcurrentDictionary<string, WaitPointState> _abnormalStates = new();
    private int _currentWorldRound = 1;
    private bool _disposed;

    /// <summary>
    /// 设置当前世界轮次
    /// </summary>
    public void SetWorldRound(int round)
    {
        _currentWorldRound = round;
        _logger.LogDebug("[等待点] 设置世界轮次: {Round}", round);
    }

    /// <summary>
    /// 更新玩家状态
    /// </summary>
    public void UpdateState(string playerUid, WaitPointState state)
    {
        if (string.IsNullOrEmpty(playerUid)) return;
        _states.AddOrUpdate(playerUid, state, (_, _) => state);
        _logger.LogDebug("[等待点] 更新玩家状态: {Uid} -> {SyncPointId} (轮次:{Round})",
            playerUid, state.SyncPointId, state.WorldRound);
    }

    /// <summary>
    /// 获取玩家状态
    /// </summary>
    public WaitPointState? GetState(string playerUid)
    {
        return _states.TryGetValue(playerUid, out var state) ? state : null;
    }

    /// <summary>
    /// 移除玩家状态
    /// </summary>
    public void RemoveState(string playerUid)
    {
        _states.TryRemove(playerUid, out _);
        _logger.LogDebug("[等待点] 移除玩家状态: {Uid}", playerUid);
    }

    /// <summary>
    /// 获取所有有效状态（当前轮次）
    /// </summary>
    public List<WaitPointState> GetAllValidStates()
    {
        return _states.Values
            .Where(s => s.WorldRound == _currentWorldRound)
            .OrderBy(s => s.LastUpdated)
            .ToList();
    }

    /// <summary>
    /// 获取在指定同步点异常的玩家数量
    /// </summary>
    public int GetAbnormalPlayersAtPoint(string syncPointId)
    {
        return _abnormalStates.Values.Count(s => s.SyncPointId == syncPointId && s.WorldRound == _currentWorldRound);
    }

    /// <summary>
    /// 获取异常玩家总数
    /// </summary>
    public int GetAbnormalPlayerCount()
    {
        return _abnormalStates.Values.Count(s => s.WorldRound == _currentWorldRound);
    }

    /// <summary>
    /// 检查玩家是否处于异常状态
    /// </summary>
    public bool IsAbnormalPlayer(string playerUid)
    {
        return _abnormalStates.ContainsKey(playerUid);
    }

    /// <summary>
    /// 标记玩家为异常状态
    /// </summary>
    public void MarkAbnormal(string playerUid, string routeId, string syncPointId, string reason)
    {
        if (string.IsNullOrEmpty(playerUid)) return;
        
        var state = new WaitPointState
        {
            PlayerUid = playerUid,
            RouteId = routeId,
            SyncPointId = syncPointId,
            WorldRound = _currentWorldRound,
            LastUpdated = DateTime.Now
        };
        
        _abnormalStates.AddOrUpdate(playerUid, state, (_, _) => state);
        _logger.LogWarning("[等待点] 标记玩家异常: {Uid}, 路线={RouteId}, 同步点={SyncId}, 原因={Reason}",
            playerUid, routeId, syncPointId, reason);
    }

    /// <summary>
    /// 清除玩家异常状态
    /// </summary>
    public bool ClearAbnormal(string playerUid, string reason)
    {
        if (_abnormalStates.TryRemove(playerUid, out var state))
        {
            _logger.LogInformation("[等待点] 清除玩家异常状态: {Uid}, 原因={Reason}", playerUid, reason);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 重置当前轮次状态
    /// </summary>
    public void ResetCurrentRound()
    {
        _states.Clear();
        _abnormalStates.Clear();
        _logger.LogInformation("[等待点] 重置当前轮次状态");
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public WaitPointStats GetStatistics()
    {
        return new WaitPointStats
        {
            CurrentWorldRound = _currentWorldRound,
            TotalPlayers = _states.Count,
            AbnormalPlayers = _abnormalStates.Count,
            ValidStates = GetAllValidStates()
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _states.Clear();
        _abnormalStates.Clear();
    }
}

/// <summary>
/// 等待点状态
/// </summary>
public class WaitPointState
{
    public string PlayerUid { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string SyncPointId { get; set; } = "";
    public int WorldRound { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// 等待点统计信息
/// </summary>
public class WaitPointStats
{
    public int CurrentWorldRound { get; set; } = 1;
    public int TotalPlayers { get; set; }
    public int AbnormalPlayers { get; set; }
    public List<WaitPointState> ValidStates { get; set; } = new();
}
