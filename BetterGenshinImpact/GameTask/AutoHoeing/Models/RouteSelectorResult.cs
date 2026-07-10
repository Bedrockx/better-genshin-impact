namespace BetterGenshinImpact.GameTask.AutoHoeing.Models;

/// <summary>
/// 路线选择结果
/// </summary>
public class RouteSelectorResult
{
    public int TotalElites { get; set; }
    public int TotalMonsters { get; set; }
    public double TotalGain { get; set; }
    public double TotalTime { get; set; }
}
