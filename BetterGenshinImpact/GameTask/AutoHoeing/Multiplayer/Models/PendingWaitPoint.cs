#nullable enable

using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 服务端指令的待处理等待点（multiplayer-abnormal-wait-coordination）
/// 用于存储服务端广播的统一等待点信息，指导客户端在哪里等待异常玩家
/// </summary>
public class PendingWaitPoint
{
    /// <summary>同步点ID（格式：{routeId}_tp_{listIdx}_{wpIdx}）</summary>
    public string SyncPointId { get; set; } = string.Empty;
    
    /// <summary>路线ID</summary>
    public string RouteId { get; set; } = string.Empty;
    
    /// <summary>异常玩家UID列表</summary>
    public List<string> AbnormalPlayerUids { get; set; } = new();
    
    /// <summary>预期等待人数（正常玩家 + 异常玩家）</summary>
    public int ExpectedWaitCount { get; set; }
    
    /// <summary>收到广播的时间</summary>
    public DateTime ReceivedTime { get; set; } = DateTime.UtcNow;
    
    /// <summary>是否已被处理</summary>
    public bool IsProcessed { get; set; }
    
    /// <summary>是否强制等待（服务端指令的等待点，不依赖 SyncAtEveryTeleport 配置）</summary>
    public bool IsForced { get; set; } = true;
    
    /// <summary>
    /// 检查等待点是否过期（超过10分钟视为过期）
    /// </summary>
    public bool IsExpired(TimeSpan? expiry = null)
    {
        var expiryTime = expiry ?? TimeSpan.FromMinutes(10);
        return DateTime.UtcNow - ReceivedTime > expiryTime;
    }
    
    /// <summary>
    /// 验证同步点是否是传送点格式（包含 _tp_）
    /// </summary>
    public bool IsValidTeleportPoint()
    {
        return !string.IsNullOrEmpty(SyncPointId) && SyncPointId.Contains("_tp_");
    }
    
    public override string ToString()
    {
        return $"PendingWaitPoint[SyncPoint={SyncPointId}, Route={RouteId}, AbnormalPlayers={AbnormalPlayerUids.Count}, Expected={ExpectedWaitCount}, Forced={IsForced}]";
    }
}
