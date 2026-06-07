namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 多世界轮换"空轮预跳过"判定（纯函数，PBT 友好，无外部依赖）。
/// multiplayer-host-empty-round-preskip-before-world-join。
///
/// 与 RunSingleWorldCoreAsync 步骤1 的 CD 过滤规则保持完全一致：
///   StartRouteIndex > 0 || !IsOnCooldown(route)
/// 使"进世界前预判"与"进世界后步骤1判空"对同一输入产生一致结论。
/// </summary>
public static class EmptyRoundPreSkipDecisions
{
    /// <summary>
    /// 计算房主本轮经 CD 过滤后的待跑线路文件名集合（与步骤1 SetHostRouteList 上传内容一致）。
    /// </summary>
    /// <param name="routeFileNames">本轮分组过滤后（Group==targetGroup && Selected）的线路文件名，顺序保留。</param>
    /// <param name="startRouteIndex">_config.StartRouteIndex；>0 时旁路 CD 过滤。</param>
    /// <param name="isOnCooldown">线路文件名 → 是否在 CD 的委托。</param>
    public static List<string> FilterHostRouteSet(
        IEnumerable<string> routeFileNames, int startRouteIndex, Func<string, bool> isOnCooldown)
    {
        if (routeFileNames == null) return new List<string>();
        if (startRouteIndex > 0)
        {
            // 与步骤1一致：StartRouteIndex>0 旁路 CD，全部线路计入。
            return routeFileNames.ToList();
        }
        var cd = isOnCooldown ?? (_ => false);
        return routeFileNames.Where(name => !cd(name)).ToList();
    }

    /// <summary>
    /// 本轮房主是否为 Empty_Round（CD 过滤后无可跑线路）。
    /// </summary>
    public static bool IsEmptyRound(
        IEnumerable<string> routeFileNames, int startRouteIndex, Func<string, bool> isOnCooldown)
        => FilterHostRouteSet(routeFileNames, startRouteIndex, isOnCooldown).Count == 0;
}
