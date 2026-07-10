#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.GameTask.AutoSwitchRoles;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 联机锄地「按线路切换角色」的决策/序列化纯函数（无副作用、不依赖外部状态/IO/logger）。
/// 集中承载：线路角色映射的序列化/反序列化、是否需要切换判定、可操作角色数裁剪、
/// 同步点抑制/上报决策、号位动态探测命中/放弃/二号位索引判定。便于属性测试（PBT）。
/// </summary>
public static class PerRouteSwitchRolesDecisions
{
    /// <summary>联机最多号位数（最多 2 个可操作角色）。</summary>
    public const int PositionCount = 2;

    /// <summary>R11.3/R2.4/R2.5：至少一个号位去空白后非空 → 需要切换。entry==null → false。</summary>
    public static bool RouteNeedsSwitch(RouteRoleEntry? entry)
    {
        return entry != null
               && (!string.IsNullOrWhiteSpace(entry.Position1)
                   || !string.IsNullOrWhiteSpace(entry.Position2));
    }

    /// <summary>
    /// R2.6/R2.4：映射 → 可写入 Group_Settings 的纯数据结构（List of Dictionary）。
    /// 仅写入 RouteNeedsSwitch 为真的条目（全空条目归一化丢弃）。
    /// </summary>
    public static List<Dictionary<string, object?>> SerializeRoutes(IReadOnlyDictionary<string, RouteRoleEntry> map)
    {
        var list = new List<Dictionary<string, object?>>();
        if (map == null) return list;
        foreach (var kv in map)
        {
            var entry = kv.Value;
            if (!RouteNeedsSwitch(entry)) continue;
            list.Add(new Dictionary<string, object?>
            {
                ["routeId"] = string.IsNullOrEmpty(entry.RouteId) ? kv.Key : entry.RouteId,
                ["position1"] = entry.Position1 ?? "",
                ["position2"] = entry.Position2 ?? "",
            });
        }
        return list;
    }

