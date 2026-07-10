using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 路线选择与优化引擎：效率计算 → 两轮选择 → 迭代优化 → 贪心逆筛 → 排序
/// </summary>
public class RouteSelector
{
    private static readonly ILogger Logger = App.GetLogger<RouteSelector>();

    public RouteSelectorResult SelectOptimalRoutes(
        List<RouteInfo> routes,
        double efficiencyIndex,
        int targetEliteNum,
        int targetMonsterNum,
        string sortMode)
    {
        Logger.LogInformation("开始根据配置寻找路线组合");

        // 0. 计算效率指数
        CalculateEfficiency(routes, efficiencyIndex);

        // 1-2. 两轮选择 + 迭代优化
        var result = IterativeSelection(routes, targetEliteNum, targetMonsterNum);

        // 3. 贪心逆筛
        GreedyPrune(routes, ref result, targetEliteNum, targetMonsterNum);

        // 4. 小怪标签更新
        UpdateMonsterTags(routes);

        // 5. 排序
        SortRoutes(routes, sortMode);

        LogResult(result);
        return result;
    }

    private static void CalculateEfficiency(List<RouteInfo> routes, double efficiencyIndex)
    {
        double maxE1 = double.NegativeInfinity, maxE2 = double.NegativeInfinity;
        double minE1 = double.PositiveInfinity, minE2 = double.PositiveInfinity;

        foreach (var p in routes)
        {
            p.Selected = false;
            p.E1 = p.EliteMonsterCount != 0
                ? (efficiencyIndex * p.EliteMoraGain - p.AdjustedTime) / p.EliteMonsterCount
                : double.NaN;
            p.E2 = p.NormalMonsterCount != 0
                ? (efficiencyIndex * p.NormalMoraGain - p.AdjustedTime) / p.NormalMonsterCount
                : double.NaN;

            if (p.EliteMonsterCount != 0)
            {
                maxE1 = Math.Max(maxE1, p.E1);
                minE1 = Math.Min(minE1, p.E1);
            }
            if (p.NormalMonsterCount != 0)
            {
                maxE2 = Math.Max(maxE2, p.E2);
                minE2 = Math.Min(minE2, p.E2);
            }
        }

        // 处理无精英/无小怪的路线
        if (double.IsPositiveInfinity(minE1)) minE1 = 0;
        if (double.IsPositiveInfinity(minE2)) minE2 = 0;

        foreach (var p in routes)
        {
            if (p.EliteMonsterCount == 0) p.E1 = minE1 - 1;
            if (p.NormalMonsterCount == 0) p.E2 = minE2 - 1;
            if (p.Prioritized)
            {
                p.E1 += (maxE1 - minE1 + 2);
                p.E2 += (maxE2 - minE2 + 2);
            }
        }
    }

    private static RouteSelectorResult IterativeSelection(
        List<RouteInfo> routes, int targetEliteNum, int targetMonsterNum)
    {
        int nextTargetEliteNum = targetEliteNum;
        var result = new RouteSelectorResult();

        for (int iter = 0; iter < 100; iter++)
        {
            // 第一轮：按E1降序选精英路线
            foreach (var p in routes) p.Selected = false;
            result = new RouteSelectorResult();

            foreach (var p in routes.OrderByDescending(r => r.E1))
            {
                if (p.EliteMonsterCount > 0 && p.Available
                    && result.TotalElites + p.EliteMonsterCount <= nextTargetEliteNum + 2)
                {
                    p.Selected = true;
                    result.TotalElites += p.EliteMonsterCount;
                    result.TotalMonsters += p.NormalMonsterCount;
                    result.TotalGain += p.EliteMoraGain + p.NormalMoraGain;
                    result.TotalTime += p.AdjustedTime;
                }
            }

            // 第二轮：按E2降序补选小怪路线
            foreach (var p in routes.OrderByDescending(r => r.E2))
            {
                if (p.NormalMonsterCount > 0 && p.Available && !p.Selected
                    && result.TotalMonsters + p.NormalMonsterCount < targetMonsterNum + 5)
                {
                    p.Selected = true;
                    result.TotalElites += p.EliteMonsterCount;
                    result.TotalMonsters += p.NormalMonsterCount;
                    result.TotalGain += p.NormalMoraGain;
                    result.TotalTime += p.AdjustedTime;
                }
            }

            if (result.TotalElites >= targetEliteNum && result.TotalMonsters >= targetMonsterNum)
                break;

            var eliteGap = targetEliteNum - result.TotalElites;
            nextTargetEliteNum += (int)Math.Round(0.7 * eliteGap);
        }

        return result;
    }

    private static void GreedyPrune(
        List<RouteInfo> routes, ref RouteSelectorResult result,
        int targetEliteNum, int targetMonsterNum)
    {
        var candidates = routes
            .Where(p => p.Selected && !p.Prioritized && !p.Tags.Contains("精英高收益"))
            .OrderBy(p => p.E1).ThenBy(p => p.E2)
            .ToList();

        foreach (var p in candidates)
        {
            var newE = result.TotalElites - p.EliteMonsterCount;
            var newM = result.TotalMonsters - p.NormalMonsterCount;
            if (newE >= targetEliteNum && newM >= targetMonsterNum)
            {
                p.Selected = false;
                result.TotalElites = newE;
                result.TotalMonsters = newM;
                result.TotalGain -= p.EliteMoraGain + p.NormalMoraGain;
                result.TotalTime -= p.AdjustedTime;
            }
        }
    }

    private static void UpdateMonsterTags(List<RouteInfo> routes)
    {
        foreach (var p in routes)
        {
            p.Tags.Remove("小怪");
            if (p.Selected && p.EliteMonsterCount == 0
                && !p.Tags.Contains("传奇") && !p.Tags.Contains("高危"))
            {
                p.Tags.Add("小怪");
            }
        }
    }

    private static void SortRoutes(List<RouteInfo> routes, string sortMode)
    {
        switch (sortMode)
        {
            case "效率降序":
                Logger.LogInformation("使用效率降序运行");
                routes.Sort((a, b) =>
                {
                    var cmp = b.E1.CompareTo(a.E1);
                    return cmp != 0 ? cmp : b.E2.CompareTo(a.E2);
                });
                break;
            case "高收益优先":
                Logger.LogInformation("使用高收益优先运行");
                routes.Sort((a, b) =>
                {
                    var aHigh = a.Tags.Contains("高收益") ? 1 : 0;
                    var bHigh = b.Tags.Contains("高收益") ? 1 : 0;
                    var cmp = bHigh.CompareTo(aHigh);
                    return cmp != 0 ? cmp : string.Compare(a.FileName, b.FileName, StringComparison.Ordinal);
                });
                break;
            default:
                Logger.LogInformation("使用原文件顺序运行");
                routes.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.Ordinal));
                break;
        }
    }

    private static void LogResult(RouteSelectorResult result)
    {
        var ts = TimeSpan.FromSeconds(result.TotalTime);
        Logger.LogInformation("路线组合结果：精英 {E}, 小怪 {M}, 收益 {G} 摩拉, 预计用时 {H}时{Min}分{S}秒",
            result.TotalElites, result.TotalMonsters,
            result.TotalGain.ToString("F0"),
            (int)ts.TotalHours, ts.Minutes, ts.Seconds);
    }
}
