using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

/// <summary>
/// 纯会话级（进程内、不持久化）兑换码内存缓存。
/// </summary>
/// <remarks>
/// 本类不再承担任何跨进程持久化职责；
/// "已成功兑换" 的跨进程持久化由 <c>AutoRedeemCodeConfig.UsedCodesByUid</c> 独立提供。
/// 这里维护两份会话级集合：
/// <list type="bullet">
/// <item>会话内成功 short-circuit（不区分 UID，作为剪贴板路径与同会话内重兑的兜底）。</item>
/// <item>会话内失败 short-circuit（沿用 1 天 TTL，避免本会话内对同一已知失败码反复尝试）。</item>
/// </list>
/// 进程退出即清空，不写盘。
/// </remarks>
public class RedeemCodeCache
{
    private static readonly ILogger _logger = App.GetLogger<RedeemCodeCache>();

    /// <summary>
    /// 本会话内已成功兑换的兑换码缓存 (code -> 记录时间)。不区分 UID，不持久化。
    /// </summary>
    private static readonly Dictionary<string, DateTime> _succeededCodesInSession = new();

    /// <summary>
    /// 过期或失败的兑换码缓存 (code -> 过期日期)。会话级，不持久化。
    /// </summary>
    private static readonly Dictionary<string, DateTime> _failedCodes = new();

    private static readonly TimeSpan _succeededInSessionExpiration = TimeSpan.FromDays(30);
    private static readonly TimeSpan _failedCodeExpiration = TimeSpan.FromDays(1);

    /// <summary>
    /// 检查兑换码在本会话内是否已成功兑换过（不区分 UID）。
    /// </summary>
    public static bool IsRecentlySucceededInSession(string code)
    {
        CleanExpiredEntries();
        return _succeededCodesInSession.ContainsKey(code);
    }

    /// <summary>
    /// 检查兑换码在本会话内是否最近失败过（已过期或服务器拒绝）。
    /// </summary>
    public static bool IsRecentlyFailed(string code)
    {
        CleanExpiredEntries();
        return _failedCodes.ContainsKey(code);
    }

    /// <summary>
    /// 记录本会话内兑换成功的码（不区分 UID，不持久化）。
    /// </summary>
    public static void MarkAsSucceededInSession(string code)
    {
        _succeededCodesInSession[code] = DateTime.Now;
        _logger.LogDebug("兑换码 {Code} 已标记为本会话已成功兑换", code);
    }

    /// <summary>
    /// 记录兑换失败的码（已过期或服务器拒绝）。
    /// </summary>
    public static void MarkAsFailed(string code, DateTime? expireDate = null)
    {
        // 注意：不再使用 expireDate 作为缓存过期时间，避免过期日期早于当前日期时
        // 缓存立即失效的问题。统一使用 _failedCodeExpiration（1天）作为缓存有效期，
        // 确保当天已知失败的码不会重复尝试。
        _failedCodes[code] = DateTime.Now.Add(_failedCodeExpiration);
        _logger.LogDebug("兑换码 {Code} 已标记为失败/过期（兑换码有效期：{ExpireDate}）", code, expireDate?.ToString("yyyy-MM-dd") ?? "未知");
    }

    /// <summary>
    /// 清理过期的缓存条目
    /// </summary>
    private static void CleanExpiredEntries()
    {
        var now = DateTime.Now;

        // 清理会话内成功缓存（30天后清理）
        var succeededToRemove = new List<string>();
        foreach (var kvp in _succeededCodesInSession)
        {
            if (now - kvp.Value > _succeededInSessionExpiration)
            {
                succeededToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in succeededToRemove)
        {
            _succeededCodesInSession.Remove(key);
        }

        // 清理失败缓存（根据缓存的过期时间清理）
        var failedToRemove = new List<string>();
        foreach (var kvp in _failedCodes)
        {
            if (now > kvp.Value)
            {
                failedToRemove.Add(kvp.Key);
            }
        }
        foreach (var key in failedToRemove)
        {
            _failedCodes.Remove(key);
        }
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public static void Clear()
    {
        _succeededCodesInSession.Clear();
        _failedCodes.Clear();
        _logger.LogInformation("兑换码缓存已清空");
    }
}
