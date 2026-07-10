#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 异常状态管理器：跟踪玩家的异常状态（如掉线、战斗失败等），
/// 用于协调等待和跳过逻辑。
/// </summary>
public class AbnormalStatusManager
{
    private readonly ILogger<AbnormalStatusManager> _logger = App.GetLogger<AbnormalStatusManager>();
    private readonly MultiplayerCoordinator _coordinator;
    private readonly WaitPointStateManager _stateManager;
    private readonly AutoHoeingConfig _config;
    
    private readonly ConcurrentDictionary<string, AbnormalStatusInfo> _abnormalStatuses = new();

    public AbnormalStatusManager(MultiplayerCoordinator coordinator, WaitPointStateManager stateManager, AutoHoeingConfig config)
    {
        _coordinator = coordinator;
        _stateManager = stateManager;
        _config = config;
    }

    /// <summary>
    /// 标记玩家为异常状态
    /// </summary>
    public void MarkAbnormal(string playerUid, string routeId, string syncPointId, string reason)
    {
        if (string.IsNullOrEmpty(playerUid)) return;

        var info = new AbnormalStatusInfo
        {
            PlayerUid = playerUid,
            RouteId = routeId,
            SyncPointId = syncPointId,
            Reason = reason,
            MarkedTime = DateTime.Now,
            IsCleared = false
        };

        _abnormalStatuses.AddOrUpdate(playerUid, info, (_, _) => info);
        _stateManager.MarkAbnormal(playerUid, routeId, syncPointId, reason);
        
        _logger.LogWarning("[异常状态] 标记玩家异常: {Uid}, 路线={RouteId}, 同步点={SyncId}, 原因={Reason}",
            playerUid, routeId, syncPointId, reason);
    }

    /// <summary>
    /// 清除玩家异常状态
    /// </summary>
    public bool ClearAbnormalStatus(string playerUid, string reason)
    {
        if (_abnormalStatuses.TryGetValue(playerUid, out var info))
        {
            info.IsCleared = true;
            _abnormalStatuses.TryRemove(playerUid, out _);
            _stateManager.ClearAbnormal(playerUid, reason);
            
            _logger.LogInformation("[异常状态] 清除玩家异常: {Uid}, 原因={Reason}", playerUid, reason);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 清除所有玩家异常状态
    /// </summary>
    public void ClearAllAbnormalStatuses(string reason)
    {
        var uids = _abnormalStatuses.Keys.ToList();
        foreach (var uid in uids)
        {
            ClearAbnormalStatus(uid, reason);
        }
        _logger.LogInformation("[异常状态] 清除所有异常状态, 原因={Reason}", reason);
    }

    /// <summary>
    /// 检查玩家是否处于异常状态
    /// </summary>
    public bool IsAbnormal(string playerUid)
    {
        return _abnormalStatuses.ContainsKey(playerUid);
    }

    /// <summary>
    /// 获取所有异常玩家 UID 列表
    /// </summary>
    public List<string> GetAllAbnormalPlayerUids()
    {
        return _abnormalStatuses.Keys.ToList();
    }

    /// <summary>
    /// 重置状态
    /// </summary>
    public void Reset()
    {
        ClearAllAbnormalStatuses("重置");
        _logger.LogInformation("[异常状态] 已重置");
    }

    /// <summary>
    /// 获取统计信息（透传到 WaitPointStateManager）
    /// </summary>
    public WaitPointStats GetStatistics()
    {
        return _stateManager.GetStatistics();
    }
}

/// <summary>
/// 异常状态信息
/// </summary>
public class AbnormalStatusInfo
{
    public string PlayerUid { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string SyncPointId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime MarkedTime { get; set; }
    public bool IsCleared { get; set; }
}
