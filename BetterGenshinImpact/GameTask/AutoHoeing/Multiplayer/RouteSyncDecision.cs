#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 线路同步决策结果
/// </summary>
public enum RouteSyncDecision
{
    /// <summary>正常继续执行当前线路</summary>
    Proceed,
    /// <summary>需要跳到协调目标线路</summary>
    SkipToTarget,
    /// <summary>结束锄地</summary>
    Abort,
    /// <summary>需要等待其他成员赶上来</summary>
    Wait,
    /// <summary>需要追赶房主进度</summary>
    CatchUp
}

/// <summary>
/// 同步点等待结果
/// </summary>
public class RouteSyncResult
{
    public RouteSyncDecision Decision { get; set; }
    public int? TargetRouteIndex { get; set; }
}
