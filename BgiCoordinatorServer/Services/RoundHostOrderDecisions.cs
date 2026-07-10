using System.Collections.Generic;
using System.Linq;
using BgiCoordinatorServer.Models;

namespace BgiCoordinatorServer.Services;

/// <summary>
/// 多世界权威轮换序列生成（纯函数，PBT 友好）。
/// multiplayer-server-authoritative-round-order。
///
/// 由首任房主 MarkRoomStarted（首轮全员已在房间）时生成一次，存入 Room.RoundHostOrder，
/// 客户端查询后据此构造 playerOrder，保证所有客户端轮换序列完全一致。
/// </summary>
public static class RoundHostOrderDecisions
{
    /// <summary>
    /// 生成轮换序列（UID 列表）。excludeUids 非空时排除其中所有 UID（重开续跑裁剪）。
    ///   - excludeUids 为 null/空 → 旧行为：首项=hostUid，其余 UID 升序。
    ///   - excludeUids 非空 → 先按旧规则生成完整序列，再过滤掉 excludeUids 中的 UID；
    ///     若 hostUid 也在 excludeUids 中，则它不会置顶（被一并过滤），
    ///     裁剪后首项 = 第一个未被排除的 UID（其余仍按升序）。
    ///   - 若全部 UID 都被排除 → 返回空列表（整场已完成）。
    /// hoeing-multiworld-host-restart-resume-round Req 1.2 / 2.3。
    /// </summary>
    public static List<string> Build(IEnumerable<PlayerInfo> players, string hostUid,
        IReadOnlySet<string>? excludeUids = null)
    {
        var uids = (players ?? Enumerable.Empty<PlayerInfo>())
            .Where(p => p != null && !string.IsNullOrEmpty(p.PlayerUid))
            .Select(p => p.PlayerUid)
            .Distinct()
            .ToList();
        if (uids.Count == 0) return new List<string>();

        var rest = uids.Where(u => u != hostUid)
                       .OrderBy(u => u, System.StringComparer.Ordinal)
                       .ToList();

        var result = new List<string>();
        if (!string.IsNullOrEmpty(hostUid) && uids.Contains(hostUid))
            result.Add(hostUid);
        result.AddRange(rest);

        // 重开续跑裁剪：排除已完成房主 UID。默认 null/空 → 不裁剪（旧行为逐字节不变）。
        if (excludeUids != null && excludeUids.Count > 0)
            result = result.Where(u => !excludeUids.Contains(u)).ToList();

        return result;
    }
}
