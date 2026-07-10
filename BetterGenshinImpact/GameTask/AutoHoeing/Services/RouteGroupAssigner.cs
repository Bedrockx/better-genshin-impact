using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using BetterGenshinImpact.GameTask.AutoHoeing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 路线分组分配器：将已选路线按标签分配到10个路径组
/// </summary>
public static class RouteGroupAssigner
{
    private static readonly ILogger Logger = App.GetLogger<AutoHoeingTask>();

    public static void AssignGroups(List<RouteInfo> routes, List<List<string>> groupTags)
    {
        foreach (var route in routes)
        {
            if (!route.Selected) continue;

            route.Group = 0;

            if (!groupTags[0].Any(tag => route.Tags.Contains(tag)))
            {
                route.Group = 1;
            }
            else
            {
                for (int i = 1; i <= 9; i++)
                {
                    if (i < groupTags.Count && groupTags[i].Any(tag => route.Tags.Contains(tag)))
                    {
                        route.Group = i + 1;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 输出分组汇总信息并写入调试结果文件（与JS一致）
    /// </summary>
    public static void PrintGroupSummary(List<RouteInfo> routes, AutoHoeingConfig config, string dataDir)
    {
        var groupNames = new[]
        {
            "路径组一", "路径组二", "路径组三", "路径组四", "路径组五",
            "路径组六", "路径组七", "路径组八", "路径组九", "路径组十"
        };

        var sb = new StringBuilder();
        sb.AppendLine("路线分配结果汇总");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        var selectedCount = routes.Count(p => p.Selected);
        sb.AppendLine($"总选中路线数: {selectedCount} 条");
        sb.AppendLine();

        int totalElites = 0, totalMonsters = 0, totalIgnoredElites = 0;
        double totalGain = 0, totalTime = 0;

        var tagSettings = new[]
        {
            config.TagsForGroup1, config.TagsForGroup2, config.TagsForGroup3,
            config.TagsForGroup4, config.TagsForGroup5, config.TagsForGroup6,
            config.TagsForGroup7, config.TagsForGroup8, config.TagsForGroup9,
            config.TagsForGroup10
        };

        for (int g = 1; g <= 10; g++)
        {
            var groupRoutes = routes.Where(p => p.Group == g && p.Selected).ToList();
            if (groupRoutes.Count == 0) continue;

            var elites = groupRoutes.Sum(p => p.EliteMonsterCount);
            var monsters = groupRoutes.Sum(p => p.NormalMonsterCount);
            var gain = groupRoutes.Sum(p => p.EliteMoraGain + p.NormalMoraGain);
            var time = groupRoutes.Sum(p => p.AdjustedTime);
            var ignoredElites = groupRoutes.Sum(p => p.OriginalEliteCount - p.EliteMonsterCount);

            totalElites += elites;
            totalMonsters += monsters;
            totalGain += gain;
            totalTime += time;
            totalIgnoredElites += ignoredElites;

            var ts = TimeSpan.FromSeconds(time);
            var tagType = g == 1 ? "排除的标签" : "选择的标签";
            var tags = g <= tagSettings.Length ? tagSettings[g - 1] : "";

            var lines = new[]
            {
                $"{groupNames[g - 1]} 总计：",
                $"  {tagType}:【{tags}】",
                $"  路线条数: {groupRoutes.Count}",
                $"  精英怪数: {elites}",
                $"  被忽视精英数: {ignoredElites}",
                $"  小怪数  : {monsters}",
                $"  预计收益: {gain:F0} 摩拉",
                $"  预计用时: {(int)ts.TotalHours} 时 {ts.Minutes} 分 {ts.Seconds} 秒"
            };

            foreach (var line in lines)
            {
                Logger.LogInformation("{Line}", line);
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        // 总计
        var tsTotal = TimeSpan.FromSeconds(totalTime);
        sb.AppendLine(new string('=', 50));
        sb.AppendLine("总体统计：");
        sb.AppendLine($"  总路线数: {selectedCount} 条");
        sb.AppendLine($"  总精英怪: {totalElites}");
        sb.AppendLine($"  被忽视精英怪数: {totalIgnoredElites}");
        sb.AppendLine($"  总小怪数: {totalMonsters}");
        sb.AppendLine($"  总收益  : {totalGain:F0} 摩拉");
        sb.AppendLine($"  总用时  : {(int)tsTotal.TotalHours} 时 {tsTotal.Minutes} 分 {tsTotal.Seconds} 秒");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();
        sb.AppendLine("配置参数：");
        sb.AppendLine($"  摩拉/耗时权衡因数: {config.EfficiencyIndex}");
        sb.AppendLine($"  好奇系数: {config.CuriosityFactor}");
        sb.AppendLine($"  忽略比例: {config.IgnoreRate}");
        sb.AppendLine($"  目标精英数: {config.TargetEliteNum}");
        sb.AppendLine($"  目标小怪数: {config.TargetMonsterNum}");
        sb.AppendLine($"  优先级标签: {config.PriorityTags}");
        sb.AppendLine($"  排除标签: {config.ExcludeTags}");

        // 写入文件
        try
        {
            var resultDir = Path.Combine(dataDir, "调试结果");
            Directory.CreateDirectory(resultDir);
            File.WriteAllText(Path.Combine(resultDir, "路线分配结果.txt"), sb.ToString());
            Logger.LogInformation("路线分配结果已保存至: 调试结果/路线分配结果.txt");
        }
        catch (Exception ex)
        {
            Logger.LogError("保存路线分配结果文件失败: {Msg}", ex.Message);
        }

        // 复制路线文件到调试目录
        CopyPathingsByGroup(routes, dataDir);
    }

    /// <summary>
    /// 按组复制选中的路线文件到调试结果目录
    /// </summary>
    private static void CopyPathingsByGroup(List<RouteInfo> routes, string dataDir)
    {
        try
        {
            foreach (var route in routes.Where(r => r.Selected))
            {
                var groupFolder = Path.Combine(dataDir, "调试结果", $"group{route.Group}");
                // 保持原始相对路径结构
                var pathingDir = Path.Combine(dataDir, "pathing");
                var relativePath = route.FullPath.StartsWith(pathingDir)
                    ? route.FullPath.Substring(pathingDir.Length).TrimStart(Path.DirectorySeparatorChar)
                    : route.FileName;
                var targetPath = Path.Combine(groupFolder, relativePath);

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                File.Copy(route.FullPath, targetPath, true);
            }
            Logger.LogInformation("路线文件已复制到调试结果目录");
        }
        catch (Exception ex)
        {
            Logger.LogError("复制路线文件失败: {Msg}", ex.Message);
        }
    }
}
