using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 自我优化：根据历史运行记录调整路线预期用时
/// 削峰填谷：7条记录去除一个最大值和一个最小值，取剩余平均值
/// </summary>
public static class SelfOptimizer
{
    /// <summary>
    /// 对路线列表应用自我优化
    /// </summary>
    public static void Apply(List<RouteInfo> routes, bool disabled, double curiosityFactor)
    {
        if (disabled) return;

        var cf = Math.Clamp(curiosityFactor, 0, 1);

        foreach (var route in routes)
        {
            if (route.Records == null || route.Records.Count == 0)
                continue;

            route.AdjustedTime = CalculateAdjustedTime(
                route.Records, route.EstimatedTime, cf);
        }
    }

    /// <summary>
    /// 计算调整后的预期用时
    /// </summary>
    public static double CalculateAdjustedTime(List<double> records, double defaultTime, double curiosityFactor)
    {
        // 过滤有效记录（>0）
        var valid = records.Where(v => v > 0).ToList();

        // 构造7条样本池
        var pool = new List<double>(7);
        for (int i = 0; i < 7; i++)
        {
            pool.Add(i < valid.Count
                ? valid[i]
                : defaultTime * (1 - curiosityFactor));
        }

        // 削峰填谷：去除一个最大值和一个最小值
        var copy = new List<double>(pool);
        copy.Remove(copy.Max());
        if (copy.Count > 0) copy.Remove(copy.Min());

        return copy.Count > 0 ? copy.Average() : defaultTime;
    }
}
