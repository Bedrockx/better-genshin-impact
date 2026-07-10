#nullable enable
using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶聚物点几何计算的纯函数集合，PBT 友好（无外部依赖、可重复调用）。
/// 由 multiplayer-kazuha-collect-point-broadcast spec 引入，用于：
/// - 万叶玩家 HoldE 起手前根据自身位置 + 相机朝向计算聚物点 (collectX, collectY)
/// - 上报 / 接收两端校验坐标有效性（NaN / Inf / (0, 0) 全部判无效）
/// - 非万叶玩家本地缓存按 syncKey 匹配命中聚物点
///
/// 朝向角约定与 <see cref="BetterGenshinImpact.GameTask.AutoPathing.Navigation.GetTargetOrientation"/> 一致：
/// 0° = +X 方向，逆时针递增，与 CameraOrientation.Compute 返回值同语义。
/// 详见 design.md §5。
/// </summary>
public static class KazuhaCollectPointDecisions
{
    /// <summary>
    /// 根据万叶当前位置 + 相机朝向 + 偏移距离，计算聚物点小地图坐标。
    /// (cx, cy) = (myX + forwardDistance * cos(rad), myY + forwardDistance * sin(rad))
    /// 其中 rad = cameraOrientationDeg * PI / 180。
    /// </summary>
    /// <param name="myX">万叶当前 X 坐标（小地图坐标系）</param>
    /// <param name="myY">万叶当前 Y 坐标（小地图坐标系）</param>
    /// <param name="cameraOrientationDeg">相机朝向角（度，0-360 区间）</param>
    /// <param name="forwardDistance">向前偏移距离（小地图距离单位），spec 默认固定 1.0 米</param>
    public static (double X, double Y) ComputeCollectPoint(
        double myX, double myY, double cameraOrientationDeg, double forwardDistance)
    {
        var rad = cameraOrientationDeg * Math.PI / 180.0;
        var cx = myX + forwardDistance * Math.Cos(rad);
        var cy = myY + forwardDistance * Math.Sin(rad);
        return (cx, cy);
    }

    /// <summary>
    /// 聚物点坐标有效性判定。NaN / Inf / (0, 0) 都视为无效。
    /// (0, 0) 排除是因为 Navigation.GetPosition 识别失败时返回 (0, 0)（与既有约定一致）。
    /// </summary>
    public static bool IsValid(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return false;
        if (double.IsInfinity(x) || double.IsInfinity(y)) return false;
        if (x == 0.0 && y == 0.0) return false;
        return true;
    }

    /// <summary>
    /// 根据查询 syncKey 在缓存中查找聚物点。
    /// 仅当 cached 有值、queryKey 非空、且 cached.SyncKey == queryKey 时返回 true 并输出 (x, y)，
    /// 否则返回 false 并把 (x, y) 置为 (0, 0)。
    /// 由 KazuhaCollectSyncCoordinator.TryGetCollectPointForCurrent 内部调用，PBT-5 覆盖。
    /// </summary>
    public static bool TryMatch(
        (string SyncKey, double X, double Y)? cached,
        string queryKey,
        out double x, out double y)
    {
        if (!cached.HasValue || string.IsNullOrEmpty(queryKey) || cached.Value.SyncKey != queryKey)
        {
            x = 0;
            y = 0;
            return false;
        }
        x = cached.Value.X;
        y = cached.Value.Y;
        return true;
    }
}
