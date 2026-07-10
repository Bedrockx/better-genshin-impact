#nullable enable

using System;

namespace BgiCoordinatorServer.Models;

/// <summary>
/// 异常玩家状态（multiplayer-abnormal-wait-coordination spec）
/// 当玩家在执行线路过程中遇到错误状态，触发线路跳过时记录的状态信息
/// Validates: Requirements 1.1, 1.3
/// </summary>
public class AbnormalPlayerState
{
    /// <summary>
    /// 玩家UID
    /// </summary>
    public string PlayerUid { get; set; } = "";

    /// <summary>
    /// 路线标识（如路线文件名）
    /// </summary>
    public string RouteId { get; set; } = "";

    /// <summary>
    /// 等待点标识（必须是 _tp_ 格式的传送点）
    /// </summary>
    public string WaitPointId { get; set; } = "";

    /// <summary>
    /// 当前世界轮次（多轮世界支持）
    /// </summary>
    public int WorldRound { get; set; }

    /// <summary>
    /// 上报时间（UTC）
    /// </summary>
    public DateTime ReportTime { get; set; }

    /// <summary>
    /// 过期时间（5分钟超时，UTC）
    /// </summary>
    public DateTime ExpiryTime { get; set; }

    /// <summary>
    /// 是否已恢复（异常状态已解除）
    /// </summary>
    public bool IsRecovered { get; set; }

    /// <summary>
    /// 创建新的异常玩家状态
    /// </summary>
    public AbnormalPlayerState()
    {
        ReportTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddMinutes(5);
        IsRecovered = false;
    }

    /// <summary>
    /// 创建新的异常玩家状态（带参数）
    /// </summary>
    public AbnormalPlayerState(string playerUid, string routeId, string waitPointId, int worldRound)
    {
        PlayerUid = playerUid;
        RouteId = routeId;
        WaitPointId = waitPointId;
        WorldRound = worldRound;
        ReportTime = DateTime.UtcNow;
        ExpiryTime = DateTime.UtcNow.AddMinutes(5);
        IsRecovered = false;
    }

    /// <summary>
    /// 验证异常状态是否有效
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrEmpty(PlayerUid)) return false;
        if (string.IsNullOrEmpty(RouteId)) return false;
        if (string.IsNullOrEmpty(WaitPointId)) return false;
        if (WorldRound < 0) return false;

        // 检查是否过期
        if (DateTime.UtcNow > ExpiryTime)
            return false;

        return true;
    }

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    public bool IsExpired()
    {
        return DateTime.UtcNow > ExpiryTime;
    }

    /// <summary>
    /// 验证等待点是否为传送点格式（_tp_）
    /// </summary>
    public bool IsValidWaitPoint()
    {
        return !string.IsNullOrEmpty(WaitPointId) && WaitPointId.Contains("_tp_");
    }

    /// <summary>
    /// 检查是否仍在有效期内（未过期且未恢复）
    /// </summary>
    public bool IsActive()
    {
        return !IsExpired() && !IsRecovered;
    }

    /// <summary>
    /// 标记为已恢复
    /// </summary>
    public void MarkAsRecovered()
    {
        IsRecovered = true;
    }

    /// <summary>
    /// 延长过期时间
    /// </summary>
    public void ExtendExpiry(TimeSpan extension)
    {
        ExpiryTime = ExpiryTime.Add(extension);
    }

    /// <summary>
    /// 转换为字符串表示（用于日志）
    /// </summary>
    public override string ToString()
    {
        return $"AbnormalPlayerState[Player={PlayerUid}, Route={RouteId}, WaitPoint={WaitPointId}, Round={WorldRound}, Recovered={IsRecovered}, Expires={ExpiryTime:HH:mm:ss}]";
    }

    /// <summary>
    /// 创建深拷贝
    /// </summary>
    public AbnormalPlayerState Clone()
    {
        return new AbnormalPlayerState
        {
            PlayerUid = PlayerUid,
            RouteId = RouteId,
            WaitPointId = WaitPointId,
            WorldRound = WorldRound,
            ReportTime = ReportTime,
            ExpiryTime = ExpiryTime,
            IsRecovered = IsRecovered
        };
    }
}
