#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BgiCoordinatorServer.Models;

/// <summary>
/// 协调后的统一等待点（skip-route-wait-point-report spec）
/// 当多个玩家跳过线路时，协调出一个统一的等待点
/// 采用"最落后玩家"原则：所有玩家向最落后的等待点对齐
/// </summary>
public class CoordinatedWaitPoint
{
    /// <summary>
    /// 路线标识
    /// </summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>
    /// 同步点标识
    /// </summary>
    public string SyncPointId { get; set; } = string.Empty;

    /// <summary>
    /// 多轮世界轮次
    /// </summary>
    public int WorldRound { get; set; }

    /// <summary>
    /// 已对齐到该点的玩家列表
    /// </summary>
    public List<string> AlignedPlayers { get; set; } = new();

    /// <summary>
    /// 协调时间
    /// </summary>
    public DateTime CoordinationTime { get; set; }

    /// <summary>
    /// 过期时间（默认60秒后过期）
    /// </summary>
    public DateTime? ExpiryTime { get; set; }

    /// <summary>
    /// 创建新的协调等待点
    /// </summary>
    public CoordinatedWaitPoint()
    {
        CoordinationTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddSeconds(60);
    }

    /// <summary>
    /// 从多个等待点报告中协调出统一等待点
    /// </summary>
    public static CoordinatedWaitPoint? Coordinate(IEnumerable<WaitPointReport> reports)
    {
        var validReports = reports
            .Where(r => r.IsValid() && !r.IsExpired())
            .ToList();

        if (validReports.Count == 0)
            return null;

        // 按路线索引分组（取最落后的路线）
        var routeGroups = validReports
            .GroupBy(r => r.ExtractRouteIndex())
            .Where(g => g.Key.HasValue)
            .OrderBy(g => g.Key!.Value) // 路线索引最小的最落后
            .ToList();

        if (routeGroups.Count == 0)
            return null;

        // 取最落后的路线组
        var targetGroup = routeGroups.First();
        var minRouteIndex = targetGroup.Key!.Value;

        // 在该组内取同步点标识最小的（最保守）
        var targetReport = targetGroup
            .OrderBy(r => r.SyncPointId) // 同步点标识排序
            .First();

        return new CoordinatedWaitPoint
        {
            RouteId = targetReport.RouteId,
            SyncPointId = targetReport.SyncPointId,
            WorldRound = targetReport.WorldRound,
            AlignedPlayers = targetGroup.Select(r => r.PlayerUid).ToList(),
            CoordinationTime = DateTime.UtcNow,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60)
        };
    }

    /// <summary>
    /// 验证协调等待点是否有效
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrEmpty(RouteId)) return false;
        if (string.IsNullOrEmpty(SyncPointId)) return false;
        if (WorldRound < 0) return false;
        if (AlignedPlayers.Count == 0) return false;
        
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
    /// 检查玩家是否已对齐到该点
    /// </summary>
    public bool IsPlayerAligned(string playerUid)
    {
        return AlignedPlayers.Contains(playerUid);
    }

    /// <summary>
    /// 添加对齐玩家
    /// </summary>
    public void AddAlignedPlayer(string playerUid)
    {
        if (!AlignedPlayers.Contains(playerUid))
            AlignedPlayers.Add(playerUid);
    }

    /// <summary>
    /// 移除对齐玩家
    /// </summary>
    public void RemoveAlignedPlayer(string playerUid)
    {
        AlignedPlayers.Remove(playerUid);
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
            if (RouteId.Contains("_"))
            {
                var parts = RouteId.Split('_');
                if (parts.Length > 1 && int.TryParse(parts[^1], out var index))
                    return index;
            }
            
            // 尝试从文件名中提取数字
            var numbers = Regex.Match(RouteId, @"\d+");
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
    /// 转换为字符串表示（用于日志）
    /// </summary>
    public override string ToString()
    {
        return $"CoordinatedWaitPoint[Route={RouteId}, SyncPoint={SyncPointId}, Round={WorldRound}, Players={AlignedPlayers.Count}, Coordinated={CoordinationTime:HH:mm:ss}]";
    }

    /// <summary>
    /// 检查是否匹配指定的同步点
    /// </summary>
    public bool MatchesSyncPoint(string syncPointId)
    {
        return SyncPointId == syncPointId;
    }

    /// <summary>
    /// 创建深拷贝
    /// </summary>
    public CoordinatedWaitPoint Clone()
    {
        return new CoordinatedWaitPoint
        {
            RouteId = RouteId,
            SyncPointId = SyncPointId,
            WorldRound = WorldRound,
            AlignedPlayers = new List<string>(AlignedPlayers),
            CoordinationTime = CoordinationTime,
            ExpiryTime = ExpiryTime
        };
    }
}