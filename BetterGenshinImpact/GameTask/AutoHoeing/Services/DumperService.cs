using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 泥头车服务：接近战斗点时提前切人放E技能
/// </summary>
public class DumperService
{
    private static readonly ILogger Logger = App.GetLogger<DumperService>();
    private const int DumperCd = 10000; // 10秒CD

    /// <summary>
    /// 泥头车主循环
    /// </summary>
    public async Task RunDumperLoop(
        List<Waypoint> waypoints,
        List<int> dumperCharacters,
        string mapName,
        CombatScenes combatScenes,
        Func<bool> isRunning,
        CancellationToken ct)
    {
        if (dumperCharacters.Count == 0) return;

        // 提取战斗点坐标
        var fightPositions = waypoints
            .Where(w => w.Action == "fight")
            .Select(w => new FightPos { X = w.X, Y = w.Y })
            .ToList();

        if (fightPositions.Count == 0) return;

        // 检查是否含有keypress(T)，有则禁用泥头车
        if (waypoints.Any(w => w.ActionParams?.Contains("keypress(T)") == true))
        {
            Logger.LogInformation("当前路线含有按键T，不启用泥头车");
            return;
        }

        // 检查是否强制使用SIFT匹配
        // 6.3版本强制使用sift的地图不开启泥头车（通过路线info判断）

        var lastDumperTime = DateTime.MinValue;

        combatScenes.BeforeTask(ct);

        while (isRunning() && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, ct);
                if (!isRunning()) break;

                // 检查是否在主界面
                using var region = CaptureToRectArea();
                if (!Bv.IsInMainUi(region)) continue;

                // 获取当前位置
                Point2f? currentPos = null;
                try
                {
                    var matchingMethod = TaskContext.Instance().Config.PathingConditionConfig.MapMatchingMethod;
                    var imgPos = Navigation.GetPosition(region, mapName, matchingMethod);
                    if (imgPos != default)
                    {
                        currentPos = MapManager.GetMap(mapName, matchingMethod)
                            .ConvertImageCoordinatesToGenshinMapCoordinates(imgPos);
                    }
                }
                catch { continue; }

                if (currentPos == null) continue;

                bool shouldPress = false;
                double dumperDistance = 0;

                foreach (var fp in fightPositions)
                {
                    if (fp.Used) continue;

                    var dx = currentPos.Value.X - fp.X;
                    var dy = currentPos.Value.Y - fp.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= 30) fp.Used = true;

                    if (distance > 5 && distance <= 30
                        && (DateTime.Now - lastDumperTime).TotalMilliseconds > DumperCd)
                    {
                        shouldPress = true;
                        lastDumperTime = DateTime.Now;
                        dumperDistance = distance;
                    }
                }

                if (shouldPress)
                {
                    Logger.LogInformation("距离下个战斗地点{Dist:F1}，启用泥头车", dumperDistance);
                    await ExecuteDumper(dumperCharacters, combatScenes, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.LogDebug("泥头车循环异常: {Msg}", ex.Message);
            }
        }
    }

    /// <summary>
    /// 执行泥头车操作：按角色编号切人放E
    /// </summary>
    public async Task ExecuteDumper(List<int> characters, CombatScenes combatScenes, CancellationToken ct)
    {
        foreach (var key in characters)
        {
            if (key < 1 || key > combatScenes.AvatarCount) continue;
            
            var avatar = combatScenes.SelectAvatar(key);

            if (avatar.IsSkillReady())
            {
                Logger.LogInformation("[泥头车] 切换{Key}号角色({Name})施放E技能", key, avatar.Name);

                //检测avatar的E的CD
            
                if (!avatar.TrySwitch())
                {
                    Logger.LogWarning("[泥头车] 切换{Key}号角色失败，跳过", key);
                    continue;
                }

                avatar.UseSkill(); 
            }
            else
            {
                Logger.LogInformation("[泥头车] {Name}的E技能未准备好，跳过", avatar.Name);
            }
        }
    }

    private class FightPos
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool Used { get; set; }
    }
}
