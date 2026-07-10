using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Models;

/// <summary>
/// 路径组配置，按账户名称持久化到JSON文件
/// 路径组一选中时保存，非路径组一选中时加载
/// </summary>
[Serializable]
public class AccountGroupSettings
{
    [JsonPropertyName("tagsForGroup1")]
    public string TagsForGroup1 { get; set; } = "蕈兽";

    [JsonPropertyName("tagsForGroup2")]
    public string TagsForGroup2 { get; set; } = "";

    [JsonPropertyName("tagsForGroup3")]
    public string TagsForGroup3 { get; set; } = "";

    [JsonPropertyName("tagsForGroup4")]
    public string TagsForGroup4 { get; set; } = "";

    [JsonPropertyName("tagsForGroup5")]
    public string TagsForGroup5 { get; set; } = "";

    [JsonPropertyName("tagsForGroup6")]
    public string TagsForGroup6 { get; set; } = "";

    [JsonPropertyName("tagsForGroup7")]
    public string TagsForGroup7 { get; set; } = "";

    [JsonPropertyName("tagsForGroup8")]
    public string TagsForGroup8 { get; set; } = "";

    [JsonPropertyName("tagsForGroup9")]
    public string TagsForGroup9 { get; set; } = "";

    [JsonPropertyName("tagsForGroup10")]
    public string TagsForGroup10 { get; set; } = "";

    [JsonPropertyName("disableSelfOptimization")]
    public bool DisableSelfOptimization { get; set; }

    [JsonPropertyName("efficiencyIndex")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double EfficiencyIndex { get; set; } = 0.25;

    [JsonPropertyName("curiosityFactor")]
    public string CuriosityFactor { get; set; } = "0";

    [JsonPropertyName("ignoreRate")]
    public int IgnoreRate { get; set; } = 100;

    [JsonPropertyName("targetEliteNum")]
    public int TargetEliteNum { get; set; } = 400;

    [JsonPropertyName("targetMonsterNum")]
    public int TargetMonsterNum { get; set; } = 2000;

    [JsonPropertyName("priorityTags")]
    public string PriorityTags { get; set; } = "";

    [JsonPropertyName("excludeTags")]
    public string ExcludeTags { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    public static AccountGroupSettings? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<AccountGroupSettings>(json, JsonOptions);
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public void SaveToFile(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(filePath, json);
    }
}
