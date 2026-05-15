using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoFriendship.Model;

/// <summary>
/// 敌人路径配置
/// </summary>
public class EnemyPathConfig
{
    /// <summary>触发点路径</summary>
    public List<Waypoint> TriggerPath { get; set; } = [];

    /// <summary>战斗点路径</summary>
    public List<Waypoint> CombatPath { get; set; } = [];

    /// <summary>准备点路径</summary>
    public List<Waypoint> PreparePath { get; set; } = [];

    /// <summary>失败返回路径</summary>
    public List<Waypoint> FailReturnPath { get; set; } = [];

    /// <summary>战后路径</summary>
    public List<Waypoint> PostFightPath { get; set; } = [];

    /// <summary>战后对话路径</summary>
    public List<Waypoint> PostFightDialoguePath { get; set; } = [];

    /// <summary>初始延迟（毫秒）</summary>
    public int InitialDelayMs { get; set; } = 5000;

    /// <summary>失败等待时间（秒）</summary>
    public int FailWaitTimeSeconds { get; set; } = 5;
}
