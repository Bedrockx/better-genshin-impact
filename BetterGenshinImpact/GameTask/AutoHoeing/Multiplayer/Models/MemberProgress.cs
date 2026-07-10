#nullable enable

using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 成员路线进度信息（需求 6），用于进度感知等待的查询响应。
/// </summary>
public class MemberProgress
{
    public int RouteIndex { get; set; }
    public DateTime RouteStartTime { get; set; }
    public double RouteEstimatedSeconds { get; set; }
}
