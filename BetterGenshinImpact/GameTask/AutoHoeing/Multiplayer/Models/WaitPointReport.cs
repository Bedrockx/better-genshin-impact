#nullable enable

using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 等待点上报信息（skip-route-wait-point-report spec）
/// 当玩家跳过线路并在同步点等待时，上报其等待点位置
/// </summary>
public class WaitPointReport
{
    /// <summary>
    /// 玩家UID
    /// </summary>
    public string PlayerUid { get; set; } = string.Empty;

    /// <summary>
    /// 路线标识（如路线文件名）
    /// </summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>
    /// 同步点标识（格式：{task.FileName}_{listIdx}_{fightIdx}）
    /// </summary>
    public string SyncPointId { get; set; } = string.Empty;

    /// <summary>
    /// 多轮世界轮次（防止跨轮状态污染）
    /// </summary>
    public int WorldRound { get; set; }

    /// <summary>
    /// 上报时间
    /// </summary>
    public DateTime ReportedTime { get; set; }

    /// <summary>
    /// 过期时间（默认30秒后过期）
    /// </summary>
    public DateTime? ExpiryTime { get; set; }

    /// <summary>
    /// 创建新的等待点上报
    /// </summary>
    public WaitPointReport()
    {
        ReportedTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddSeconds(30); // 默认30秒过期
    }

    /// <summary>
    /// 创建新的等待点上报（带参数）
    /// </summary>
    public WaitPointReport(string playerUid, string routeId, string syncPointId, int worldRound)
    {
        PlayerUid = playerUid;
        RouteId = routeId;
        SyncPointId = syncPointId;
        WorldRound = worldRound;
        ReportedTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddSeconds(30);
    }

    /// <summary>
    /// 验证等待点是否有效
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrEmpty(PlayerUid)) return false;
        if (string.IsNullOrEmpty(RouteId)) return false;
        if (string.IsNullOrEmpty(SyncPointId)) return false;
        if (WorldRound < 0) return false;
        
        // 检查是否过期
        if (ExpiryTime.HasValue && DateTime.UtcNow > ExpiryTime.Value)
            return false;

        return true;
    }

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    public bool IsExpired()
    {
        return ExpiryTime.HasValue && DateTime.UtcNow > ExpiryTime.Value;
    }

    /// <summary>
    /// 延长过期时间
    /// </summary>
    public void ExtendExpiry(TimeSpan extension)
    {
        if (ExpiryTime.HasValue)
            ExpiryTime = ExpiryTime.Value.Add(extension);
        else
            ExpiryTime = DateTime.UtcNow.Add(extension);
    }

    /// <summary>
    /// 从路线标识中提取路线索引
    /// </summary>
    public int? ExtractRouteIndex()
    {
        try
        {
            // 假设路线标识格式为 "Route_1" 或类似格式
            // 实际实现需要根据项目中的路线标识格式调整
            if (RouteId.Contains("_"))
            {
                var parts = RouteId.Split('_');
                if (parts.Length > 1 && int.TryParse(parts[^1], out var index))
                    return index;
            }
            
            // 尝试从文件名中提取数字
            var numbers = System.Text.RegularExpressions.Regex.Match(RouteId, @"\d+");
            if (numbers.Success && int.TryParse(numbers.Value, out var idx))
                return idx;
                
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 验证同步点标识格式
    /// </summary>
    public bool IsValidSyncPointId()
    {
        // 同步点标识应包含下划线分隔的部分（格式：{task.FileName}_{listIdx}_{fightIdx}，至少4部分）
        return !string.IsNullOrEmpty(SyncPointId) && 
               SyncPointId.Contains("_") &&
               SyncPointId.Split('_').Length >= 4;
    }

    /// <summary>
    /// 转换为字符串表示（用于日志）
    /// </summary>
    public override string ToString()
    {
        return $"WaitPointReport[Player={PlayerUid}, Route={RouteId}, SyncPoint={SyncPointId}, Round={WorldRound}, Reported={ReportedTime:HH:mm:ss}]";
    }

    /// <summary>
    /// 创建深拷贝
    /// </summary>
    public WaitPointReport Clone()
    {
        return new WaitPointReport
        {
            PlayerUid = PlayerUid,
            RouteId = RouteId,
            SyncPointId = SyncPointId,
            WorldRound = WorldRound,
            ReportedTime = ReportedTime,
            ExpiryTime = ExpiryTime
        };
    }
}