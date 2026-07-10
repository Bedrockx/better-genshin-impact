#nullable enable
using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶聚物 / 回战斗点流程"读小地图坐标"的播种 + 距离护栏决策纯函数集合，PBT 友好（无外部依赖、可重复调用）。
/// 由 kazuha-collect-fightpoint-position-misrecognition-fix spec 引入，用于：
/// - 方案 A（首帧播种）：ComputeSeedAnchor 决定用什么坐标作 Navigation.SetPrevPosition 的种子（恒等返回战斗点）。
/// - 方案 B（读取侧距离护栏）：IsRecognizedPositionTrustworthy 判定识别结果是否可信
///   （数值合法且距种子锚点 ≤ 阈值），编排层据此决定是否拒绝 + 全局重匹配 / 判失败。
///
/// 与既有 KazuhaCollectPointDecisions / KazuhaCollectRecognitionDecisions 同模式：
/// 决策集中在纯函数，编排层（PathExecutor / KazuhaCollectSyncCoordinator）只负责调用。
/// 详见 design.md §1 / Correctness Properties Property 1 / Property 2。
/// </summary>
public static class KazuhaCollectPositionGuardDecisions
{
    /// <summary>
    /// 方案 B 读取侧距离护栏的默认阈值（小地图坐标单位）。
    /// 以 NavigationInstance.GetPositionStable 既有的 150 单位跳跃护栏（NavigationInstance.cs:123）为基准。
    /// 取略宽的 180：聚物 / 回战斗点过程中角色可能被怪拉走、战斗漂移，比纯跳跃护栏宽一档，
    /// 既能挡住"陈旧锚点局部误匹配到几百单位外旧位置 / 全局 garbage 远点"，又给正常战斗漂移留余量。
    /// 不引入用户配置项（保持最小改动）；如需调整改这个常量即可（编译期常量，零运行时开销）。
    /// 区间参考 bugfix.md Open Question Q5：150（基准）~ 200（放宽），取中位偏宽的 180。
    /// </summary>
    public const double RecognizedPositionGuardThreshold = 180.0;

    /// <summary>
    /// 方案 A：决定播种锚点的种子坐标。
    /// 当前策略恒等返回"本段战斗点坐标"（fightPoint），抽成纯函数便于：
    /// (1) PBT 行为级断言"种子 == 战斗点"；(2) 集中表达"用什么作种子"的决策，未来若改用稳定快照只改这里。
    /// </summary>
    /// <param name="fightPointX">本段战斗点 X（小地图坐标系）</param>
    /// <param name="fightPointY">本段战斗点 Y（小地图坐标系）</param>
    public static (double X, double Y) ComputeSeedAnchor(double fightPointX, double fightPointY)
    {
        return (fightPointX, fightPointY);
    }

    /// <summary>
    /// 方案 A 扩展（return-to-point-stale-prev-position-drift-fix (d)）：线路中间普通节点的播种种子取舍。
    /// 角色正从 prevWaypoint 走向 currentWaypoint，prevWaypoint 比 currentWaypoint
    /// 更接近角色真实位置（角色刚从那走来，还没到 current），故选 prevWaypoint 作种子。
    /// 抽成纯函数便于 PBT 行为级断言"中间节点种子 == 上一个 waypoint"。
    /// </summary>
    /// <param name="prevWaypointX">上一个 waypoint X（小地图坐标系）</param>
    /// <param name="prevWaypointY">上一个 waypoint Y</param>
    /// <param name="currentWaypointX">当前 waypoint X（目标点，角色还没到）</param>
    /// <param name="currentWaypointY">当前 waypoint Y</param>
    public static (double X, double Y) ComputeMidRouteSeedAnchor(
        double prevWaypointX, double prevWaypointY,
        double currentWaypointX, double currentWaypointY)
    {
        // 恒等选上一个 waypoint（current 参数保留用于 PBT 表达"选 prev 非 current"语义、未来若改策略只改这里）
        return (prevWaypointX, prevWaypointY);
    }

    /// <summary>
    /// 方案 B：判定识别结果是否可信（采纳）。
    /// 可信 = 数值合法（非 NaN/Inf）且距种子锚点距离 ≤ 阈值。
    /// 返回 false 时调用方应拒绝该结果（触发 B-1 全局重匹配一次 / B-2 判失败）。
    /// 距离计算封装在此，编排层不内联。
    /// </summary>
    /// <param name="recognizedX">识别结果 X</param>
    /// <param name="recognizedY">识别结果 Y</param>
    /// <param name="seedX">种子锚点 X（= 战斗点，方案 A 的播种值）</param>
    /// <param name="seedY">种子锚点 Y</param>
    /// <param name="threshold">距离护栏阈值（默认用 RecognizedPositionGuardThreshold）</param>
    public static bool IsRecognizedPositionTrustworthy(
        double recognizedX, double recognizedY, double seedX, double seedY, double threshold)
    {
        // 识别结果数值非法 → 不可信（NaN/Inf 视为读取失败，交 B-2 判失败）
        if (double.IsNaN(recognizedX) || double.IsNaN(recognizedY)) return false;
        if (double.IsInfinity(recognizedX) || double.IsInfinity(recognizedY)) return false;
        // 种子非法 / 阈值非法 → 无法判定，保守视为可信（不干预），避免误拒正常识别
        if (double.IsNaN(seedX) || double.IsNaN(seedY)) return true;
        if (double.IsInfinity(seedX) || double.IsInfinity(seedY)) return true;
        if (double.IsNaN(threshold) || threshold < 0) return true;

        var dx = recognizedX - seedX;
        var dy = recognizedY - seedY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        return distance <= threshold;
    }

    /// <summary>
    /// Point2f 重载，便于编排层直接传 Navigation.GetPosition 的返回值与战斗点。
    /// </summary>
    public static bool IsRecognizedPositionTrustworthy(
        Point2f recognized, double seedX, double seedY, double threshold)
        => IsRecognizedPositionTrustworthy(recognized.X, recognized.Y, seedX, seedY, threshold);
}
