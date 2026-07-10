namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 多世界轮次日志上下文：把 RunMultiWorldAsync 主循环的轮次信息透传到
/// ProcessRoutesByGroup 的两条核心日志前缀。字段全部不可变（init-only），
/// 构造后不应被改写。
/// 详见 .kiro/specs/multiplayer-route-log-round-prefix/design.md §2.1
/// </summary>
public sealed class MultiWorldRoundContext
{
    /// <summary>1-based 当前轮次（与 [多世界] 第 R/Total 轮 日志中的 R 一致）</summary>
    public int Round1Based { get; init; }

    /// <summary>本次多世界的总轮数（已经是 Math.Min(MultiWorldCount, playerOrder.Count)）</summary>
    public int TotalRounds { get; init; }

    /// <summary>本轮房主玩家名（可为 null 或空字符串，前缀会优雅降级）</summary>
    public string? HostPlayerName { get; init; }
}

/// <summary>
/// 多世界路线日志前缀生成器：纯函数，PBT 友好。
/// </summary>
public static class MultiWorldRoundLogPrefix
{
    /// <summary>
    /// 计算路线日志前缀。返回值末尾必带一个空格（如非空），方便和原文直接拼接。
    /// </summary>
    /// <param name="ctx">
    /// 多世界轮次上下文。null 表示非多世界场景（单机 / 单世界联机 / 多世界兜底单轮分支），返回空字符串。
    /// </param>
    public static string Format(MultiWorldRoundContext? ctx)
    {
        if (ctx == null) return string.Empty;
        if (ctx.TotalRounds <= 0) return string.Empty;
        if (ctx.Round1Based <= 0) return string.Empty;

        var host = ctx.HostPlayerName;
        if (string.IsNullOrEmpty(host))
        {
            return $"[第 {ctx.Round1Based}/{ctx.TotalRounds} 轮] ";
        }
        // 净化换行：日志前缀必须保持单行，避免恶意/异常玩家名破坏日志结构
        var sanitizedHost = host.Replace('\r', ' ').Replace('\n', ' ');
        return $"[第 {ctx.Round1Based}/{ctx.TotalRounds} 轮 {sanitizedHost}] ";
    }
}
