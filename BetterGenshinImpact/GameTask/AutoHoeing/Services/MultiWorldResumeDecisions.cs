using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 重开续跑决策（纯函数，PBT 友好）：由本地进度 + 开关状态算出"要上报给服务端裁剪的 Completed_Host_Uids"。
/// 无外部依赖，不持有 client/logger。
/// hoeing-multiworld-host-restart-resume-round Req 1 / 2 / 5。
/// </summary>
public static class MultiWorldResumeDecisions
{
    /// <summary>
    /// 计算要上报的已完成房主 UID 集合：
    ///   - resumeEnabled == false（开关关）→ 空集合（全量序列，回退现状）。
    ///   - progress 为 null → 空集合。
    ///   - progress.OrderSignature != currentSignature（数据不可信：玩家集合已变）→ 空集合（Req 2.4）。
    ///   - 否则 → progress.CompletedHostUids 去空去重。
    /// </summary>
    public static IReadOnlySet<string> ComputeCompletedHostUids(
        MultiWorldProgress? progress, bool resumeEnabled, string currentSignature)
    {
        var empty = new HashSet<string>();
        if (!resumeEnabled) return empty;
        if (progress == null) return empty;
        if (progress.OrderSignature != currentSignature) return empty;
        return new HashSet<string>(
            progress.CompletedHostUids.Where(u => !string.IsNullOrEmpty(u)));
    }

    /// <summary>轮换序列签名：全体房主 UID 升序拼接（Ordinal）。判定进度是否仍对应当前这组玩家。</summary>
    public static string ComputeOrderSignature(IEnumerable<string> hostUids)
    {
        var sorted = (hostUids ?? Enumerable.Empty<string>())
            .Where(u => !string.IsNullOrEmpty(u))
            .Distinct()
            .OrderBy(u => u, System.StringComparer.Ordinal);
        return string.Join("|", sorted);
    }
}
