using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 集合点自动推断算法。
/// 纯算法类，无 I/O，无外部依赖。
/// </summary>
public class SyncPointResolver
{
    /// <summary>
    /// 判断候选点是否合法（排除不适合作为集合点的路径点类型）。
    /// </summary>
    public static bool IsValidCandidate(WaypointForTrack wp)
    {
        if (wp.MoveMode is "fly" or "climb" or "swim" or "jump") return false;
        if (wp.Type is "teleport" or "orientation" or "actionOnly") return false;
        if (wp.Action is "stop_flying" or "up_down_grab_leaf"
                      or "exit_and_relogin" or "fight") return false;
        return true;
    }

    /// <summary>
    /// 对单个 fight waypoint 向前扫描，推断最近的合法集合点。
    /// 遇到 teleport 立即停止扫描；优先选 walk/run，fallback 选 dash。
    /// </summary>
    private static WaypointForTrack? FindSyncPoint(
        List<WaypointForTrack> waypoints,
        int fightIndex,
        ISet<int> usedIndices,
        double minDist = 30.0)
    {
        var fight = waypoints[fightIndex];
        WaypointForTrack? dashFallback = null;
        int dashFallbackIndex = -1;

        for (int j = fightIndex - 1; j >= 0; j--)
        {
            var wp = waypoints[j];

            // 遇到传送点立即停止，不跨传送段
            if (wp.Type == "teleport") break;

            if (!IsValidCandidate(wp)) continue;
            if (usedIndices.Contains(j)) continue;

            var dist = Navigation.GetDistance(wp,
                new Point2f((float)fight.X, (float)fight.Y));
            if (dist < minDist) continue;

            if (wp.MoveMode is "walk" or "run")
            {
                usedIndices.Add(j);
                return wp; // 优先 walk/run
            }

            if (wp.MoveMode == "dash" && dashFallback == null)
            {
                dashFallback = wp;
                dashFallbackIndex = j;
            }
        }

        if (dashFallback != null)
            usedIndices.Add(dashFallbackIndex);

        return dashFallback; // fallback dash，或 null
    }

    private static (WaypointForTrack? syncPoint, int syncIdx) FindSyncPointWithIndex(
        List<WaypointForTrack> waypoints,
        int fightIndex,
        ISet<int> usedIndices,
        double minDist = 30.0)
    {
        var fight = waypoints[fightIndex];

        for (int j = fightIndex - 1; j >= 0; j--)
        {
            var wp = waypoints[j];
            if (wp.Type == "teleport") break;
            if (!IsValidCandidate(wp)) continue;
            if (usedIndices.Contains(j)) continue;

            var dist = Navigation.GetDistance(wp, new Point2f((float)fight.X, (float)fight.Y));
            if (dist >= minDist)
            {
                // walk/run/dash 同等优先，找到第一个合法点就返回
                usedIndices.Add(j);
                return (wp, j);
            }
        }

        // 找不到距离 >= minDist 的合法点，回退到最近的传送点（而非战斗点本身）
        for (int j = fightIndex - 1; j >= 0; j--)
        {
            if (waypoints[j].Type == "teleport")
            {
                usedIndices.Add(j);
                return (waypoints[j], j);
            }
        }

        // 连传送点都没有（路线第一个段），跳过同步
        return (null, -1);
    }

    /// <summary>
    /// 对路线中所有 fight waypoint 批量推断集合点。
    /// </summary>
    /// <param name="waypoints">完整路线点列表</param>
    /// <returns>fight waypoint 索引 → 集合点（null 表示跳过同步）</returns>
    public Dictionary<int, WaypointForTrack?> Resolve(List<WaypointForTrack> waypoints, double minDist = 30.0)
    {
        var result = new Dictionary<int, WaypointForTrack?>();
        var usedIndices = new HashSet<int>();

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i].Action == "fight")
            {
                result[i] = FindSyncPoint(waypoints, i, usedIndices, minDist);
            }
        }

        return result;
    }

    /// <summary>
    /// 返回 (fightIdx, syncPointIdx, syncPoint) 三元组，syncPointIdx 为集合点在 waypoints 中的索引
    /// </summary>
    public List<(int fightIdx, int syncPointIdx, WaypointForTrack? syncPoint)> ResolveWithIndex(
        List<WaypointForTrack> waypoints, double minDist = 30.0)
    {
        var result = new List<(int, int, WaypointForTrack?)>();
        var usedIndices = new HashSet<int>();

        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i].Action == "fight")
            {
                var (syncPoint, syncIdx) = FindSyncPointWithIndex(waypoints, i, usedIndices, minDist);
                result.Add((i, syncIdx, syncPoint));
            }
        }

        return result;
    }
}
