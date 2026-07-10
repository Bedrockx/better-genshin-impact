using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Models;

/// <summary>
/// 怪物信息条目，对应 monsterInfo.json 中的单条记录
/// </summary>
[Serializable]
public class MonsterInfoEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 怪物类型："普通" 或 "精英"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "普通";

    /// <summary>
    /// 摩拉倍率，默认1.0
    /// </summary>
    [JsonPropertyName("moraRate")]
    public double MoraRate { get; set; } = 1.0;

    /// <summary>
    /// 怪物标签列表
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// 掉落物品列表
    /// </summary>
    [JsonPropertyName("item")]
    public List<string> Drops { get; set; } = new();

    public bool IsElite => Type == "精英";
    public bool IsNormal => Type == "普通";
}
