using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 角色特化动作分派（按动作名+角色名决定是否使用特化逻辑）
/// </summary>
public static class AvatarSpecialAction
{
    /// <summary>
    /// 资源缩放比例
    /// </summary>
    private static double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    /// <summary>
    /// 木偶（桑多涅）红温状态评分阈值（固定 0.5）。
    /// </summary>
    private const double OverheatThreshold = 0.5;

    /// <summary>
    /// 恰斯卡子弹框特征模型：识别子弹框是否存在（子弹框不存在时恰斯卡处于喷射状态）
    /// TODO: 特征待训练后填充（1组特征），当前留空（Features 为空时 Score 返回 0.5，恒判定为"子弹框存在/非喷射"）
    /// </summary>
    private static readonly FeatureScorerExportData _chascaBulletBoxModel = new()
    {
        Features =
        {
            // TODO: 子弹框特征（1组），训练后填充
        }
    };

    /// <summary>
    /// 恰斯卡六槽位 × 五元素（风火水雷冰）子弹特征模型（6×5=30组）
    /// 索引：第一维为槽位 0-5；第二维对应 ChascaBulletType 的 Anemo/Pyro/Hydro/Electro/Cryo（1-5）
    /// TODO: 特征待训练后填充，当前留空（模型为 null 时识别跳过，对应槽位判定为空）
    /// </summary>
    private static readonly FeatureScorerExportData?[,] _chascaBulletModels = new FeatureScorerExportData?[6, 5];

    /// <summary>
    /// 桑多涅特化叠加层目标框共享画笔（避免每帧新建 Pen 导致 GDI+ 句柄抖动）
    /// </summary>
    private static readonly System.Drawing.Pen _targetPen = new(System.Drawing.Color.LimeGreen, 2);

