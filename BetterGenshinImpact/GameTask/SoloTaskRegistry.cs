using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFriendship;
using BetterGenshinImpact.GameTask.AutoHoeing;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 独立任务注册表，用于配置组中按名称创建独立任务实例
/// </summary>
public static class SoloTaskRegistry
{
    /// <summary>
    /// 可在配置组中使用的独立任务名称列表
    /// </summary>
    public static readonly List<string> AvailableTasks =
    [
        "锄地一条龙",
        "好感任务自动完成"
    ];

    /// <summary>
    /// 根据名称创建独立任务实例
    /// </summary>
    public static ISoloTask? CreateTask(string name, PathingPartyConfig? partyConfig,
        Dictionary<string, object?>? settings = null, string? groupName = null)
    {
        return name switch
        {
            "锄地一条龙" => new AutoHoeingTask(partyConfig, settings, groupName),
            "好感任务自动完成" => new AutoFriendshipTask(TaskContext.Instance().Config.AutoFriendshipConfig, partyConfig, settings, partyConfig?.AutoFightConfig),
            _ => null
        };
    }

    /// <summary>
    /// 获取独立任务的可配置参数定义
    /// </summary>
    public static List<SoloTaskSettingItem> GetSettingItems(string taskName)
    {
        return taskName switch
        {
            "锄地一条龙" => AutoHoeingTask.GetSettingDefinitions(),
            "好感任务自动完成" => AutoFriendshipTask.GetSettingDefinitions(),
            _ => new()
        };
    }
}

/// <summary>
/// 独立任务配置项定义
/// </summary>
public class SoloTaskSettingItem
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text"; // text, number, select, bool
    public object? DefaultValue { get; set; }
    public List<string>? Options { get; set; }
}