    /// <summary>
    /// R2.6/R3：原始 object?（JsonElement 数组 / IEnumerable）→ 映射（routeId → entry）。
    /// null / 非数组 / 损坏 / 缺字段 → 空或仅含合法条目的映射，绝不抛异常。全空/无 routeId 条目跳过。
    /// </summary>
    public static Dictionary<string, RouteRoleEntry> ParseRoutes(object? raw)
    {
        var map = new Dictionary<string, RouteRoleEntry>(StringComparer.Ordinal);
        try
        {
            if (raw is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in je.EnumerateArray())
                    {
                        AddEntry(map, ParseEntryFromJsonElement(item));
                    }
                }
            }
            else if (raw is IEnumerable enumerable && raw is not string)
            {
                foreach (var item in enumerable)
                {
                    AddEntry(map, ParseEntryFromObject(item));
                }
            }
        }
        catch
        {
            // 任何解析异常都回退为空映射（可恢复：旧/损坏配置不应阻断启动或弹窗）。
            map.Clear();
        }
        return map;
    }

    private static void AddEntry(Dictionary<string, RouteRoleEntry> map, RouteRoleEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.RouteId)) return;
        if (!RouteNeedsSwitch(entry)) return;   // 全空条目跳过（R2.4）
        map[entry.RouteId] = entry;             // 同 routeId 后者覆盖前者
    }

    private static RouteRoleEntry? ParseEntryFromJsonElement(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        return new RouteRoleEntry
        {
            RouteId = GetJsonString(obj, "routeId", ""),
            Position1 = GetJsonString(obj, "position1", ""),
            Position2 = GetJsonString(obj, "position2", ""),
        };
    }

    private static RouteRoleEntry? ParseEntryFromObject(object? item)
    {
        if (item is JsonElement je) return ParseEntryFromJsonElement(je);
        if (item is RouteRoleEntry existing) return existing;
        if (item is IDictionary<string, object?> dict)
        {
            return new RouteRoleEntry
            {
                RouteId = GetDictString(dict, "routeId", ""),
                Position1 = GetDictString(dict, "position1", ""),
                Position2 = GetDictString(dict, "position2", ""),
            };
        }
        return null;
    }

    private static string GetJsonString(JsonElement obj, string name, string fallback)
    {
        if (obj.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString() ?? fallback;
            if (p.ValueKind == JsonValueKind.Number) return p.ToString();
        }
        return fallback;
    }

    private static string GetDictString(IDictionary<string, object?> dict, string key, string fallback)
    {
        return dict.TryGetValue(key, out var v) && v != null ? v.ToString() ?? fallback : fallback;
    }

    /// <summary>
    /// R9.2/R9.6/R11.2：裁剪到可操作数。返回长度 = min(targets.Count, max(operableCount,0))，逐位置保留原值（含 null）。
    /// 幂等（R11.1）、operableCount≥Count 时恒等（R11.2）。
    /// </summary>
    public static IReadOnlyList<string?> ClampTargetsToOperableCount(IReadOnlyList<string?> targets, int operableCount)
    {
        if (targets == null) return Array.Empty<string?>();
        var k = Math.Clamp(operableCount, 0, targets.Count);
        return targets.Take(k).ToList();
    }

    /// <summary>R10.1/R10.2/R10.6：是否抑制上报 = routeHasSwitch &amp;&amp; isFirstTeleportSyncPoint。</summary>
    public static bool ShouldSuppressReport(bool routeHasSwitch, bool isFirstTeleportSyncPoint)
        => routeHasSwitch && isFirstTeleportSyncPoint;

    /// <summary>R10/OQ-8：抑制条件（routeHasSwitch &amp;&amp; isFirstTeleport）返回 null（不抢报），否则恒等返回 baseFastSyncId。</summary>
    public static string? ResolveFastSyncIdForWaypoint(string? baseFastSyncId, bool routeHasSwitch, bool isFirstTeleport)
        => (routeHasSwitch && isFirstTeleport) ? null : baseFastSyncId;

    /// <summary>
    /// R11.4/R9.4：把 entry 的两号位经别名表解析为目标角色名列表（长度 PositionCount）。
    /// 复用 AutoSwitchRolesDecisions.ResolvePosition（空白→null、命中→正式名、未命中→trim 原值）。
    /// </summary>
    public static IReadOnlyList<string?> ResolveEntryTargets(RouteRoleEntry entry, IReadOnlyDictionary<string, string> aliasMap)
    {
        return new[]
        {
            AutoSwitchRolesDecisions.ResolvePosition(entry?.Position1, aliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(entry?.Position2, aliasMap),
        };
    }

    /// <summary>
    /// R7.4（候选 #9）：号位命中判定（动态探测）。
    /// 点击候选格子前 MapCloseButton 存在、点后消失 = 进入角色选择页 = 命中我的号位。
    /// </summary>
    public static bool IsMyPositionDetected(bool mapCloseVisibleBefore, bool mapCloseVisibleAfter)
        => mapCloseVisibleBefore && !mapCloseVisibleAfter;

    /// <summary>
    /// R7.6/R7.7（候选 #10）：探测放弃判定。
    /// 本轮全候选未命中 且 已用重试轮数达上限 → 放弃本次切换。
    /// </summary>
    public static bool ShouldGiveUpProbing(int retriesUsed, bool allCandidatesMissedThisRound, int maxRetries = 2)
        => allCandidatesMissedThisRound && retriesUsed >= maxRetries;

    /// <summary>
    /// R7.5（候选 #11）：1 号位命中索引 → 2 号位候选索引。
    /// firstHitIndex+1 &lt; candidateCount → 返回 firstHitIndex+1；越界/firstHitIndex&lt;0 → null（无 2 号位右邻）。
    /// </summary>
    public static int? Resolve2ndPositionCandidateIndex(int firstHitIndex, int candidateCount = 4)
        => (firstHitIndex >= 0 && firstHitIndex + 1 < candidateCount) ? firstHitIndex + 1 : (int?)null;

    /// <summary>R4.1/R4.3（Property 8）：每线路一次性触发判定 = routeHasSwitch &amp;&amp; isTeleportWaypoint &amp;&amp; !alreadyDone。</summary>
    public static bool IsFirstTeleportTrigger(bool routeHasSwitch, bool isTeleportWaypoint, bool alreadyDone)
        => routeHasSwitch && isTeleportWaypoint && !alreadyDone;
}
