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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// 恰斯卡 6 个子弹槽位区域（1080p 基准绝对坐标，由各槽位特征模型坐标 + 余量圈定，斜排于屏幕右上至右下）。
    /// 伤害数字识别需排除这些区域，避免彩色元素图标被误判为伤害数字。
    /// </summary>
    private static readonly OpenCvSharp.Rect[] ChascaBulletRects =
    [
        new(935, 112, 60, 60),   // 槽位0（特征 957,131）
        new(995, 140, 72, 45),   // 槽位1（特征 1004-1054, 152-166）
        new(1090, 140, 76, 75),  // 槽位2（特征 1102-1154, 155-204）
        new(1160, 232, 45, 48),  // 槽位3（特征 1170-1191, 241-264）
        new(1228, 312, 72, 58),  // 槽位4（特征 1240-1286, 324-356）
        new(1270, 400, 52, 58),  // 槽位5（特征 1284-1308, 416-444）
    ];

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
        // 阿蕾奇诺普攻特化：仅 attack(n)（n>0，脚本秒数→毫秒）触发；
        // 单独的 attack（ms=0）不触发，走通用普攻逻辑
        [("Attack",   "阿蕾奇诺")] = args => args is ActionArgs { Ms: > 0 },
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
            case "Attack":
                return ExecuteAttackSpecialized(avatar, character, ((ActionArgs)args).Ms);
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
                    // 平滑旋转控制（声明于 try 外，保证 finally 中可取消独立异步旋转循环）
                    var smoothRotateCts = CancellationTokenSource.CreateLinkedTokenSource(avatar.Ct);
                    Task smoothRotateTask = null!;
                    try
                    {
                    // 第一步：确认恰斯卡状态为飞行
                    // 1. 已处于飞行状态（特定位置白色像素）→ 按住左键开始射击，进入第二步
                    // 2. 未飞行且 E 可用（OCR 识别不到 CD）→ 点按 E，等待 400ms，进入第二步
                    // 3. 未飞行且 E 不可用 → 直接跳出动作，不进入第二步
                    if (ChascaIsFlying())
                    {
                        // 已飞行：按住左键（骑乘索敌射击）后进入第二步
                        Simulation.SendInput.Mouse.LeftButtonDown();
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
                    // 稳定时间倍数：识别到子弹喷射后，下一次稳定时间判定阈值翻倍（喷射后留出缓冲再旋转）
                    double stableTimeMultiplier = 1;
                    // 退出条件状态：第二步开始时间（10秒超时）、累计旋转（距上次识别到目标后超过一圈）
                    var startTime = DateTime.UtcNow;
                    float? prevAngle = null;
                    double cumulativeRotation = 0;
                    // 水平旋转力度（像素/次）：初始取配置值（恰斯卡初始旋转力度，默认 1000≈单次 30°），
                    // 之后根据实测旋转角度自适应校准（目标单次旋转角度由配置"恰斯卡单次旋转角度"决定，默认 50°）
                    double rotateX = visConfig.ChascaInitialRotateX * dpi;
                    // 单次旋转角度（度）：由配置决定（默认 50）。传奇血条存在（有目标）时单次旋转该角度，
                    // 无目标（无血条连续旋转）时使用该值的一半
                    double rotateStepAngle = visConfig.ChascaRotateStepAngle;
                    // 上一帧是否执行过水平旋转（用于下一帧实测角度自适应校准）
                    bool rotatedLastFrame = false;
                    // 旋转时实际使用的水平力度（px），供下一帧计算 实测角度÷力度 比例
                    double rotateXUsed = 0;
                    // 最近 5 次 实测角度÷力度 比例（滑动窗口，取中位数校准，抗异常值干扰且响应及时）
                    List<double> angleRatios = new();
                    // 最近一次校准得到的中位数 角度÷力度 比例（供无血条连续旋转换算固定角度力度）
                    double lastMedianRatio = 0;
                    // 无血条连续旋转模式：第一次由稳定时间触发后，不再等待稳定间隔，每帧旋转"单次旋转角度的一半"（默认25°），
                    // 直到再次看到血条或伤害数字后重置（恢复稳定时间判定）
                    bool continuousRotating = false;
                    // 子弹序列变化跟踪：保存至多 N 个历史子弹序列（每帧识别结果），用于检测子弹列表变化。
                    // N 由配置"恰斯卡序列槽数量"决定（默认 2，范围 1-5），识别结果与全部历史序列比较，
                    // 序列变化时替换最旧的历史序列
                    int seqSlotCount = Math.Clamp(visConfig.ChascaSequenceSlotCount, 1, 5);
                    List<ChascaBulletType[]> bulletSeqs = new();
                    List<DateTime> bulletSeqTimes = new();
                    // 退出条件4状态：传奇血条最后出现时间（本次第二步期间出现后，连续1.5秒未出现时触发下车）
                    DateTime? legendaryBarLastSeen = null;

                    // 平滑转动模式：勾选"恰斯卡平滑转动"后启用，取代原有"无血条连续25°/帧"与"传奇血条间歇50°大旋转"两种旋转。
                    // 无目标分支超过稳定时间后置旋转请求标志，由下方独立异步循环持续小步旋转（间隔较小、角度较小），
                    // 转速根据主循环维护的视角-时间序列（orientationHistory）实测值与预期值自适应调节
                    bool smoothRotateEnabled = visConfig.ChascaSmoothRotateEnabled;
                    // 旋转请求标志（主循环写、旋转器线程读，经 Volatile 访问保证可见性）
                    bool smoothRotateRequested = false;
                    // 往回转补偿进行中标志（回转循环写、主循环读，经 Volatile 访问）：回转期间主循环协作空转，避免鼠标操作冲突
                    bool rollbackActive = false;
                    // 子弹喷射快速下压的上次触发时间（1 秒内置冷却，硬编码）
                    DateTime lastSprayPressTime = DateTime.MinValue;
                    // 红色箭头下方检测计数：平滑水平旋转期间，所有红色箭头持续处于正下方45度
                    // （正下±22.5度）区间时计数加一，否则减一（下限0）；超过配置阈值（默认20，约90度）
                    // 时清零并执行一次强力下压（约5倍"伤害数字位于屏幕最下方"的瞄准力度）
                    int downArrowCounter = 0;
                    // 向上旋转状态：平滑水平旋转期间检测到红色箭头在正上方45度（正上±22.5度）区间时置 true
                    // （暂停水平旋转），由主循环每帧向上旋转一步；被血条/伤害数字/子弹事件打断（路径1，
                    // 索敌已成功不再旋转）或箭头连续3帧消失（路径2，恢复水平旋转）时复位
                    bool verticalRotateActive = false;
                    // 向上旋转期间箭头消失的连续帧数（连续3帧后退出向上旋转）
                    int arrowLostFrames = 0;
                    // 平滑旋转步进水平力度（px/步，主循环初始化、旋转器线程读取并调节，经 Volatile 访问）。
                    // 仅在首次进入平滑旋转时初始化，暂停后恢复时沿用上次保存的力度断点（由 EMA 持续调节）
                    int smoothStepX = 0;
                    // 平滑旋转力度是否已初始化（仅主循环线程访问）：保证断点只初始化一次，暂停恢复不重置
                    bool smoothStepInitialized = false;
                    // 独立异步旋转循环：节奏不依赖主循环帧间隔，仅在旋转请求标志为 true 时旋转，
                    // 否则等待一个帧间隔后 continue 跳过（避免忙等）
                    smoothRotateTask = Task.Run(() =>
                    {
                        // 增量消费主循环记录的活跃段样本（仅旋转器线程访问）：
                        // 游标 + 上一个已消费样本 + 转速 EMA（°/s，平滑相邻样本瞬时转速的识别噪声）。
                        // 每个新样本与上一个时间连续（两次采样均在平滑旋转期间）时计算一次瞬时转速并做一次
                        // 小幅度 EMA 修正，每点有且仅使用一次；不连续（暂停后恢复）时重置基线，首个样本仅作起点
                        int lastConsumedIndex = -1;
                        (float Angle, DateTime Time)? lastConsumedSample = null;
                        double emaSpeed = 0;
                        while (!smoothRotateCts.Token.IsCancellationRequested)
                        {
                            // 不满足旋转条件：等待一个帧间隔后跳过
                            if (!Volatile.Read(ref smoothRotateRequested))
                            {
                                Sleep(frameIntervalMs, smoothRotateCts.Token);
                                continue;
                            }
                            // 预期转速（度/秒，可配置）：有目标（传奇血条）与无目标场景统一使用配置值
                            double expectedSmoothRotateSpeed = visConfig.ChascaSmoothRotateSpeed;
                            // 增量消费主循环新增的活跃段样本：样本由主循环仅在平滑旋转活跃段写入，
                            // 暂停段的静止样本不入列。每个新样本与上一个已消费样本时间连续（间隔约一个主循环帧，
                            // 说明两次采样均在平滑旋转期间）时，用两点计算一次瞬时转速并做一次小幅度 EMA 修正；
                            // 时间不连续（暂停后恢复的首个样本）仅重置基线，不修正
                            lock (orientationHistory)
                            {
                                int count = orientationHistory.Count;
                                if (count > lastConsumedIndex + 1)
                                {
                                    for (int i = lastConsumedIndex + 1; i < count; i++)
                                    {
                                        var sample = orientationHistory[i];
                                        if (lastConsumedSample.HasValue)
                                        {
                                            double dt = (sample.Time - lastConsumedSample.Value.Time).TotalSeconds;
                                            // 时间连续判定：正常相邻样本间隔约一个主循环帧，超过 0.25s 视为跨段（暂停恢复）
                                            if (dt > 0.05 && dt <= 0.25)
                                            {
                                                double dAngle = sample.Angle - lastConsumedSample.Value.Angle;
                                                if (dAngle > 180) dAngle -= 360;
                                                else if (dAngle < -180) dAngle += 360;
                                                double instSpeed = Math.Abs(dAngle) / dt;
                                                // 转速 EMA（新值 30% 权重）：平滑相邻样本瞬时转速的识别噪声
                                                emaSpeed = emaSpeed > 0 ? emaSpeed * 0.7 + instSpeed * 0.3 : instSpeed;
                                                // 转速过低（初始力度过小或画面静止）时也按低速评估，避免永不进入自适应而空转
                                                if (emaSpeed > 0.1)
                                                {
                                                    double factor = Math.Clamp(expectedSmoothRotateSpeed / Math.Max(emaSpeed, 0.5), 0.2, 5.0);
                                                    if (Math.Abs(factor - 1) > 0.1)
                                                    {
                                                        // 步进力度按乘法 EMA 渐近（new = current × factor^0.2）：
                                                        // 在乘法域平滑调节，放大/缩小对称互逆（factor 与其倒数调整恰好互为倒数，
                                                        // 单次最多放大 5^0.2≈1.38 倍、最小缩小 0.2^0.2≈0.72 倍），
                                                        // 避免线性插值造成放大远大于缩小的不对称
                                                        double current = Volatile.Read(ref smoothStepX);
                                                        double newStep = Math.Clamp(current * Math.Pow(factor, 0.2), 1, 2000);
                                                        Volatile.Write(ref smoothStepX, (int)newStep);
                                                    }
                                                }
                                            }
                                            // 不连续（暂停后恢复）：此样本仅作新基线，不参与转速计算
                                        }
                                        lastConsumedSample = sample;
                                    }
                                    lastConsumedIndex = count - 1;
                                }
                            }
                            // 按调节后的步进力度小角度旋转一次
                            int stepX = Volatile.Read(ref smoothStepX);
                            if (stepX > 0)
                            {
                                Simulation.SendInput.Mouse.MoveMouseBy(stepX,
                                    (int)(visConfig.ChascaPressStrength * stepX * 0.194));
                            }
                            // 独立于主循环帧的步进间隔（约62步/秒，10°级小步连续旋转）
                            Sleep(16, smoothRotateCts.Token);
                        }
                    }, avatar.Ct);

                    // 局部函数：下车动作（松开左键 → 长按 E 落地 → 检测 E 进入 CD → 松开 E），2 秒超时兜底防止卡死
                    void LandChasca()
                    {
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        Sleep(500, avatar.Ct);
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                        try
                        {
                            var landStartTime = DateTime.UtcNow;
                            while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - landStartTime).TotalSeconds < 3)
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
                    }

                    // 局部函数：按目标角度水平旋转一次（用最近中位数 角度÷力度 比例换算力度；无样本时回退 rotateX）
                    // 旋转后额外等待两个帧间隔，确保画面稳定后再继续识别
                    void RotateStep(double targetDeg)
                    {
                        double stepX = lastMedianRatio > 0 ? targetDeg / lastMedianRatio : rotateX;
                        Simulation.SendInput.Mouse.MoveMouseBy((int)stepX, (int)(visConfig.ChascaPressStrength * stepX * 0.194));
                        Sleep(frameIntervalMs * 2, avatar.Ct);
                    }

                    // 局部函数：停止平滑旋转，并对子弹识别延迟导致的过冲做往回转补偿。
                    // 子弹识别存在延迟，识别到"应停止旋转"（子弹喷射/序列变化）时视角实际已多转，
                    // 故以与正转相同的步进节奏（16ms）与断点力度（smoothStepX，方向取反）持续往回转，
                    // 每步用视角识别判断是否到达目标点（停止角度-15°）±5°，到达后退出（不再一次性转固定角度）。
                    // 回转在独立异步循环中执行，不阻塞主循环：主循环检测 rollbackActive 后协作空转避免鼠标操作冲突
                    void StopSmoothRotate()
                    {
                        if (!Volatile.Read(ref smoothRotateRequested))
                        {
                            return; // 未在旋转中，无需停止与回转
                        }
                        Volatile.Write(ref smoothRotateRequested, false);
                        Volatile.Write(ref rollbackActive, true);
                        Task.Run(() =>
                        {
                            try
                            {
                                using (var cap = CaptureToRectArea())
                                {
                                    double stopAngle = CameraOrientation.Compute(cap.SrcMat);
                                    double targetAngle = stopAngle - visConfig.ChascaRollbackAngle;
                                    int stepX = Volatile.Read(ref smoothStepX);
                                    var rollbackStart = DateTime.UtcNow;
                                    while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - rollbackStart).TotalSeconds < 3)
                                    {
                                        using (var curCap = CaptureToRectArea())
                                        {
                                            double cur = CameraOrientation.Compute(curCap.SrcMat);
                                            double diff = cur - targetAngle;
                                            if (diff > 180) diff -= 360;
                                            else if (diff < -180) diff += 360;
                                            if (Math.Abs(diff) <= 5)
                                            {
                                                break; // 到达目标点 ±5° 内，退出回转
                                            }
                                            // diff>0 需要左转（负力度），diff<0 过头需右转（正力度）
                                            int dir = diff > 0 ? -1 : 1;
                                            Simulation.SendInput.Mouse.MoveMouseBy(dir * stepX, (int)(visConfig.ChascaPressStrength * dir * stepX * 0.194));
                                        }
                                        Sleep(16, avatar.Ct);
                                    }
                                    Logger.LogInformation("恰斯卡特化：平滑转动停止，往回转补偿（目标 {Target:F0}°±5°）", targetAngle);
                                }
                            }
                            finally
                            {
                                Volatile.Write(ref rollbackActive, false);
                            }
                        });
                    }

                    while (!avatar.Ct.IsCancellationRequested)
                    {
                        // 退出条件1：20秒超时
                        if ((DateTime.UtcNow - startTime).TotalSeconds >= 20)
                        {
                            Logger.LogInformation("恰斯卡特化退出：20秒超时，开始落地");
                            LandChasca();
                            break;
                        }

                        using (var capture = CaptureToRectArea())
                        {
                            // 本帧识别结果绘制列表（受"绘制识别结果"配置控制，参考桑多涅特化逻辑）
                            var drawResults = visConfig.DrawRecognitionResults;
                            var drawList = new System.Collections.Generic.List<View.Drawable.RectDrawable>();

                            // 测试用：开启"自动保存截图"后，本帧截图保存到 log\screenshot\（与截图快捷键保存路径一致）
                            if (visConfig.ChascaAutoSaveScreenshot)
                            {
                                try
                                {
                                    var shotDir = Global.Absolute(@"log\screenshot\");
                                    Directory.CreateDirectory(shotDir);
                                    Cv2.ImWrite(Path.Combine(shotDir, $@"{DateTime.Now:yyyyMMddHHmmssffff}.png"), capture.SrcMat);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogDebug("恰斯卡自动保存截图失败: {Message}", ex.Message);
                                }
                            }

                            // 退出条件2：不处于飞行状态（已下车）
                            if (!ChascaIsFlyingByPixel(capture.SrcMat))
                            {
                                // 已下车：松开左键并等待 300ms 即可
                                Logger.LogInformation("恰斯卡特化退出：不处于飞行状态（已下车）");
                                Simulation.SendInput.Mouse.LeftButtonUp();
                                Sleep(300, avatar.Ct);
                                break;
                            }

                            // 获取当前视角朝向（每帧计算，用于下方累计旋转判定），
                            // 仅当平滑旋转活跃（请求标志为 true）时才记录到序列：暂停段的静止样本不入列，
                            // 保证旋转器评估窗口内的视角变化完全由平滑旋转自身产生
                            var angle = CameraOrientation.Compute(capture.SrcMat);
                            if (Volatile.Read(ref smoothRotateRequested))
                            {
                                lock (orientationHistory)
                                {
                                    orientationHistory.Add((angle, DateTime.UtcNow));
                                }
                            }

                            // 往回转补偿进行中：本帧协作空转（不识别不旋转），避免主循环的鼠标操作与回转循环冲突
                            if (Volatile.Read(ref rollbackActive))
                            {
                                // 空转帧无识别结果：提交空列表清空上帧绘制，避免画面移动后框错位
                                View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("ChascaSpecialized", drawList);
                                Sleep(frameIntervalMs, avatar.Ct);
                                continue;
                            }
                            // 累计旋转（距上次识别到目标后重新计数）：相邻帧角度差归一化到 (-180,180] 后累加
                            float delta = 0;
                            if (prevAngle.HasValue)
                            {
                                delta = angle - prevAngle.Value;
                                if (delta > 180) delta -= 360;
                                else if (delta < -180) delta += 360;
                                cumulativeRotation += delta;
                            }

                            // 红色箭头检测：仅平滑水平旋转活跃（旋转器线程正在旋转）或向上旋转中执行，避免无谓的每帧识别开销
                            if (Volatile.Read(ref smoothRotateRequested) || verticalRotateActive)
                            {
                                var arrowRects = new List<OpenCvSharp.Rect>();
                                var redArrows = AvatarRecognition.FindRedArrowAngles(capture, arrowRects);
                                if (drawResults)
                                {
                                    foreach (var rect in arrowRects)
                                    {
                                        drawList.Add(capture.ToRectDrawable(rect, "chasca_arrow", _targetPen));
                                    }
                                }

                                if (verticalRotateActive)
                                {
                                    // 向上旋转中：任一箭头仍在上方区间 → 继续向上一步（步进力度沿用水平断点，主循环50ms帧间隔
                                    // 相对旋转器16ms步进约慢3倍）；否则视为箭头消失，连续3帧后退出（路径2：恢复平滑水平旋转）
                                    if (redArrows.Count > 0 && redArrows.Any(a => a >= -112.5 && a <= -67.5))
                                    {
                                        arrowLostFrames = 0;
                                        Simulation.SendInput.Mouse.MoveMouseBy(0, -Volatile.Read(ref smoothStepX));
                                    }
                                    else
                                    {
                                        arrowLostFrames++;
                                        if (arrowLostFrames >= 3)
                                        {
                                            verticalRotateActive = false;
                                            arrowLostFrames = 0;
                                            Volatile.Write(ref smoothRotateRequested, true); // 箭头消失，恢复平滑水平旋转
                                        }
                                    }
                                }
                                else
                                {
                                    // 平滑水平旋转活跃：
                                    // 1) 下方检测：所有红色箭头处于正下方45度（正下±22.5度）区间 → 计数+1，否则-1（下限0），
                                    //    超过阈值（默认20，约90度）时清零并执行一次强力下压（约5倍"伤害数字位于屏幕最下方"的瞄准力度）
                                    if (redArrows.Count > 0 && redArrows.All(a => a >= 67.5 && a <= 112.5))
                                    {
                                        downArrowCounter++;
                                    }
                                    else
                                    {
                                        downArrowCounter = Math.Max(0, downArrowCounter - 1);
                                    }
                                    if (downArrowCounter > visConfig.ChascaDownArrowPressThreshold)
                                    {
                                        downArrowCounter = 0;
                                        Simulation.SendInput.Mouse.MoveMouseBy(0, (int)(675 * AssetScale * dpi));
                                    }
                                    // 2) 上方检测：任一箭头处于正上方45度（正上±22.5度）区间 → 敌人很可能在上方，
                                    //    暂停平滑水平旋转，开始向上旋转；被血条/伤害数字/子弹事件打断（路径1，不再恢复旋转）
                                    //    或箭头连续3帧消失（路径2，恢复水平旋转）时退出
                                    if (redArrows.Count > 0 && redArrows.Any(a => a >= -112.5 && a <= -67.5))
                                    {
                                        Volatile.Write(ref smoothRotateRequested, false);
                                        verticalRotateActive = true;
                                        arrowLostFrames = 0;
                                        Simulation.SendInput.Mouse.MoveMouseBy(0, -Volatile.Read(ref smoothStepX));
                                    }
                                }
                            }

                            // 血条识别：区分传奇血条与普通血条
                            // FindBloodBars 内部自动更新传奇血条跨帧追踪（与持续索敌共用静态追踪器，
                            // 开启持续索敌时跨帧识别信息可保留，此处不清空追踪器）
                            // 提前到校准块之前：退出条件4需跟踪传奇血条出现状态
                            var bars = AvatarRecognition.FindBloodBars(capture);
                            var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();
                            var hasLegendaryBar = valid.Any(b => AvatarRecognition.IsLegendaryBar(b.x, b.y));

                            // 退出条件4状态维护：记录传奇血条最后出现时间
                            if (hasLegendaryBar)
                            {
                                legendaryBarLastSeen = DateTime.UtcNow;
                            }

                            // 自适应旋转力度：对 实测角度÷使用力度 的比例滑动取中位数，据此预测当前力度的单次旋转角并校准
                            // 中位数对异常值（角度识别误差、画面抖动导致的离群测量）稳健，窗口避免无限累积导致调节迟钝
                            // 预期单次旋转角度：由配置"恰斯卡单次旋转角度"决定（默认 50°），传奇血条与无血条场景校准目标一致
                            // 调节与补转分离：每次旋转后先按中位数预测调节力度（向目标角度收敛），
                            // 再判断实测角度是否超容差（<60% 或 >130%），超差则用角度差值补转并跳过后续步骤
                            if (rotatedLastFrame)
                            {
                                var actual = Math.Abs(delta);
                                if (actual > 1 && rotateXUsed > 0) // 忽略噪声级角度差（画面稳定后无操作时接近 0）
                                {
                                    // 实测角度÷使用力度：每像素力度产生的旋转角度
                                    angleRatios.Add(actual / rotateXUsed);
                                    if (angleRatios.Count > 5)
                                    {
                                        angleRatios.RemoveAt(0);
                                    }
                                    var sorted = angleRatios.OrderBy(r => r).ToArray();
                                    var medianRatio = sorted[sorted.Length / 2];
                                    lastMedianRatio = medianRatio; // 供无血条连续旋转换算固定角度力度
                                    // 预期单次旋转角度：由配置决定（默认 50°）
                                    double expected = rotateStepAngle;
                                    // 按中位数比例预测当前力度的单次旋转角度，并向预期收敛（始终执行，避免力度停在旧值反复补转）
                                    var predicted = medianRatio * rotateX;
                                    if (predicted < expected)
                                    {
                                        double factor = Math.Clamp(expected / predicted, 1.0, 5.0);
                                        rotateX *= factor;
                                        Logger.LogInformation("自适应旋转角：预测单次{Predicted:F2}°，将旋转力度调整为{Factor:F2}倍", predicted, factor);
                                    }
                                    else if (predicted > expected)
                                    {
                                        double factor = Math.Clamp(expected / predicted, 0.2, 1.0);
                                        rotateX *= factor;
                                        Logger.LogInformation("自适应旋转角：预测单次{Predicted:F2}°，将旋转力度调整为{Factor:F2}倍", predicted, factor);
                                    }
                                    // 实际角度与预期偏差过大：跳过后续步骤，先用角度差值补转
                                    if (actual < expected * 0.6 || actual > expected * 1.3)
                                    {
                                        // 角度差值（正=向右补转，负=向左回补），用中位数比例换算为水平力度
                                        double diff = expected - actual;
                                        double compensateX = diff / medianRatio;
                                        Logger.LogInformation("自适应旋转角：实测{Actual:F2}°偏离预期{Expected:F0}°，补转{Diff:F2}°", actual, expected, diff);
                                        rotatedLastFrame = false; // 补转结果不计入下一次校准
                                        Simulation.SendInput.Mouse.MoveMouseBy((int)compensateX, (int)(visConfig.ChascaPressStrength * compensateX * 0.194));
                                        Sleep(frameIntervalMs, avatar.Ct); // 补转后额外等待一个帧间隔
                                        prevAngle = angle; // 补转后更新基准角度，保证下一帧累计旋转正确
                                        View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("ChascaSpecialized", drawList);
                                        continue; // 跳过后续步骤（血条/伤害/子弹识别与稳定旋转），重新截图
                                    }
                                }
                                rotatedLastFrame = false;
                            }
                            prevAngle = angle;

                            if (valid.Count > 0 && !hasLegendaryBar)
                            {
                                // 存在普通血条且不存在传奇血条：参考桑多涅逻辑，瞄准最近血条中心
                                // 中心点使用 1080p 的 (960,300)，将敌人置于屏幕偏上位置（恰斯卡相对俯视）
                                continuousRotating = false; // 再次看到血条，重置连续旋转状态
                                Volatile.Write(ref smoothRotateRequested, false); // 再次看到血条，停止平滑旋转
                                verticalRotateActive = false; // 打断向上旋转（路径1：索敌已成功，不再恢复旋转）
                                var preAimX = (int)(960 * AssetScale);
                                var preAimY = (int)(300 * AssetScale);
                                var nearest = valid.OrderBy(b =>
                                    Math.Abs((b.x + b.width / 2) - preAimX) +
                                    Math.Abs((b.y + b.height / 2) - preAimY)).First();
                                var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                                var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                                // 单次旋转力度为桑多涅逻辑的四分之三（0.35×0.75、0.25×0.75，原四分之一翻3倍）
                                Simulation.SendInput.Mouse.MoveMouseBy(
                                    (int)(offsetX * 0.2625 * dpi), (int)(offsetY * 0.1875 * dpi));
                                // 叠加层：最近血条绿色粗框（target），其余血条红色细框（blood），与桑多涅特化一致
                                if (drawResults)
                                {
                                    foreach (var b in valid)
                                    {
                                        var rect = new OpenCvSharp.Rect(b.x, b.y, b.width, b.height);
                                        bool isTarget = b.x == nearest.x && b.y == nearest.y &&
                                                        b.width == nearest.width && b.height == nearest.height;
                                        drawList.Add(capture.ToRectDrawable(rect,
                                            isTarget ? "target" : "blood",
                                            isTarget ? _targetPen : null));
                                    }
                                }
                                cumulativeRotation = 0; // 识别到普通血条，累计旋转重新计数
                            }
                            else
                            {
                                // 存在传奇血条 或 无任何血条：做伤害数字识别，瞄准有效伤害数字
                                // 中心点使用 1080p 的 (960,360)，力度系数可配置（见 ChascaAimForceX/Y）
                                // 排除恰斯卡 6 个子弹槽位区域，避免彩色元素图标被误判为伤害数字
                                var damageResult = AvatarRecognition.FindDamageNumber(capture, ChascaBulletRects);
                                if (damageResult.HasValue)
                                {
                                    continuousRotating = false; // 再次看到伤害数字，重置连续旋转状态
                                    Volatile.Write(ref smoothRotateRequested, false); // 再次看到伤害数字，停止平滑旋转
                                    verticalRotateActive = false; // 打断向上旋转（路径1：索敌已成功，不再恢复旋转）
                                    var (dcx, dcy, _, dx, dy, dw, dh) = damageResult.Value;
                                    Simulation.SendInput.Mouse.MoveMouseBy(
                                        (int)((dcx - (int)(960 * AssetScale)) * visConfig.ChascaAimForceX * dpi),
                                        (int)((dcy - (int)(360 * AssetScale)) * visConfig.ChascaAimForceY * dpi));
                                    if (drawResults)
                                    {
                                        // 叠加层：伤害数字区域绿色粗框
                                        drawList.Add(capture.ToRectDrawable(
                                            new OpenCvSharp.Rect(dx, dy, dw, dh),
                                            "damage_target",
                                            _targetPen));
                                    }
                                    lastEventTime = DateTime.UtcNow; // 识别到伤害数字，视为活动事件
                                    cumulativeRotation = 0; // 识别到伤害数字，累计旋转重新计数
                                }
                                else
                                {
                                    // 无目标（无血条且无伤害数字）：依赖子弹状态判断当前是否需要移动视角
                                    // 子弹识别与喷射检测仅在无目标分支执行，血条/伤害数字可见时短路跳过

                                    // 恰斯卡子弹识别：六个槽位的元素状态（空/风/火/水/雷/冰）
                                    var bullets = RecognizeChascaBullets(capture, visConfig.ChascaBulletThreshold);
                                    // 每帧输出识别到的子弹序列（元素名）
                                    string[] elementNames = ["空", "风", "火", "水", "雷", "冰"];
                                    Logger.LogInformation("恰斯卡子弹序列: {Bullets}", string.Join(",", bullets.Select(b => elementNames[(int)b])));

                                    // 子弹框识别：子弹框不存在（<0.5）时视为正在进行子弹喷射，更新时间
                                    if (ChascaIsSpraying(capture))
                                    {
                                        Logger.LogInformation("检测到子弹发射");
                                        lastEventTime = DateTime.UtcNow;
                                        cumulativeRotation = 0; // 识别到子弹喷射，累计旋转重新计数
                                        stableTimeMultiplier = 2; // 喷射后下一次稳定时间判定阈值翻倍
                                        // 识别到子弹喷射：快速下压一次（力度可配置），内置 1 秒冷却（硬编码）；
                                        // 仅本帧没有找到普通血条或伤害数字时执行（进入本分支前伤害数字已判空，
                                        // valid.Count == 0 即本帧无普通血条，避免传奇+普通血条共存时误下压）；
                                        // 先下压再停止平滑旋转，避免与回转循环并发移动鼠标
                                        if (valid.Count == 0 && (DateTime.UtcNow - lastSprayPressTime).TotalSeconds >= 1)
                                        {
                                            lastSprayPressTime = DateTime.UtcNow;
                                            Simulation.SendInput.Mouse.MoveMouseBy(0, (int)(visConfig.ChascaSprayPressForce * dpi));
                                        }
                                        StopSmoothRotate(); // 子弹喷射中，停止平滑旋转并往回转补偿
                                        verticalRotateActive = false; // 打断向上旋转（路径1：索敌已成功，不再恢复旋转）
                                    }
                                    else
                                    {
                                        // 子弹变化跟踪：当前帧序列与全部历史序列比较，无相同序列则更新时间，
                                        // 历史序列不足数量时追加，否则替换最旧的历史序列
                                        bool seqSame = false;
                                        foreach (var seq in bulletSeqs)
                                        {
                                            if (ChascaSeqEquals(bullets, seq)) { seqSame = true; break; }
                                        }
                                        if (!seqSame)
                                        {
                                            lastEventTime = DateTime.UtcNow;
                                            cumulativeRotation = 0; // 子弹序列变化，累计旋转重新计数
                                            StopSmoothRotate(); // 子弹序列变化，停止平滑旋转并往回转补偿
                                            verticalRotateActive = false; // 打断向上旋转（路径1：索敌已成功，不再恢复旋转）
                                            if (bulletSeqs.Count < seqSlotCount)
                                            {
                                                bulletSeqs.Add(bullets);
                                                bulletSeqTimes.Add(DateTime.UtcNow);
                                            }
                                            else
                                            {
                                                // 替换最旧的历史序列（记录时间最早者）
                                                int oldestIdx = 0;
                                                for (int k = 1; k < bulletSeqs.Count; k++)
                                                {
                                                    if (bulletSeqTimes[k] < bulletSeqTimes[oldestIdx]) oldestIdx = k;
                                                }
                                                bulletSeqs[oldestIdx] = bullets;
                                                bulletSeqTimes[oldestIdx] = DateTime.UtcNow;
                                            }
                                        }
                                    }

                                    // 旋转索敌：无血条时进入连续旋转模式——第一次由稳定时间触发后不再等待稳定间隔，
                                    // 每帧旋转"单次旋转角度的一半"直到再次看到血条或伤害数字（识别处重置 continuousRotating）
                                    // 传奇血条存在（有目标）：保持稳定时间判定，单次旋转"单次旋转角度"（自适应力度）
                                    // 勾选"恰斯卡平滑转动"时以上两种旋转均被平滑转动取代；
                                    // 向上旋转中（verticalRotateActive）跳过本节，避免与主循环的向上旋转并发抢鼠标
                                    if (smoothRotateEnabled && !verticalRotateActive)
                                    {
                                        // 平滑转动：超过稳定时间（传奇血条存在时还须过起飞后不旋转观察期）后置旋转请求标志，
                                        // 由独立异步旋转循环持续小步旋转（间隔较小、角度较小，转速自适应调节），无需间隔等待
                                        if ((!hasLegendaryBar || (DateTime.UtcNow - startTime).TotalSeconds >= visConfig.ChascaNoRotateBeforeSeconds) &&
                                            (DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime * stableTimeMultiplier)
                                        {
                                            if (!Volatile.Read(ref smoothRotateRequested))
                                            {
                                                // 平滑旋转启动：仅在首次进入时初始化步进力度作为自适应过渡起点，
                                                // 按当前校准力度换算约 10° 步进（无校准样本时取初始力度 10%）；
                                                // 暂停后恢复时沿用上次保存的力度断点（不重置，由 EMA 继续调节）
                                                if (!smoothStepInitialized)
                                                {
                                                    double stepDeg = 10;
                                                    Volatile.Write(ref smoothStepX,
                                                        (int)Math.Max(1, lastMedianRatio > 0 ? stepDeg / lastMedianRatio : rotateX * 0.1));
                                                    smoothStepInitialized = true;
                                                    Logger.LogInformation("恰斯卡特化：平滑转动开始（每步约{F0}°，转速自适应调节）", stepDeg);
                                                }
                                            }
                                            Volatile.Write(ref smoothRotateRequested, true);
                                        }
                                    }
                                    else if (!hasLegendaryBar)
                                    {
                                        if (!continuousRotating)
                                        {
                                            // 未开始连续旋转：等待稳定时间后触发第一次旋转（单次旋转角度的一半）
                                            if ((DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime * stableTimeMultiplier)
                                            {
                                                Logger.LogInformation("恰斯卡特化：无血条且无伤害数字，开始连续旋转索敌（每帧{F0}°）", rotateStepAngle / 2);
                                                RotateStep(rotateStepAngle / 2);
                                                continuousRotating = true;
                                                stableTimeMultiplier = 1;
                                            }
                                        }
                                        else
                                        {
                                            // 连续旋转：每帧转"单次旋转角度的一半"，不等待稳定间隔
                                            RotateStep(rotateStepAngle / 2);
                                        }
                                    }
                                    else
                                    {
                                        // 传奇血条存在：重置连续旋转状态，按稳定时间单次旋转"单次旋转角度"
                                        continuousRotating = false;
                                        // 传奇血条存在时，起飞后前 chascaNoRotateBeforeSeconds 秒不执行旋转
                                        //（开局观察期，默认 1 秒，配置为 0 时立即按稳定时间旋转）
                                        if ((DateTime.UtcNow - startTime).TotalSeconds >= visConfig.ChascaNoRotateBeforeSeconds &&
                                            (DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime * stableTimeMultiplier)
                                        {
                                            rotatedLastFrame = true; // 下一帧用实测旋转角度自适应校准力度
                                            rotateXUsed = rotateX; // 记录本次旋转实际使用的力度，供下一帧计算 角度÷力度 比例
                                            Simulation.SendInput.Mouse.MoveMouseBy((int)rotateX, (int)(visConfig.ChascaPressStrength * rotateX * 0.194));
                                            lastEventTime = DateTime.UtcNow; // 上一次旋转
                                            stableTimeMultiplier = 1; // 本次翻倍判定已生效，恢复正常阈值
                                            Sleep(frameIntervalMs * 2, avatar.Ct);
                                        }
                                    }
                                }
                            }

                            // 退出条件3：距上次识别到伤害数字或血条后，旋转超过一圈（360°）
                            // 依赖本帧朝向的累计旋转，放在截图块内（朝向记录之后）
                            // 传奇血条存在时不触发（持续索敌中，不应因旋转超一圈而落地）；
                            // 无血条连续旋转模式转满一圈仍无目标时落地兜底（识别到血条/伤害数字会重置累计）
                            if (!hasLegendaryBar && Math.Abs(cumulativeRotation) >= 360)
                            {
                                Logger.LogInformation("恰斯卡特化退出：累计旋转超过一圈（{Rotation:F0}°），开始落地", cumulativeRotation);
                                LandChasca();
                                break;
                            }

                            // 退出条件4：传奇血条曾出现且连续1.5秒未出现 → 下车
                            if (legendaryBarLastSeen.HasValue && (DateTime.UtcNow - legendaryBarLastSeen.Value).TotalSeconds >= 1.5)
                            {
                                Logger.LogInformation("恰斯卡特化退出：传奇血条连续1.5秒未出现，开始落地");
                                LandChasca();
                                break;
                            }

                            // 提交本帧识别结果叠加层（血条/伤害数字/红色箭头框，受"绘制识别结果"配置控制）
                            View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("ChascaSpecialized", drawList);
                        }

                        // 每帧末尾等待帧间间隔
                        Sleep(frameIntervalMs);
                    }

                    return true;
                    }
                    finally
                    {
                        // 取消平滑旋转独立异步循环，避免旋转器在第二步结束后继续旋转/泄漏
                        smoothRotateCts.Cancel();
                        try { smoothRotateTask.Wait(1000); } catch (Exception) { }
                        // 清除识别结果叠加层（与桑多涅特化一致，防止退出后残留绘制）
                        View.Drawable.VisionContext.Instance().DrawContent.RemoveRect("ChascaSpecialized");
                        // 保证异常路径下左键与 E 键均释放，避免按键卡住
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                        // 整个恰斯卡特化逻辑结束后，最终点按一次鼠标中键（退出骑乘）
                        Simulation.SendInput.Mouse.MiddleButtonClick();
                    }
                }
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Attack 普攻特化分派
    /// </summary>
    private static bool ExecuteAttackSpecialized(Avatar avatar, string character, int ms)
    {
        switch (character)
        {
            // 阿蕾奇诺：普攻特化（契量状态机）
            case "阿蕾奇诺":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var visConfig = AvatarRecognition.GetVisualRecognitionConfig();
                    var c2Enabled = visConfig.ArlecchinoC2Enabled;
                    var bondStrengthenLine = visConfig.ArlecchinoBondStrengthenLine; // 契量低于此视为空契/无强化
                    var bondRefreshECooldown = visConfig.ArlecchinoBondRefreshECooldown;          // 契空+Q可用时 E 剩余 CD 超过此值才放 Q（秒）
                    var bondChargeAmount = visConfig.ArlecchinoBondChargeAmount;      // 重击收契量（%）：携带专武 155，否则 130
                    var normalAttackLoop = visConfig.ArlecchinoNormalAttackLoop;      // 普攻动作循环（战斗策略语言，| 分隔多序列，空则用内置普攻闪A）
                    var debugLogEnabled = visConfig.ArlecchinoDebugLogEnabled;        // 调试日志开关
                    var fightEndCheckRound = visConfig.ArlecchinoFightEndCheckRound;  // 战斗结束检查轮次（每 N 轮一次；0 不检查）
                    var dpi = TaskContext.Instance().DpiScale;

                    // 重击前允许的最大契量（避免溢出浪费），由收契量自动推导：契上限 200% − 本次回收量
                    var chargeThreshold = 200 - bondChargeAmount;

                    // 解析普攻动作循环：按 | 分隔多套序列；解析失败或为空时回退内置普攻闪A
                    List<List<CombatCommand>> attackLoopSequences = [];
                    if (!string.IsNullOrWhiteSpace(normalAttackLoop))
                    {
                        try
                        {
                            foreach (var seq in normalAttackLoop.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                var commands = CombatScriptParser.ParseLinePart(seq, avatar.Name);
                                if (commands.Count > 0)
                                {
                                    attackLoopSequences.Add(commands);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.LogWarning("阿蕾奇诺普攻动作循环解析失败，回退内置普攻闪A：{Message}", e.Message);
                            attackLoopSequences = [];
                        }
                    }

                    // 本次 Attack 调用内的状态
                    // 上一次放 E 的时刻存于战斗参数（AutoFightParam.ArlecchinoLastETime），跨多次 attack 调用保留，
                    // 用于 2 命以下重击收契的 5 秒等待判断
                    var fightParam = AvatarRecognition.CurrentAutoFightParam;
                    var lastETime = fightParam?.ArlecchinoLastETime ?? DateTime.MinValue;
                    var eAfterActionDone = true;        // 本次 E 后是否已完成重击或放 Q（初值为 true：首个 E 之前无需先重击）
                    var startTime = DateTime.UtcNow;    // 循环起始时间（超时判断用 ms 时长）
                    var loopRound = 0;                  // 循环轮次计数（战斗结束检查用）
                    var lastDebugLogTime = DateTime.MinValue; // 调试日志节流

                    while ((DateTime.UtcNow - startTime).TotalMilliseconds < ms)
                    {
                        if (avatar.Ct is { IsCancellationRequested: true })
                        {
                            return true;
                        }

                        loopRound++;

                        // 战斗结束检查：每 N 轮触发一次（N>0 才检查）
                        if (fightEndCheckRound > 0 && loopRound % fightEndCheckRound == 0)
                        {
                            var finishConfig = new AutoFightTask.TaskFightFinishDetectConfig(fightParam.FinishDetectConfig);
                            if (AutoFightTask.CheckFightFinish(finishConfig, avatar.Ct).Result)
                            {
                                Logger.LogInformation("阿蕾奇诺普攻特化：检测到战斗结束，提前退出");
                                return true;
                            }
                        }

                        // 读契量与红血（契区域统一截取一次）
                        using var capture = CaptureToRectArea();
                        var bondPercent = MeasureBondPercent(capture);
                        var redBlood = IsArlecchinoRedBlood(capture);

                        // 契量状态判断
                        var isBondEmpty = bondPercent < bondStrengthenLine; // 契量低于强化线 = 空契（无强化普攻）

                        var eReady = avatar.IsSkillReady();      // E 是否就绪
                        var eRemainingCd = avatar.GetSkillCdSeconds(); // E 剩余 CD（秒）
                        var now = DateTime.UtcNow;

                        // 调试日志：每 500ms 节流输出一次
                        if (debugLogEnabled && (now - lastDebugLogTime).TotalMilliseconds >= 500)
                        {
                            lastDebugLogTime = now;
                            Logger.LogInformation("阿蕾奇诺普攻特化：契量 {BondPercent:F1}%，红血={RedBlood}，E就绪={EReady}，E-CD={ERemainingCd:F1}s，Q就绪={QReady}",
                                bondPercent, redBlood, eReady, eRemainingCd, avatar.IsBurstReady);
                        }

                        // ① 红血 且 Q 可用 → 放 Q 回血（Q 会清空契并使 E 的 CD 归零）
                        if (redBlood && avatar.IsBurstReady)
                        {
                            avatar.UseBurst();
                            eAfterActionDone = true;
                            Sleep(200, avatar.Ct);
                            continue;
                        }

                        // ② 契空 且 Q 可用 且 E 剩余 CD 超过配置阈值 → 放 Q 刷新 E
                        if (isBondEmpty && avatar.IsBurstReady && eRemainingCd > bondRefreshECooldown)
                        {
                            avatar.UseBurst();
                            eAfterActionDone = true;
                            Sleep(200, avatar.Ct);
                            continue;
                        }

                        // ③ E 已就绪：本 E 后未完成重击/放 Q → 无条件先重击（腾出印记窗口）再放 E
                        if (eReady && !eAfterActionDone)
                        {
                            avatar.Charge(ChargeMs);
                            eAfterActionDone = true;
                            Sleep(100, avatar.Ct);
                            continue;
                        }

                        // ③' E 已就绪 且 本 E 后已完成重击/放 Q → 直接放 E 挂印记
                        if (eReady && eAfterActionDone)
                        {
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                            if (fightParam != null)
                            {
                                fightParam.ArlecchinoLastETime = now; // 记录放 E 时刻（战斗内保留）
                            }
                            lastETime = now;
                            eAfterActionDone = false; // 本次 E 后尚未重击/放 Q，禁止直接连续 E
                            Sleep(100, avatar.Ct);
                            continue;
                        }

                        // ④ 正常收契重击：契量不超过上限、本 E 未重击、且（2命 或 E 后已满 5 秒）
                        if (!eAfterActionDone
                            && bondPercent <= chargeThreshold
                            && (c2Enabled || (now - lastETime).TotalSeconds >= 5))
                        {
                            avatar.Charge(ChargeMs);
                            eAfterActionDone = true;
                            Sleep(100, avatar.Ct);
                            continue;
                        }

                        // 默认分支：配置了普攻动作循环则按序列执行（多序列每轮切换），否则内置普攻闪A
                        if (attackLoopSequences.Count > 0)
                        {
                            var seq = attackLoopSequences[loopRound % attackLoopSequences.Count];
                            foreach (var cmd in seq)
                            {
                                cmd.Execute(avatar);
                            }
                        }
                        else
                        {
                            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                            Sleep(100, avatar.Ct);
                        }
                    }

                    return true;
                }
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// 阿蕾奇诺重击收契的时长（毫秒），供契量状态机调用 avatar.Charge。
    /// </summary>
    private const int ChargeMs = 450;

    /// <summary>
    /// 红血检测：血条中心区域连通域判断是否红血。
    /// 区域与颜色阈值照抄参考 CombatHealthDetector（基于 1080p 基准）。
    /// </summary>
    private static bool IsArlecchinoRedBlood(ImageRegion ra)
    {
        const int x = 808, y = 1009, w = 3, h = 3;
        var lower = new Scalar(255, 90, 89);
        var upper = new Scalar(255, 91, 90);
        return CountConnectedRegions(ra, x, y, w, h, lower, upper) > 1;
    }

    /// <summary>
    /// 契量识别：返回契量百分比（0~200%）。
    /// 契包裹在血条上、上下各一层；在契区域内按契色二值化后，
    /// 上、下两个分隔连通域各自取 x 跨度，求均值后按契区域宽度归一化为契量。
    /// 契量 = 200% × 上下连通域 x 长度均值 / 契区域宽度。
    /// </summary>
    private static double MeasureBondPercent(ImageRegion ra)
    {
        // 契区域：X=812, W=295（→ 812~1107）；Y/H 照抄参考（契上下包裹血条，Y 覆盖两层）
        const int bondX = 812, bondW = 295, bondY = 1000, bondH = 20;
        // 契颜色 #FF8C89 ≈ BGR(255,140,137)；用容差小的参考阈值降低误判
        var lower = new Scalar(243, 132, 128);
        var upper = new Scalar(255, 156, 152);

        using var bondRect = ra.DeriveCrop(bondX, bondY, bondW, bondH);
        using var mask = OpenCvCommonHelper.Threshold(bondRect.SrcMat, lower, upper);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

        // 统计所有契色连通域（label>=1）的 x 跨度（width），取均值作为契量跨度
        double totalWidth = 0;
        int count = 0;
        if (numLabels > 1)
        {
            // stats 每行: [left, top, width, height, area]，label 从 1 开始
            for (int i = 1; i < numLabels; i++)
            {
                var w = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                totalWidth += w;
                count++;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        var avgWidth = totalWidth / count;
        return avgWidth / bondW * 200.0;
    }

    /// <summary>
    /// 连通域个数统计工具：裁剪区域→颜色阈值二值化→连通域计数。
    /// 返回连通域总数（含背景 label 0）。
    /// </summary>
    private static int CountConnectedRegions(ImageRegion ra, int x, int y, int w, int h, Scalar lower, Scalar upper)
    {
        using var rect = ra.DeriveCrop(x, y, w, h);
        using var mask = OpenCvCommonHelper.Threshold(rect.SrcMat, lower, upper);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        return Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);
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
    /// 子弹框特征评分低于 0.5 判定为子弹框不存在（正在喷射）
    /// </summary>
    private static bool ChascaIsSpraying(ImageRegion capture)
    {
        return ImageFeatureScorer.Score(ChascaFeatureModelLoader.BulletBoxModel, capture.SrcMat) < 0.5;
    }

    /// <summary>
    /// 恰斯卡飞行子弹识别：识别全部六个槽位（供日志完整输出），对每个槽位用对应元素模型评分，
    /// 取最高且超过阈值的元素（阈值由配置指定，默认 0.5）；该槽位没有任何可用模型（缺失）时直接返回空（0）
    /// 序列变化比较时忽略槽位 1 和槽位 6（见 ChascaSeqEquals）
    /// </summary>
    private static ChascaBulletType[] RecognizeChascaBullets(ImageRegion capture, double threshold)
    {
        var result = new ChascaBulletType[6];
        for (int pos = 0; pos < result.Length; pos++)
        {
            double bestScore = 0;
            ChascaBulletType bestType = ChascaBulletType.Empty;
            bool hasModel = false;
            for (int elemIdx = 0; elemIdx < 5; elemIdx++)
            {
                var model = ChascaFeatureModelLoader.GetBulletModel(pos, elemIdx);
                if (model == null) continue; // 缺失的元素模型不参与评分
                hasModel = true;
                double score = ImageFeatureScorer.Score(model, capture.SrcMat);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestType = (ChascaBulletType)(elemIdx + 1);
                }
            }
            // 该槽位无模型或最高分未过阈值：直接判定为空（返回 0）
            result[pos] = !hasModel || bestScore < threshold ? ChascaBulletType.Empty : bestType;
        }
        return result;
    }

    /// <summary>
    /// 子弹序列比较：只比较槽位 2-5（忽略槽位 1 与槽位 6，两者受子弹填充规则限制信息量低）；
    /// 空槽位与风元素视为等价（两者视觉特征易混淆，避免误判触发序列变化）
    /// </summary>
    private static bool ChascaSeqEquals(ChascaBulletType[] a, ChascaBulletType[] b)
    {
        if (a.Length != b.Length) return false;
        // 槽位 1、6 不参与比较，只比较索引 1-4
        for (int i = 1; i < a.Length - 1; i++)
        {
            var x = a[i] == ChascaBulletType.Empty ? ChascaBulletType.Anemo : a[i];
            var y = b[i] == ChascaBulletType.Empty ? ChascaBulletType.Anemo : b[i];
            if (x != y) return false;
        }
        return true;
    }
}

/// <summary>
/// 特化动作参数（由动作类型决定哪些字段生效）
/// </summary>
/// <param name="Hold">UseSkill 是否长按</param>
/// <param name="Ms">Charge 持续时间（毫秒）</param>
public sealed record ActionArgs(bool Hold = false, int Ms = 0);
