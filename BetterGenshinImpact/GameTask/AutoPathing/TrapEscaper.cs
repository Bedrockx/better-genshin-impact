using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 卡死脱困（分轮）：
/// 卡死检测 + 第一套动作轻量试探（X下落+空格跳跃+普攻，斜右上走300ms），
/// 无效则主循环以1.5秒窗口再次判定；仍卡死则第二套动作（斜左上走300ms）并回头朝上一个点跑，
/// 跑出卡死点10m或回到上一个点即成功，3秒超时则放弃重试路线；
/// 回头走成功后同一点再次卡死（4秒窗口）则直接重试路线
/// </summary>
public class TrapEscaper(CancellationToken ct)
{
    private readonly CameraRotateTask _rotateTask = new(ct);

    // 卡死检测状态：未脱困8秒窗口；已执行第一套动作1.5秒；回头走成功后4秒
    private readonly List<OpenCvSharp.Point2f> _prevPositions = new();
    private DateTime _lastPositionRecord = DateTime.UtcNow;
    private int _inTrap = 0;

    /// <summary>
    /// 卡死检测 + 分轮脱困，由主移动循环每帧调用
    /// </summary>
    /// <param name="waypoint">当前目标点</param>
    /// <param name="prevWaypoint">上一个点（回头走目标，可能为空）</param>
    /// <param name="position">当前坐标</param>
    /// <param name="additionalTimeInMs">坐标识别附加耗时（用于记录间隔补偿）</param>
    /// <returns>true=本帧触发了脱困动作（主循环应 continue）；false=未判定卡死</returns>
    public async Task<bool> CheckAndEscape(WaypointForTrack waypoint, WaypointForTrack? prevWaypoint, OpenCvSharp.Point2f position, int additionalTimeInMs)
    {
        // 窗口选择：未脱困8秒；已执行第一套动作1.5秒；回头走成功后再次卡死4秒
        var recordIntervalMs = _inTrap == 1 ? 1500 : 1000 + additionalTimeInMs;
        var windowSize = _inTrap switch { 1 => 2, 2 => 4, _ => 8 };

        if ((DateTime.UtcNow - _lastPositionRecord).TotalMilliseconds <= recordIntervalMs)
        {
            return false;
        }
        _lastPositionRecord = DateTime.UtcNow;
        _prevPositions.Add(position);
        if (_prevPositions.Count <= windowSize)
        {
            return false;
        }

        var delta = _prevPositions[^1] - _prevPositions[^windowSize];
        if (Math.Abs(delta.X) + Math.Abs(delta.Y) >= 3)
        {
            return false;
        }

        if (_inTrap == 0)
        {
            // 第一套动作：轻量试探，回正常赶路，由1.5秒窗口再次判定是否脱困
            Logger.LogWarning("疑似卡死，执行第一套脱困动作...");
            _inTrap = 1;
            await FirstTrial();
            return true;
        }

        if (_inTrap == 1)
        {
            // 第二套动作并回头朝上一个点跑
            // 出口：距离卡死点>10m或回到上一个点（成功） / 3秒超时（失败，重试路线）
            Logger.LogWarning("第一套脱困无效，执行第二套脱困动作并回头走...");
            var escapeOk = await SecondTrialAndRunBack(waypoint, prevWaypoint, _prevPositions[^1]);
            if (!escapeOk)
            {
                throw new RetryException("卡死脱困失败，重试路线！");
            }
            Logger.LogInformation("卡死脱困成功，继续移动");
            _inTrap = 2; // 回头走成功过，若同一点再次卡死则直接重试路线
            return true;
        }

        // _inTrap == 2：回头走成功过仍再次卡死，直接重试路线
        throw new RetryException("同一点再次卡死，重试路线！");
    }

    /// <summary>
    /// 每个点位开始时清空卡死检测状态（到达点位/进入新点位时调用）
    /// </summary>
    public void Reset()
    {
        _inTrap = 0;
        _prevPositions.Clear();
        _lastPositionRecord = DateTime.UtcNow;
    }

    /// <summary>
    /// 第一套动作：轻量试探。松开W，X下落+空格跳跃+普攻，按W+D斜向右上走300ms后松开D只留W。
    /// 执行后回到正常赶路，由卡死检测（1.5秒窗口）再次确认是否脱困
    /// </summary>
    public async Task FirstTrial()
    {
        var keepForwardPressed = false;
        try
        {
            // 松开w，等待100，点按x（drop），等待100，点按空格，等待100，点按普攻，等待100
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.Drop);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            await Delay(100, ct);
            // 按下w和d，保持300ms后松开d，只留w
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyDown);
            await Delay(300, ct);
            keepForwardPressed = true;
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
            if (!keepForwardPressed)
            {
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            }
        }
    }

    /// <summary>
    /// 第二套动作：斜左上走300ms，然后朝上一个点回头跑
    /// </summary>
    /// <param name="waypoint">当前目标点（坐标识别用）</param>
    /// <param name="prevWaypoint">上一个点，可能为空（为空时只按"距离卡死点10m"判定出口，不转向）</param>
    /// <param name="stuckPosition">卡死点坐标</param>
    /// <returns>true=脱困成功（距离卡死点>10m或回到上一个点）；false=3秒超时</returns>
    public async Task<bool> SecondTrialAndRunBack(WaypointForTrack waypoint, WaypointForTrack? prevWaypoint, OpenCvSharp.Point2f stuckPosition)
    {
        try
        {
            // 第二套动作：松开w，X下落+空格跳跃+普攻，按W+A斜向左上走300ms后松开A只留W
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.Drop);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            await Delay(100, ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
            await Delay(300, ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);

            // 朝上一个点（回头）跑；没有上一个点时按当前朝向直接跑
            OpenCvSharp.Point2f position;
            using (var initialScreen = CaptureToRectArea())
            {
                position = Navigation.GetPosition(initialScreen, waypoint.MapName, waypoint.MapMatchMethod);
            }
            if (prevWaypoint != null)
            {
                var targetOrientation = Navigation.GetTargetOrientation(prevWaypoint, position);
                await _rotateTask.WaitUntilRotatedTo(targetOrientation, 5);
            }

            // 按下w，一直跑
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            var startTime = DateTime.UtcNow;
            while (!ct.IsCancellationRequested)
            {
                using var screen = CaptureToRectArea();
                position = Navigation.GetPosition(screen, waypoint.MapName, waypoint.MapMatchMethod);

                // 出口1：距离卡死点>10m 或 回到上一个点（距离<4）→ 脱困成功
                var movedFar = position != new OpenCvSharp.Point2f() &&
                               Math.Abs(position.X - stuckPosition.X) + Math.Abs(position.Y - stuckPosition.Y) > 10;
                var backToPrev = prevWaypoint != null && position != new OpenCvSharp.Point2f() &&
                                 Navigation.GetDistance(prevWaypoint, position) < 4;
                if (movedFar || backToPrev)
                {
                    return true;
                }

                // 出口2：超过3秒 → 脱困失败
                if ((DateTime.UtcNow - startTime).TotalSeconds > 3)
                {
                    return false;
                }

                await Delay(100, ct);
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
        }
    }
}
