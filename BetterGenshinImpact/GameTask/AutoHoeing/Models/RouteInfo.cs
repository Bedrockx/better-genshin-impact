using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Models;

/// <summary>
/// 路线信息数据模型，包含路线文件路径、预计用时、怪物信息、标签、效率指数等
/// </summary>
public class RouteInfo
{
    // 文件信息
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public int Index { get; set; }

    // 从 description 解析
    public double EstimatedTime { get; set; } = 60;
    public Dictionary<string, int> MonsterInfo { get; set; } = new();

    // 怪物统计（计算得出）
    public int NormalMonsterCount { get; set; }       // m - 普通怪数量
    public int EliteMonsterCount { get; set; }        // e - 精英怪数量
    public int OriginalEliteCount { get; set; }       // 原始精英数（ignoreRate过滤前）
    public double NormalMoraGain { get; set; }        // mora_m
    public double EliteMoraGain { get; set; }         // mora_e

    // 自我优化后的调整用时
    public double AdjustedTime { get; set; }

    // 效率指数
    public double E1 { get; set; }  // 精英效率
    public double E2 { get; set; }  // 小怪效率

    // 标记
    public List<string> Tags { get; set; } = new();
    public bool Available { get; set; } = true;
    public bool Prioritized { get; set; }
    public bool Selected { get; set; }
    public int Group { get; set; }

    // 运行记录
    public List<double> Records { get; set; } = new();
    public DateTime CdTime { get; set; } = DateTime.MinValue;

    // 拾取历史
    public HashSet<string> PickupHistory { get; set; } = new();

    // 地图名称
    public string MapName { get; set; } = "Teyvat";
}
