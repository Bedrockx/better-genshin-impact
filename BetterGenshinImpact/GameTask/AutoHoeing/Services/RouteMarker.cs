using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 路线标记与过滤：互斥标签、排除关键词、优先关键词
/// </summary>
public static class RouteMarker
{
    public static void MarkRoutes(
        List<RouteInfo> routes,
        List<List<string>> groupTags,
        List<string> priorityTags,
        List<string> excludeTags)
    {
        // 取出第0组并剔除与其他9组重复的标签 → 互斥标签
        var uniqueTags = groupTags[0]
            .Where(tag => !groupTags.Skip(1).Any(arr => arr.Contains(tag)))
            .ToList();

        foreach (var route in routes)
        {
            route.Prioritized = false;

            var containsUniqueTag = uniqueTags.Any(t => route.Tags.Contains(t));

            var containsExcludeTag = excludeTags.Any(ex =>
                (route.FullPath?.Contains(ex) ?? false)
                || route.Tags.Any(tag => tag.Contains(ex))
                || route.MonsterInfo.Keys.Any(name => name.Contains(ex)));

            var containsPriorityTag = priorityTags.Any(pt =>
                (route.FullPath?.Contains(pt) ?? false)
                || route.Tags.Any(tag => tag.Contains(pt))
                || route.MonsterInfo.Keys.Any(name => name.Contains(pt)));

            route.Available = !(containsUniqueTag || containsExcludeTag);
            route.Prioritized = containsPriorityTag;
        }
    }
}