    /// <summary>
    /// 木偶（桑多涅）红温状态特征模型（硬编码自训练工具导出的 JSON）。
    /// </summary>
    private static readonly FeatureScorerExportData _overheatModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 519, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 301.808, Weight = 0.7914,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0051, 0.0138, 0.0366, 0.0937, 0.2193, 0.433, 0.6749, 0.8494, 0.9388, 0.9766, 0.9913, 0.9968]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 518, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 300.5802, Weight = 0.789,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0166, 0.0439, 0.1109, 0.2532, 0.4796, 0.7147, 0.872, 0.9487, 0.9805, 0.9927, 0.9973]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 517, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 297.9216, Weight = 0.7738,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0213, 0.0558, 0.1384, 0.3038, 0.5426, 0.7633, 0.8976, 0.9597, 0.9848, 0.9944]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1096, Y = 513, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5461,
                RefHist = [0.0705, 0.0023, 0, 0, 0, 0.0018, 0.0739, 0.8516],
                ProbTable = [0, 0, 0.0001, 0.0002, 0.0005, 0.0015, 0.004, 0.0108, 0.0289, 0.0747, 0.18, 0.3737, 0.6186, 0.8151, 0.923, 0.9702, 0.9888, 0.9959, 0.9985, 0.9994, 0.9998]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1097, Y = 516, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5088,
                RefHist = [0.1062, 0.0046, 0, 0, 0, 0, 0.0201, 0.8691],
                ProbTable = [0, 0, 0.0001, 0.0004, 0.001, 0.0026, 0.0071, 0.0192, 0.0504, 0.1262, 0.2819, 0.5162, 0.7436, 0.8874, 0.9554, 0.9831, 0.9937, 0.9977, 0.9991, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "H", X = 1090, Y = 552, W = 4, H = 1,
                IsCircular = false, Range = 1, Weight = 0.4793,
                RefHist = [0, 0, 0.0191, 0.9213, 0.0576, 0.002, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0058, 0.0156, 0.0414, 0.1051, 0.2419, 0.4645, 0.7022, 0.865, 0.9457, 0.9793, 0.9923, 0.9972]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1105, Y = 564, W = 2, H = 3,
                IsCircular = true, Range = 360, RefVal = 349.1209, Weight = 0.7477,
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0049, 0.0133, 0.0353, 0.0905, 0.2129, 0.4237, 0.6665, 0.8446, 0.9366, 0.9757]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1095, Y = 572, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5165,
                RefHist = [0.9278, 0.0164, 0, 0, 0.0052, 0, 0.0121, 0.0384],
                ProbTable = [0, 0.0001, 0.0002, 0.0004, 0.0011, 0.003, 0.0082, 0.0221, 0.0578, 0.143, 0.3121, 0.5522, 0.7702, 0.9011, 0.9612, 0.9854, 0.9946, 0.998, 0.9993, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1105, Y = 572, W = 5, H = 4,
                IsCircular = true, Range = 360, RefVal = 351.1534, Weight = 0.7542,
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0212, 0.0556, 0.138, 0.3032, 0.5419, 0.7628, 0.8973, 0.9596, 0.9848]
            },
        }
    };

    /// <summary>
    /// 判断当前木偶是否处于红温状态（特征评分 ≥ 阈值）。
    /// 评分异常时降级返回 false，不中断战斗。
    /// </summary>
    private static bool IsOverheated(ImageRegion capture)
    {
        try
        {
            return ImageFeatureScorer.Score(_overheatModel, capture.SrcMat) >= OverheatThreshold;
        }
        catch (Exception e)
        {
            Logger.LogWarning("红温状态评分异常: {Message}", e.Message);
            return false;
        }
    }

    /// <summary>
    /// 特化规则：(动作, 角色) → 参数条件（null=无条件，仅检查动作+角色即生效）
    /// 不在此字典中的组合直接跳过，走通用逻辑。
    /// </summary>
    private static readonly Dictionary<(string Action, string Character), Func<object, bool>?> SpecializedRules = new()
    {
        [("UseSkill", "纳西妲")]   = args => args is ActionArgs { Hold: true },
        [("UseSkill", "坎蒂丝")]   = args => args is ActionArgs { Hold: true },
        [("UseSkill", "恰斯卡")]   = args => args is ActionArgs { Hold: true },
        [("Charge",   "那维莱特")] = null,
        [("Charge",   "恰斯卡")]   = null,
        [("Charge",   "桑多涅")]   = null,
    };

    /// <summary>
    /// 根据动作和角色名分派特化逻辑。
    /// 如果当前角色有对应的特化实现，则执行该特化逻辑并返回 true（调用方应跳过通用逻辑）；
    /// 否则返回 false，由调用方执行通用逻辑。
    /// </summary>
    /// <param name="action">动作名（如 "UseSkill"、"Charge"）</param>
    /// <param name="character">角色名（如 "纳西妲"）</param>
    /// <param name="args">动作参数对象（如 UseSkillArgs、ChargeArgs）</param>
    /// <returns>true 表示已由特化逻辑处理，false 表示无特化逻辑</returns>
    public static bool ExecuteSpecializedAction(Avatar avatar, string action, string character, object args)
    {
        // 不在特化规则中 → 提前退出
        if (!SpecializedRules.TryGetValue((action, character), out var condition)) return false;

        // 参数条件存在且不满足 → 提前退出
        if (condition != null && !condition(args)) return false;

        switch (action)
        {
            case "UseSkill":
                return ExecuteUseSkillSpecialized(avatar, character);
            case "Charge":
                return ExecuteChargeSpecialized(avatar, character, ((ActionArgs)args).Ms);
            default:
                return false;
        }
    }

    /// <summary>
    /// UseSkill 特化分派
    /// </summary>
    private static bool ExecuteUseSkillSpecialized(Avatar avatar, string character)
    {
        switch (character)
        {
            // 纳西妲长按 E：按下后向右移动鼠标
            case "纳西妲":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                    Sleep(300, avatar.Ct);
                    for (int j = 0; j < 10; j++)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(1000, 0);
                        Sleep(50);
                    }

                    Sleep(300);
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                    return true;
                }
            }
            // 坎蒂丝长按 E：固定等待 3 秒
            case "坎蒂丝":
            {
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                Thread.Sleep(3000);
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                return true;
            }
            // 恰斯卡长按 E：骑乘蓄力瞄准
            case "恰斯卡":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    // 第一步：确认恰斯卡状态为飞行
                    // 1. 已处于飞行状态（特定位置白色像素）→ 无需操作，进入第二步
                    // 2. 未飞行且 E 可用（OCR 识别不到 CD）→ 点按 E，等待 400ms，进入第二步
                    // 3. 未飞行且 E 不可用 → 直接跳出动作，不进入第二步
                    if (ChascaIsFlying())
                    {
                        // 已飞行，无需操作，直接进入第二步
                    }
                    else if (ReadEskillCdForChasca() > 0)
                    {
                        // E 不可用，直接跳出动作
                        return true;
                    }
                    else
                    {
                        // E 可用：点按 E，等待 400ms，按住左键（骑乘索敌射击）后进入第二步
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        Sleep(400, avatar.Ct);
                        Simulation.SendInput.Mouse.LeftButtonDown();
                    }

                    // 第二步：索敌循环逻辑
                    // 记录自本次特化启动以来，每一帧的视角朝向与时间戳（用于后续转向与退出判定）
                    var orientationHistory = new List<(float Angle, DateTime Time)>();
                    // 视觉识别配置（帧间间隔、恰斯卡稳定时间，与持续索敌一致）
                    var visConfig = AvatarRecognition.GetVisualRecognitionConfig();
                    var frameIntervalMs = visConfig.TargetingDetectionInterval;
                    var chascaStableTime = visConfig.ChascaStableTime;
                    var dpi = TaskContext.Instance().DpiScale;
                    // 距离上一次事件的时间：启动进入第二步/识别到伤害数字/上一次旋转/上一次子弹列表变化/上一次喷射动画
                    // 子弹列表变化与喷射动画的更新时间点在帧内子弹识别处补充
                    var lastEventTime = DateTime.UtcNow;
                    // 退出条件状态：第二步开始时间（10秒超时）、累计旋转（距上次识别到目标后超过一圈）
                    var startTime = DateTime.UtcNow;
                    float? prevAngle = null;
                    double cumulativeRotation = 0;
                    // 子弹序列变化跟踪：至多保存两个历史子弹序列（每帧识别结果），用于检测子弹列表变化
                    ChascaBulletType[]? bulletSeq1 = null;
                    ChascaBulletType[]? bulletSeq2 = null;
                    DateTime bulletSeq1Time = DateTime.UtcNow;
                    DateTime bulletSeq2Time = DateTime.UtcNow;
                    while (!avatar.Ct.IsCancellationRequested)
                    {
                        // 退出条件1：10秒超时
                        if ((DateTime.UtcNow - startTime).TotalSeconds >= 10)
                        {
                            // 仍在飞行：松开左键 → 长按 E 落地 → 循环截图检测直到识别到 CD → 松开 E
                            Simulation.SendInput.Mouse.LeftButtonUp();
                            Sleep(300, avatar.Ct);
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                            try
                            {
                                // 长按 E 落地，循环截图检测直到 E 进入 CD（落地完成），2 秒超时兜底防止卡死
                                var landStartTime = DateTime.UtcNow;
                                while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - landStartTime).TotalSeconds < 2)
                                {
                                    if (ReadEskillCdForChasca() > 0)
                                    {
                                        break;
                                    }
                                    Sleep(100, avatar.Ct);
                                }
                            }
                            finally
                            {
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                            }
                            break;
                        }

                        using (var capture = CaptureToRectArea())
                        {
                            // 退出条件2：不处于飞行状态（已下车）
                            if (!ChascaIsFlyingByPixel(capture.SrcMat))
                            {
                                // 已下车：松开左键并等待 300ms 即可
                                Simulation.SendInput.Mouse.LeftButtonUp();
                                Sleep(300, avatar.Ct);
                                break;
                            }

                            // 获取当前视角朝向并记录
                            var angle = CameraOrientation.Compute(capture.SrcMat);
                            orientationHistory.Add((angle, DateTime.UtcNow));
                            // 累计旋转（距上次识别到目标后重新计数）：相邻帧角度差归一化到 (-180,180] 后累加
                            if (prevAngle.HasValue)
                            {
                                var delta = angle - prevAngle.Value;
                                if (delta > 180) delta -= 360;
                                else if (delta < -180) delta += 360;
                                cumulativeRotation += delta;
                            }
                            prevAngle = angle;

                            // 恰斯卡子弹识别：六个槽位的元素状态（空/风/火/水/雷/冰）
                            var bullets = RecognizeChascaBullets(capture);

                            // 子弹框识别：子弹框不存在（<0.5）时视为正在进行子弹喷射，更新时间
                            if (ChascaIsSpraying(capture))
                            {
                                lastEventTime = DateTime.UtcNow;
                            }
                            else
                            {
                                // 子弹变化跟踪：当前帧序列与已存序列比较，无相同序列则更新时间，并替换两个历史序列中较旧的
                                var seqSame = (bulletSeq1 != null && bullets.SequenceEqual(bulletSeq1))
                                           || (bulletSeq2 != null && bullets.SequenceEqual(bulletSeq2));
                                if (!seqSame)
                                {
                                    lastEventTime = DateTime.UtcNow;
                                    if (bulletSeq1 == null || (bulletSeq2 != null && bulletSeq2Time < bulletSeq1Time))
                                    {
                                        bulletSeq1 = bullets;
                                        bulletSeq1Time = DateTime.UtcNow;
                                    }
                                    else
                                    {
                                        bulletSeq2 = bullets;
                                        bulletSeq2Time = DateTime.UtcNow;
                                    }
                                }
                            }

                            // 血条识别：区分传奇血条与普通血条
                            // FindBloodBars 内部自动更新传奇血条跨帧追踪（与持续索敌共用静态追踪器，
                            // 开启持续索敌时跨帧识别信息可保留，此处不清空追踪器）
                            var bars = AvatarRecognition.FindBloodBars(capture);
                            var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();
                            var hasLegendaryBar = valid.Any(b => AvatarRecognition.IsLegendaryBar(b.x, b.y));

                            if (valid.Count > 0 && !hasLegendaryBar)
                            {
                                // 存在普通血条且不存在传奇血条：参考桑多涅逻辑，瞄准最近血条中心
                                // 中心点使用 1080p 的 (960,300)，将敌人置于屏幕偏上位置（恰斯卡相对俯视）
                                var preAimX = (int)(960 * AssetScale);
                                var preAimY = (int)(300 * AssetScale);
                                var nearest = valid.OrderBy(b =>
                                    Math.Abs((b.x + b.width / 2) - preAimX) +
                                    Math.Abs((b.y + b.height / 2) - preAimY)).First();
                                var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                                var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                                // 单次旋转力度为桑多涅逻辑的四分之一（0.35/4、0.25/4）
                                Simulation.SendInput.Mouse.MoveMouseBy(
                                    (int)(offsetX * 0.0875 * dpi), (int)(offsetY * 0.0625 * dpi));
                                cumulativeRotation = 0; // 识别到血条，累计旋转重新计数
                            }
                            else
                            {
                                // 存在传奇血条 或 无任何血条：做伤害数字识别，瞄准有效伤害数字
                                // 中心点使用 1080p 的 (960,360)，力度为桑多涅逻辑的四分之一（0.35/4、0.25/4）
                                var damageResult = AvatarRecognition.FindDamageNumber(capture);
                                if (damageResult.HasValue)
                                {
                                    var (dcx, dcy, _, _, _, _, _) = damageResult.Value;
                                    Simulation.SendInput.Mouse.MoveMouseBy(
                                        (int)((dcx - (int)(960 * AssetScale)) * 0.0875 * dpi),
                                        (int)((dcy - (int)(360 * AssetScale)) * 0.0625 * dpi));
                                    lastEventTime = DateTime.UtcNow; // 识别到伤害数字，视为活动事件
                                    cumulativeRotation = 0; // 识别到伤害数字，累计旋转重新计数
                                }
                                else
                                {
                                    // 未识别到伤害数字：依赖子弹状态判断当前是否需要移动视角
                                    // 子弹喷射（子弹框不存在）与子弹序列变化已在帧首更新 lastEventTime
                                    // 距离上一次事件超过恰斯卡稳定时间时，进行一次水平向右旋转
                                    // 旋转实现参考恰斯卡 charge 分段变速中的水平旋转（rateX=0.7, rateY=0）
                                    if ((DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime)
                                    {
                                        Simulation.SendInput.Mouse.MoveMouseBy((int)(0.7 * 50 * dpi), 0);
                                        lastEventTime = DateTime.UtcNow; // 上一次旋转
                                    }
                                }
                            }

                            // 退出条件3：距上次识别到伤害数字或血条后，旋转超过一圈（360°）
                            // 依赖本帧朝向的累计旋转，放在截图块内（朝向记录之后）
                            if (Math.Abs(cumulativeRotation) >= 360)
                            {
                                // 仍在飞行：松开左键 → 长按 E 落地 → 循环截图检测直到识别到 CD → 松开 E
                                Simulation.SendInput.Mouse.LeftButtonUp();
                                Sleep(300, avatar.Ct);
                                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                                try
                                {
                                    // 长按 E 落地，循环截图检测直到 E 进入 CD（落地完成），10 秒超时兜底防止卡死
                                    var landStartTime = DateTime.UtcNow;
                                    while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - landStartTime).TotalSeconds < 10)
                                    {
                                        if (ReadEskillCdForChasca() > 0)
                                        {
                                            break;
                                        }
                                        Sleep(100, avatar.Ct);
                                    }
                                }
                                finally
                                {
                                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                                }
                                break;
                            }
                        }

                        // 每帧末尾等待帧间间隔
                        Sleep(frameIntervalMs);
                    }

                    return true;
                }
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Charge 重击特化分派
    /// </summary>
    private static bool ExecuteChargeSpecialized(Avatar avatar, string character, int ms)
    {
        switch (character)
        {
            // 那维莱特：按住普攻循环向右旋转
            case "那维莱特":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
                    try
                    {
                        while (ms >= 0)
                        {
                            if (avatar.Ct is { IsCancellationRequested: true })
                            {
                                return true;
                            }

                            Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                            ms -= 50;
                            Sleep(50);
                        }
                    }
                    finally
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }
                return true;
            }
            // 恰斯卡：按住普攻分段变速旋转
            case "恰斯卡":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
                    try
                    {
                        int tick = -4;
                        while (ms >= 0)
                        {
                            if (avatar.Ct is { IsCancellationRequested: true })
                            {
                                return true;
                            }

                            const double lowspeed = 0.7, highspeed = 50;
                            double rateX, rateY;
                            if (tick < 3)
                            {
                                rateX = highspeed;
                                rateY = highspeed * 0.23;
                            }
                            else if (tick < 40)
                            {
                                rateX = lowspeed * 0.7;
                                rateY = 0;
                            }
                            else if (tick < 43)
                            {
                                rateX = highspeed;
                                rateY = highspeed * 0.4;
                            }
                            else if (tick < 70)
                            {
                                rateX = lowspeed * 0.9;
                                rateY = 0;
                            }
                            else if (tick < 73)
                            {
                                rateX = highspeed;
                                rateY = highspeed;
                            }
                            else
                            {
                                rateX = lowspeed;
                                rateY = 0;
                            }

                            Simulation.SendInput.Mouse.MoveMouseBy((int)(rateX * 50 * dpi), (int)(rateY * 50 * dpi));
                            tick = (tick + 1) % 100;
                            Sleep(25);
                            ms -= 25;
                        }

                        return true;
                    }
                    finally
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }
            }
            // 桑多涅：按住普攻 + 截图寻的血条/伤害数字追踪
            case "桑多涅":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    var visConfig = AvatarRecognition.GetVisualRecognitionConfig();
                    var frameIntervalMs = visConfig.TargetingDetectionInterval;
                    var drawResults = visConfig.DrawRecognitionResults;
                    var lockLostWaitTime = visConfig.LockLostWaitTime;

                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);

                    DateTime? lastSeenTargetTime = null;
                    var startTime = DateTime.UtcNow;
                    var maxDurationMs = ms;
                    int overheatCount = 0;  // 红温连续命中计数

                    try
                    {
                        while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - startTime).TotalMilliseconds < maxDurationMs)
                        {
                            using (var capture = CaptureToRectArea())
                            {
                                // 距重击开始超过 3 秒后开始检测红温，连续命中 3 次（1/3 → 2/3 → 3/3）才提前退出
                                if ((DateTime.UtcNow - startTime).TotalSeconds >= 3)
                                {
                                    if (IsOverheated(capture))
                                    {
                                        overheatCount++;
                                        if (overheatCount >= 3)
                                        {
                                            Logger.LogInformation("桑多涅重击特化：连续 3 次检测到红温状态，提前退出");
                                            break;
                                        }

                                        Logger.LogInformation("桑多涅重击特化：检测到红温状态 {OverheatCount}/3", overheatCount);
                                    }
                                    else
                                    {
                                        overheatCount = 0;
                                    }
                                }

                                int preAimX = (int)(capture.Width * 0.5);
                                int preAimY = (int)(capture.Height * (480.0 / 1080.0));

                                var bars = AvatarRecognition.FindBloodBars(capture);
                                var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();

                                var drawList = new System.Collections.Generic.List<View.Drawable.RectDrawable>();

                                bool hasLegendaryBar = valid.Any(b => AvatarRecognition.IsLegendaryBar(b.x, b.y));

                                if (valid.Count > 0 && !hasLegendaryBar)
                                {
                                    lastSeenTargetTime = DateTime.UtcNow;
                                    var nearest = valid.OrderBy(b => Math.Abs((b.x + b.width / 2) - preAimX) + Math.Abs((b.y + b.height / 2) - preAimY)).First();
                                    //Logger.LogInformation("追踪血条: 裁剪坐标({X},{Y}) 大小({W}×{H})", nearest.x, nearest.y, nearest.width, nearest.height);
                                    var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                                    var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                                    Simulation.SendInput.Mouse.MoveMouseBy((int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));

                                    if (drawResults)
                                    {
                                        foreach (var b in valid)
                                        {
                                            var rect = new OpenCvSharp.Rect(b.x, b.y, b.width, b.height);
                                            if (b.x == nearest.x && b.y == nearest.y && b.width == nearest.width && b.height == nearest.height)
                                                drawList.Add(capture.ToRectDrawable(rect, "target", _targetPen));
                                            else
                                                drawList.Add(capture.ToRectDrawable(rect, "blood"));
                                        }
                                    }
                                }
                                else
                                {
                                    var damageResult = AvatarRecognition.FindDamageNumber(capture);
                                    if (damageResult.HasValue)
                                    {
                                        var (dcx, dcy, _, dx, dy, dw, dh) = damageResult.Value;
                                        lastSeenTargetTime = DateTime.UtcNow;
                                        var offsetX = dcx - preAimX;
                                        var offsetY = dcy - preAimY;
                                        Simulation.SendInput.Mouse.MoveMouseBy((int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));
                                        if (drawResults)
                                        {
                                            drawList.Add(capture.ToRectDrawable(
                                                new OpenCvSharp.Rect(dx, dy, dw, dh),
                                                "damage_target",
                                                _targetPen));
                                        }
                                    }

                                    if (!damageResult.HasValue)
                                    {

                                        if (!hasLegendaryBar && (DateTime.UtcNow - (lastSeenTargetTime ?? startTime)).TotalSeconds >= 1.5)
                                        {
                                            Logger.LogInformation("桑多涅重击特化：超过1.5秒未找到目标，提前退出");
                                            View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("SandroneBloodBars", drawList);
                                            break;
                                        }

                                        if (!lastSeenTargetTime.HasValue || (DateTime.UtcNow - lastSeenTargetTime.Value).TotalSeconds >= lockLostWaitTime)
                                        {
                                            Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                                        }
                                    }
                                }

                                View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("SandroneBloodBars", drawList);
                            }

                            Sleep(frameIntervalMs);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    finally
                    {
                        View.Drawable.VisionContext.Instance().DrawContent.RemoveRect("SandroneBloodBars");
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }

                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// 恰斯卡是否处于飞行状态：特定位置白色像素识别（照抄 SkillBoostHelper.SpaceAtSecondPlaceExist）
    /// </summary>
    private static bool ChascaIsFlying()
    {
        using var region = CaptureToRectArea();
        return ChascaIsFlyingByPixel(region.SrcMat);
    }

    /// <summary>
    /// 恰斯卡是否处于飞行状态：使用已有截图判定（避免索敌循环中二次截图）
    /// </summary>
    private static bool ChascaIsFlyingByPixel(Mat src)
    {
        var pixel = src.At<Vec3b>(1028, 1584);
        return pixel.Item0 >= 250 && pixel.Item1 >= 250 && pixel.Item2 >= 250;
    }

    /// <summary>
    /// 恰斯卡 E 技能冷却秒数（OCR 识别，照抄 SkillBoostHelper.ReadEskillCdAsync 核心逻辑，无冷却跟踪副作用）
    /// 识别不到 CD 时返回 0，视为 E 可用
    /// </summary>
    private static double ReadEskillCdForChasca()
    {
        using var cdRegion = CaptureToRectArea();
        var eRa = cdRegion.DeriveCrop(AutoFightAssets.Get(cdRegion).ECooldownRect);
        using var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        var cd = StringUtils.TryParseDouble(text);
        // OCR 常丢失小数点：如 "0.3" 被读成 "03"，此时按 0.x 秒处理
        if (text != null && text.Length == 2 && text[0] == '0' && char.IsAsciiDigit(text[1]))
        {
            cd = (text[1] - '0') / 10.0;
        }
        return cd;
    }

    /// <summary>
    /// 恰斯卡飞行子弹状态：六个槽位各自的元素属性（空/风/火/水/雷/冰）
    /// </summary>
    private enum ChascaBulletType
    {
        Empty = 0,
        Anemo = 1,   // 风
        Pyro = 2,    // 火
        Hydro = 3,   // 水
        Electro = 4, // 雷
        Cryo = 5,    // 冰
    }

    /// <summary>
    /// 恰斯卡是否处于喷射状态：子弹框不存在时为喷射
    /// 当前特征未填充（Score 返回 0.5），恒不判定为喷射
    /// </summary>
    private static bool ChascaIsSpraying(ImageRegion capture)
    {
        // 子弹框特征未填充时 Score 返回 0.5，< 0.5 恒为 false（不判定为喷射）
        return ImageFeatureScorer.Score(_chascaBulletBoxModel, capture.SrcMat) < 0.5;
    }

    /// <summary>
    /// 恰斯卡飞行子弹识别：识别六个子弹槽位的元素状态
    /// 每个槽位对五种子弹特征逐一评分，最高分 &gt;= 0.5 时判定为该元素，否则为空
    /// 当前特征未填充（模型全空），所有槽位判定为空
    /// </summary>
    private static ChascaBulletType[] RecognizeChascaBullets(ImageRegion capture)
    {
        var result = new ChascaBulletType[6];
        for (int slot = 0; slot < 6; slot++)
        {
            double bestScore = 0.5; // 命中阈值：低于 0.5 视为空
            var bestType = ChascaBulletType.Empty;
            for (int element = 0; element < 5; element++)
            {
                var model = _chascaBulletModels[slot, element];
                if (model == null || model.Features.Count == 0) continue; // 特征未填充，跳过
                var score = ImageFeatureScorer.Score(model, capture.SrcMat);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestType = (ChascaBulletType)(element + 1); // Anemo=1 ... Cryo=5
                }
            }
            result[slot] = bestType;
        }
        return result;
    }
}

/// <summary>
/// 特化动作参数（由动作类型决定哪些字段生效）
/// </summary>
/// <param name="Hold">UseSkill 是否长按</param>
/// <param name="Ms">Charge 持续时间（毫秒）</param>
public sealed record ActionArgs(bool Hold = false, int Ms = 0);
