using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightJsonTask : ISoloTask
{
    public string Name => "自动战斗(JSON策略)";

    private readonly AutoFightParam _taskParam;
    private readonly JsonCombatStrategy _strategy;
    private CancellationToken _ct;

    /// <summary>
    /// YOLO目标检测器（BgiWorld模型），用于战斗结束检测
    /// 当前未使用（战斗结束检测已委托到 AutoFightEndDetection），保留声明以与 TXT 策略保持一致
    /// 初始化条件：_taskParam.FightFinishDetectEnabled == true
    /// </summary>
    private readonly BgiYoloPredictor? _predictor;
    private DateTime _lastFightFlagTime = DateTime.Now;
    private int _skipCheckCounter;

    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;
    private readonly double _dpi = TaskContext.Instance().DpiScale;

    private static readonly object PickLock = new object();

    /// <summary>
    /// 当前队伍中的角色名集合（用于过滤动作节点）
    /// </summary>
    private HashSet<string> _teamCharacterNames = new(StringComparer.OrdinalIgnoreCase);

    // 日志防刷：1秒内同一动作名至多输出一次日志
    private string _lastLoggedActionName = "";
    private DateTime _lastLogTime = DateTime.MinValue;

    /// <summary>
    /// 上一次执行动作的红箭头对准开关（用于动作间状态转移：
    /// 前一个动作未开启 → 本动作开启时低头；前一个动作开启 → 本动作关闭时中键回正）。
    /// 初始 false，开战首个动作为 true 时自然触发低头。
    /// </summary>
    private bool _lastRedArrowAim;

    // 红箭头对准相关常量（真机标定后可调整，均未乘 dpi）
    private const int RedArrowLookDownPixels = 3000; // 动作开始前单次大位移把视角拉向最低俯视
    private const int RedArrowPauseMs = 50;          // 低头 / 回正操作后的等待时间

    // 旋转循环参考恰斯卡特化：独立异步旋转环，步进间隔 100ms 一次，
    // 每步水平旋转均按比例附带向下下压（见下压比例常量）；力度初始保守，
    // 之后按红箭头收敛效果乘法自适应调节（见 RedArrowAimLoopAsync），整体仍偏保守防越轴震荡
    private const int RedArrowStepIntervalMs = 100;   // 旋转环步进间隔（100ms 一次）
    private const double RedArrowInitialStepX = 30;   // 初始"每度像素"自适应力度（原 150，调为五分之一）
    private const double RedArrowMinStepX = 5;       // 每度像素下限（防过小空转）
    private const double RedArrowMaxStepX = 600;     // 每度像素上限（原 120，再翻 5 倍）
    private const double RedArrowTargetRatio = 0.33; // 目标每次旋转掉角度差值的 33%（剩 67% 下一帧继续，指数收敛）
    private const double RedArrowEmaNewWeight = 0.3; // EMA 新值权重：平滑后角度 = 0.7×旧 + 0.3×新（参考恰斯卡，抗单帧噪声）
    private const double RedArrowStepGain = 0.2;     // 每度力度乘法趋近步长指数：单步放大/缩小限制在 ±38%（Math.Pow(factor, 步长)）
    private const double RedArrowKeepDownRatio = 0.2; // 每步下压比例：下压 = 0.2 × 水平步进（参考恰斯卡 0.194 辅助比）
    private const double RedArrowLogIntervalMs = 500; // 红箭头索敌日志节流间隔（每 0.5 秒至多输出一条）

    /// <summary>
    /// 当前操作的角色名（私有状态，不污染全局 CurrentAvatarName）
    /// </summary>
    private string _currentAvatarName = "";

    /// <summary>
    /// 展开后的优先级动作条目
    /// 每个 JsonAction 展开为 1+N 个条目（1个主条件 + N个 morePriorities）
    /// </summary>
    private class PrioritizedAction
    {
        public JsonAction Action { get; set; }
        public string Expression { get; set; }
        public int Priority { get; set; }
    }

    // 战斗点位
    public static WaypointForTrack? FightWaypoint { get; set; } = null;

    private AutoFightTask.TaskFightFinishDetectConfig _finishDetectConfig;

    public AutoFightJsonTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _strategy = JsonCombatStrategyParser.ParseFile(_taskParam.CombatStrategyPath);

        if (_taskParam.FightFinishDetectEnabled)
        {
            _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
        }

        _finishDetectConfig = new AutoFightTask.TaskFightFinishDetectConfig(_taskParam.FinishDetectConfig);
    }

    /// <summary>
    /// 获取战斗场景，带重试机制
    /// 最多重试 5 次，每次间隔 1 秒
    /// </summary>
    /// <returns>初始化完成的战斗场景</returns>
    public CombatScenes GetCombatScenesWithRetry()
    {
        const int maxRetries = 5;
        var retryDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (combatScenes.CheckTeamInitialized())
            {
                return combatScenes;
            }

            if (attempt < maxRetries)
            {
                Thread.Sleep(retryDelayMs);
            }
        }
        throw new Exception("识别队伍角色失败（已重试 5 次）");
    }

    /// <summary>
    /// 启动自动战斗（JSON策略模式）
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        AvatarRecognition.SetCurrentAutoFightParam(_taskParam);
        AvatarRecognition.ClearLegendaryBarTracker();
        try
        {
            LogScreenResolution();
            var combatScenes = GetCombatScenesWithRetry();
    
            // 收集当前队伍角色名
            foreach (var avatar in combatScenes.GetAvatars())
            {
                _teamCharacterNames.Add(avatar.Name);
            }
            Logger.LogInformation("JSON 策略：当前队伍角色：{Names}", string.Join(", ", _teamCharacterNames));
    
            // 过滤可用动作：Character 为空（通用）或在当前队伍中
            var filteredActions = _strategy.Actions
                .Where(a => string.IsNullOrEmpty(a.Character) || _teamCharacterNames.Contains(a.Character))
                .ToList();
    
            // 展开为优先级条目：每个动作产生 1个主条目 + N个 morePriorities 条目
            var validActions = new List<PrioritizedAction>();
            foreach (var action in filteredActions)
            {
                validActions.Add(new PrioritizedAction
                {
                    Action = action,
                    Expression = action.Condition.Expression,
                    Priority = action.Index
                });
    
                foreach (var morePriority in action.MorePriorities)
                {
                    validActions.Add(new PrioritizedAction
                    {
                        Action = action,
                        Expression = morePriority.Expression,
                        Priority = morePriority.Priority
                    });
                }
            }
    
            // 按优先级排序（LINQ OrderBy 为稳定排序）：同优先级条目保持策略中的出现顺序，
            // 即动作声明顺序（每个动作的主条件条目在前、morePriorities 紧随其后，添加顺序即出现顺序）
            validActions = validActions
                .OrderBy(p => p.Priority)
                .ToList();
    
            Logger.LogInformation("JSON 策略：共 {Total} 个动作，展开为 {Expanded} 个优先级条目",
                _strategy.Actions.Count, validActions.Count);
    
            if (validActions.Count == 0)
            {
                Logger.LogWarning("JSON 策略：没有可用的动作节点，跳过战斗");
                return;
            }
    
            // 新的取消token
            var cts2 = new CancellationTokenSource();
            ct.Register(cts2.Cancel);
    
            combatScenes.BeforeTask(cts2.Token);
            // 设置初始当前角色名（用于无 Character 字段的通用 action 回退）
            _currentAvatarName = combatScenes.GetAvatars().FirstOrDefault()?.Name ?? _currentAvatarName;
            TimeSpan fightTimeout = TimeSpan.FromSeconds(_taskParam.Timeout);
            Stopwatch timeoutStopwatch = Stopwatch.StartNew();
    
            AutoFightSeek.RotationCount = 0;
            AutoFightTask.FightStatusFlag = true;
    
            var fightEndFlag = false;
            var timeOutFlag = false;
            string lastFightName = "";
    
            // 初始化条件求值器（传入策略动作名，供条件词法按名称合并连字符）
            var evaluator = new ConditionEvaluator(combatScenes, () => CaptureToRectArea(),
                _strategy.Actions.Where(a => !string.IsNullOrEmpty(a.Name)).Select(a => a.Name));
    
            // 基于经验值的战后拾取检测
            ExperienceDetector? expDetector = null;
            if (_taskParam.KazuhaPickupEnabled && _taskParam.ExpBasedPickupEnabled)
            {
                using var gameCaptureRegion = CaptureToRectArea();
                var expRos = AutoFightAssets.Get(gameCaptureRegion).ExperienceRecognitionObjects;
                expDetector = new ExperienceDetector(expRos, cts2.Token);
                expDetector.Start();
            }
    
            // 战斗前动作
            await RunPreActions(combatScenes, evaluator);
    
            // 战斗操作
            var fightTask = Task.Run(async () =>
            {
                try
                {
                    JsonAction? lastExecutedAction = null;
                    // 战斗开始时重置最近一次检查时间，供更快触发战斗结束检查判断间隔使用
                    AutoFightTask.LastFightFinishCheckTime = DateTime.Now;
                    // 记录开战时间，供"开战后一段时间阻断战斗结束检查"使用
                    AutoFightTask.FightStartTime = DateTime.Now;
                    // 每场新战斗重置"敌人可见时跳过战斗结束检查"的连续跳过计数
                    AutoFightTask.ResetSkipCheckCounter();
                    TimeSpan checkFightFinishTime = TimeSpan.FromSeconds(_finishDetectConfig.CheckTime); //检查战斗结束的超时时间

                    // 更快触发战斗结束检查（参照 txt 逻辑）：满足时间/人名条件时触发一次检查，无论是否发生换人；
                    // 未发生换人时没有切人动作提供后摇等待，因此仍需要应用前摇等待
                    // 返回是否检测到战斗结束
                    async Task<bool> FastCheckFightFinishAsync(string prevName, string actionName, bool afterSwitch = false)
                    {
                        if (_taskParam is not { FightFinishDetectEnabled: true } || !_finishDetectConfig.FastCheckEnabled)
                            return false;

                        // 本动作执行后的实际角色（无 Character 时沿用当前角色）
                        var checkAvatarName = string.IsNullOrEmpty(actionName) ? _currentAvatarName : actionName;

                        if ((_finishDetectConfig.CheckTime > 0 &&
                             (DateTime.Now - AutoFightTask.LastFightFinishCheckTime) > checkFightFinishTime)
                            || _finishDetectConfig.CheckNames.Contains(prevName))
                        {
                            // LastFightFinishCheckTime 由 CheckFightFinish 内部更新（动作中的 check 指令也会更新）
                            // 切人后检查：切人动作已包含上一个动作后摇的等待，前置延时缩短为 50ms，仅保留检测界面打开后的 DetectDelayTime；
                            // 若本动作未发生换人，则没有切人动作提供后摇等待，仍需应用前摇等待
                            int delayTime = _finishDetectConfig.DelayTime;
                            if (afterSwitch && checkAvatarName != prevName)
                            {
                                delayTime = 50;
                            }
                            else if (_finishDetectConfig.DelayTimes.TryGetValue(prevName, out var characterDelayTime))
                            {
                                delayTime = characterDelayTime;
                            }

                            var endFlag = await AutoFightTask.CheckFightFinish(_finishDetectConfig, _ct,
                                delayTime, _finishDetectConfig.DetectDelayTime);
                            if (endFlag)
                            {
                                Logger.LogInformation("{Name} 检测到战斗结束", actionName);
                                return true;
                            }
                        }
                        return false;
                    }

                    while (!cts2.Token.IsCancellationRequested)
                    {
                        if (timeoutStopwatch.Elapsed > fightTimeout)
                        {
                            Logger.LogInformation("战斗超时结束");
                            fightEndFlag = true;
                            timeOutFlag = true;
                            break;
                        }
    
                        // 每次循环开始：截图一次，供所有条件求值复用
                        using var capture = CaptureToRectArea();
                        evaluator.SetCachedCapture(capture);

                        var anyExecuted = false;

                        // 记录本轮循环开始时的角色，用于检测是否发生换人
                        var prevAvatarName = _currentAvatarName;

                        foreach (var prioritizedAction in validActions)
                            {
                                if (cts2.Token.IsCancellationRequested) break;
    
                                var action = prioritizedAction.Action;
    
                                // 求值条件表达式：当前动作序号（用于 since/last-exec/count 缺省指代本动作）传动作真实 Index，
                                // 不能传排序用的 Priority（MorePriority 条目的 Priority 与该动作 Index 不同，会导致缺省查询查不到记录）
                                var conditionMet = evaluator.Evaluate(
                                    prioritizedAction.Expression,
                                    action.Index,
                                    action.Character,
                                    action.Name);
    
                                if (!conditionMet)
                                {
                                    continue;
                                }

                                // 更快触发战斗结束检查（默认在切人前触发；开启"切人后再执行战斗结束检查"时改为切人后触发）
                                var fightEndDetected = false;

                                // 切人前检查（默认时机）
                                if (!_finishDetectConfig.CheckAfterSwitchAvatar)
                                {
                                    fightEndDetected = await FastCheckFightFinishAsync(prevAvatarName, action.Character);
                                }

                                // 指定角色的动作：执行前确保切换到该角色（战斗已结束时跳过切人）
                                if (!fightEndDetected && !string.IsNullOrEmpty(action.Character))
                                {
                                    var avatar = combatScenes.SelectAvatar(action.Character);
                                    if (avatar == null) continue;

                                    avatar.Switch();
                                    _currentAvatarName = action.Character;
                                }

                                // 切人后再执行战斗结束检查：复用切人等待上一个动作后摇的时间，检查无需再等待
                                if (_finishDetectConfig.CheckAfterSwitchAvatar)
                                {
                                    fightEndDetected = await FastCheckFightFinishAsync(prevAvatarName, action.Character, afterSwitch: true);
                                }

                                if (fightEndDetected)
                                {
                                    _fightEndFlag = true;
                                    // 战斗结束则跳过当前动作（切人、执行均不进行）
                                    break;
                                }
    
                                // 执行动作
                                await ExecuteAction(combatScenes, action);
    
                                // 确保E技能释放成功
                                if (action.EnsureCast)
                                {
                                    var characterName = string.IsNullOrEmpty(action.Character)
                                        ? _currentAvatarName
                                        : action.Character;
                                    var avatar = combatScenes.SelectAvatar(characterName);
                                    if (avatar != null)
                                    {
                                        var imageAfterAction = CaptureToRectArea();
                                        try
                                        {
                                            var retry = 5;
                                            while (!(await AutoFightSkill.AvatarSkillAsync(Logger, avatar, false, 1, _ct, imageAfterAction)) && retry > 0)
                                            {
                                                Logger.LogWarning("{Name} 未检测到技能冷却，重新执行", action.Name);
                                                // 防止在纳塔飞天或爬墙
                                                Simulation.ReleaseAllKey();
                                                Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                                                Simulation.SendInput.SimulateAction(GIActions.Drop);
                                                await Delay(200, _ct);
                                                // 重新执行整个动作
                                                await ExecuteAction(combatScenes, action);
                                                var previousImage = imageAfterAction;
                                                imageAfterAction = CaptureToRectArea();
                                                previousImage.Dispose();
                                                await Task.Delay(30, _ct);
                                                retry--;
                                            }
                                        }
                                        finally
                                        {
                                            imageAfterAction.Dispose();
                                        }
                                    }
                                }
    
                                evaluator.UpdateLastExecTime(action.Index, action.Name);
                                lastExecutedAction = action;
                                anyExecuted = true;
                                lastFightName = action.Character ?? "";
    
                                if (_fightEndFlag) break;

                                // 执行完第一个满足条件的动作后重新判断
                                break;
                            }
    
                        if (fightEndFlag || _fightEndFlag) break;
    
                        if (!anyExecuted)
                        {
                            await Delay(200, _ct);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine(e.StackTrace);
                    throw;
                }
                finally
                {
                    Simulation.ReleaseAllKey();
                    // 红箭头对准收尾：最后执行的动作仍开启对准（视角处于低头状态）→ 补一次中键回正，
                    // 避免拾取等后续流程在低头视角下进行；使用 CancellationToken.None 避免 token 已取消时 Delay 抛异常
                    if (_lastRedArrowAim)
                    {
                        try
                        {
                            Simulation.SendInput.Mouse.MiddleButtonClick();
                            await Delay(RedArrowPauseMs, CancellationToken.None);
                        }
                        catch (OperationCanceledException) { }
                        _lastRedArrowAim = false;
                    }
                    AutoFightTask.FightStatusFlag = false;
                }
            }, cts2.Token);
    
            // 在持续索敌循环启动前标记战斗进行中，避免索敌循环因 FightStatusFlag 仍为 false 而立即退出
            AutoFightTask.FightStatusFlag = true;
    
            // 启动持续索敌循环（异步后台运行，与战斗任务并发）
            // 使用独立的 CancellationTokenSource，以便在战后独立取消索敌循环，不影响 cts2 关联的其他组件（如 expDetector）
            using var targetingCts = CancellationTokenSource.CreateLinkedTokenSource(cts2.Token);
            Task? targetingTask = null;
            if (_taskParam.EnableCombatTargeting)
            {
                targetingTask = Task.Run(async () =>
                {
                    try
                    {
                        await AvatarRecognition.ContinuousTargetingLoopAsync(targetingCts.Token, () => !AutoFightTask.FightStatusFlag);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception e)
                    {
                        Logger.LogError(e, "持续索敌循环异常");
                    }
                }, targetingCts.Token);
            }
    
            try
            {
                await fightTask;
            }
            finally
            {
                // 战斗结束后（无论正常/异常），停止并等待索敌循环完成清理（ReleaseAllKey / MiddleButtonClick），
                // 避免其 finally 在拾取/切人过程中释放按键，干扰万叶E吸怪等操作
                if (targetingTask != null)
                {
                    await targetingCts.CancelAsync();
                    try { await targetingTask; } catch (OperationCanceledException) { }
                }
                AutoFightTask.FightStatusFlag = false;
            }
    
            try
            {
                // 基于经验值检测结果的拾取判断
                if (_taskParam.KazuhaPickupEnabled && _taskParam.ExpBasedPickupEnabled && expDetector != null)
                {
                    if (!expDetector.HasDetectedExperience)
                    {
                        Logger.LogInformation("基于经验值判断：等待经验值检测结果");
                        var waitMs = 1100;
                        while (!expDetector.HasDetectedExperience && waitMs > 0)
                        {
                            await Delay(100, _ct);
                            waitMs -= 100;
                        }
                    }
    
                    var shouldPickup = expDetector.HasDetectedExperience;
                    Logger.LogInformation("基于经验值判断：{Result} 战后拾取", shouldPickup ? "执行" : "不执行");
    
                    if (!shouldPickup)
                    {
                        if (_taskParam is { PickDropsAfterFightEnabled: true })
                        {
                            await new ScanPickTask().Start(_ct);
                        }
                        return;
                    }
                }
            }
            finally
            {
                if (expDetector != null)
                {
                    await expDetector.StopAsync();
                    expDetector.Dispose();
                }
            }
    
            // 战后拾取（完全参照 AutoFightTask）
            await PostFightPickup(combatScenes, timeOutFlag, lastFightName);
        }
        finally
        {
            AvatarRecognition.ClearCurrentAutoFightParam();
        }
    }

    private bool _fightEndFlag;

    /// <summary>执行单个 JSON 动作节点</summary>
    private async Task ExecuteAction(CombatScenes combatScenes, JsonAction action)
    {
        AvatarRecognition.SkipSeekScope? redArrowScope = null;
        CancellationTokenSource? redArrowCts = null;
        Task? redArrowTask = null;
        try
        {
            var character = string.IsNullOrEmpty(action.Character)
                ? _currentAvatarName
                : action.Character;

            // 红箭头索敌对准状态机
            if (action.RedArrowAim)
            {
                // 整个动作期间独占视角：关闭持续索敌循环（参考恰斯卡特化 BeginExclusiveOperation 用法）
                redArrowScope = AvatarRecognition.BeginExclusiveOperation();

                // 前一个动作未开启（含开战首动作）→ 动作开始前单次大位移把视角拉到最低俯视
                if (!_lastRedArrowAim)
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(0, (int)(RedArrowLookDownPixels * _dpi));
                    await Delay(RedArrowPauseMs, _ct);
                }

                // 动作进行期间启动异步旋转循环
                redArrowCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
                redArrowTask = Task.Run(() => RedArrowAimLoopAsync(redArrowCts.Token), redArrowCts.Token);
            }
            else if (_lastRedArrowAim)
            {
                // 前一个动作开启、当前未开启 → 点按一次鼠标中键回正视角
                Simulation.SendInput.Mouse.MiddleButtonClick();
                await Delay(RedArrowPauseMs, _ct);
            }
            _lastRedArrowAim = action.RedArrowAim;

            var commands = CombatScriptParser.ParseLinePart(action.Action, character);

            // 执行前输出日志
            LogActionOnce(action.Name);

            CombatCommand? lastSubCmd = null;
            foreach (var cmd in commands)
            {
                if (_ct.IsCancellationRequested) break;

                cmd.Execute(combatScenes, lastSubCmd);
                lastSubCmd = cmd;

                if (_fightEndFlag) break;

                // 仅由 check 指令触发战斗结束检测
                if (cmd.Method == Method.Check && _taskParam.FightFinishDetectEnabled)
                {
                    _fightEndFlag = await AutoFightTask.CheckFightFinish(_finishDetectConfig, _ct,
                        _finishDetectConfig.DelayTime, _finishDetectConfig.DetectDelayTime);
                    if (_fightEndFlag)
                    {
                        Logger.LogInformation("{Name} 检测到战斗结束", action.Name);
                        break;
                    }
                }
            }

            // 更新当前角色名，供后续无指定角色动作使用
            _currentAvatarName = character;
        }
        catch (Exception e)
        {
            Logger.LogError("自动战斗：{Name} 执行失败：{Msg}", action.Name, e.Message);
        }
        finally
        {
            // 停止并等待红箭头对准循环，释放排他作用域
            if (redArrowCts != null)
            {
                redArrowCts.Cancel();
                if (redArrowTask != null)
                {
                    try { await redArrowTask; } catch (OperationCanceledException) { }
                }
                redArrowCts.Dispose();
            }
            redArrowScope?.Dispose();
            Simulation.ReleaseAllKey();
        }
    }

    /// <summary>
    /// 红箭头对准循环：动作执行期间持续旋转视角，使顶部（-90°）对准红箭头。
    /// 旋转逻辑参考恰斯卡特化：独立异步旋转环（每 100ms 一步）。
    /// 反馈指标为红箭头与顶部的夹角（不使用视角朝向）：
    ///   1. 每帧识别红箭头，始终以最接近顶部（-90°）的箭头作为目标；
    ///   2. 单次旋转目标角度 = 当前夹角的 33%（每次吃掉 33% 差异，剩 67% 下一帧继续，指数收敛逼近顶部）；
    ///   3. 单次旋转力度（px）= 目标角度 × 每度像素系数 stepX（自适应力度，随收敛效果乘法调节）；
    ///   4. 自适应用 EMA 逐步调节（参考恰斯卡平滑转动，不依赖最近一次、不跳变）：
    ///      a) 相邻帧箭头夹角差（≈本次实际旋转角）做指数移动平均平滑，抗单帧识别噪声；
    ///      b) 用 平滑后实际角 与 目标角（夹角的 33%）求比例因子 factor，
    ///         力度按 current×factor^步长 逐步趋近（单步受限）而非一次性跳变，向目标收敛；
    ///   5. 每步均附带按比例向下下压，保持视角最低。
    /// 注意：方向符号需真机标定（原神视角旋转方向与图像角度符号的关系无法静态确定）。
    /// </summary>
    private async Task RedArrowAimLoopAsync(CancellationToken ct)
    {
        // 每度像素系数（自适应力度）：初始保守，之后按红箭头收敛效果用 EMA 逐步调节
        double stepX = RedArrowInitialStepX;
        double? lastAngle = null; // 上一帧目标箭头角度，用于估算本次实际旋转角
        double emaActual = 0;     // 本次实际旋转角的指数移动平均（EMA 平滑，抗单帧识别噪声）
        var lastLogTime = DateTime.MinValue; // 日志节流：每 0.5 秒至多输出一条

        while (!ct.IsCancellationRequested)
        {
            using (var capture = CaptureToRectArea())
            {
                var angles = AvatarRecognition.FindRedArrowAngles(capture);
                if (angles.Count > 0)
                {
                    // 始终以最接近屏幕顶部（-90°）的箭头作为目标
                    double bestAngle = 0, bestDiff = double.MaxValue;
                    foreach (var a in angles)
                    {
                        double diff = Math.Abs(AngleDiffDeg(a, -90));
                        if (diff < bestDiff)
                        {
                            bestDiff = diff;
                            bestAngle = a;
                        }
                    }

                    // 当前夹角（红箭头与顶部的角度差）
                    double deviation = AngleDiffDeg(bestAngle, -90);
                    double absDev = Math.Abs(deviation);

                    // EMA 自适应调节（参考恰斯卡平滑转动）：逐步、平滑地调节每度力度，不依赖最近一次、不跳变
                    if (lastAngle.HasValue && absDev > 0.5)
                    {
                        // 本次实际旋转角（相邻帧目标箭头角差，≈相机旋转给箭头带来的角度变化）
                        double actual = Math.Abs(AngleDiffDeg(bestAngle, lastAngle.Value));
                        if (actual > 0.5 && stepX > RedArrowMinStepX)
                        {
                            // a) 对实际旋转角做指数移动平均平滑（新值权重 RedArrowEmaNewWeight）
                            emaActual = emaActual > 0 ? emaActual * (1 - RedArrowEmaNewWeight) + actual * RedArrowEmaNewWeight : actual;
                            // b) 目标角 = 当前夹角的 33%
                            double targetDeg = absDev * RedArrowTargetRatio;
                            //    因子 = 目标角 / 平滑后实际角：实际角大于目标 → factor<1 → 逐步调小力度；反之逐步调大
                            double factor = Math.Clamp(targetDeg / Math.Max(emaActual, 0.01), 0.2, 5.0);
                            if (Math.Abs(factor - 1) > 0.1)
                            {
                                //    力度按 current×factor^步长 逐步趋近（单步受限，防跳变/震荡）
                                stepX = Math.Clamp(stepX * Math.Pow(factor, RedArrowStepGain), RedArrowMinStepX, RedArrowMaxStepX);
                            }
                        }
                    }

                    // 单次旋转力度（px）= 目标角度（夹角的 33%）× 每度像素系数
                    double appliedStep = absDev * RedArrowTargetRatio * stepX;
                    int dir = deviation >= 0 ? 1 : -1; // 顶部右侧（deviation>0）→ 右移视角使其回归顶部
                    // 日志节流：每 0.5 秒至多输出一条，列出全部红箭头位置、角度差值、当前自适应旋转力度
                    if ((DateTime.Now - lastLogTime).TotalMilliseconds >= RedArrowLogIntervalMs)
                    {
                        lastLogTime = DateTime.Now;
                        var posStr = string.Join("，", angles.Select(a => a.ToString("F1")));
                        Logger.LogInformation("红箭头位置：{Pos}，角度差值 {Diff:F1}°，当前自适应旋转力度 {Adaptive:F0}",
                            posStr, absDev, stepX);
                    }
                    Simulation.SendInput.Mouse.MoveMouseBy(
                        (int)(appliedStep * dir * _dpi),
                        (int)(RedArrowKeepDownRatio * appliedStep * _dpi));
                    lastAngle = bestAngle;
                }
                else
                {
                    // 未识别到红箭头：仅重置角度反馈基准（lastAngle/emaActual），
                    // 力度 stepX 是整场战斗的自适应状态，需全程保留（dpi 等不中途变），不在此重置；
                    // 期间仅向下下压保持视角最低
                    lastAngle = null;
                    emaActual = 0;
                    if ((DateTime.Now - lastLogTime).TotalMilliseconds >= RedArrowLogIntervalMs)
                    {
                        lastLogTime = DateTime.Now;
                        Logger.LogInformation("红箭头位置：无，角度差值 -，当前自适应旋转力度 {Adaptive:F0}", stepX);
                    }
                    Simulation.SendInput.Mouse.MoveMouseBy(0, (int)(RedArrowInitialStepX * RedArrowTargetRatio * RedArrowKeepDownRatio * _dpi));
                }
            }
            await Task.Delay(RedArrowStepIntervalMs, ct);
        }
    }

    /// <summary>
    /// 角度差归一化到 (-180, 180]（度）
    /// </summary>
    private static double AngleDiffDeg(double a, double b)
    {
        double d = a - b;
        while (d > 180) d -= 360;
        while (d <= -180) d += 360;
        return d;
    }

    /// <summary>日志防刷：同一动作名在1秒内至多输出一次日志</summary>
    private void LogActionOnce(string actionName)
    {
        if (actionName == _lastLoggedActionName && (DateTime.Now - _lastLogTime).TotalSeconds < 1)
        {
            return;
        }
        _lastLoggedActionName = actionName;
        _lastLogTime = DateTime.Now;
        Logger.LogInformation("自动战斗：{Name}", actionName);
    }

    /// <summary>执行战斗前动作</summary>
    private async Task RunPreActions(CombatScenes combatScenes, ConditionEvaluator evaluator)
    {
        if (_strategy.Info.PreActions == null || _strategy.Info.PreActions.Count == 0)
            return;

        Logger.LogInformation("JSON 策略：执行战斗前动作");
        using var capture = CaptureToRectArea();
        evaluator.SetCachedCapture(capture);

        foreach (var preAction in _strategy.Info.PreActions)
        {
            if (_ct.IsCancellationRequested) break;

            var firstSpaceIndex = preAction.IndexOf(' ');
            var character = _currentAvatarName;
            var commands = preAction;
            if (firstSpaceIndex > 0)
            {
                character = preAction[..firstSpaceIndex];
                commands = preAction[(firstSpaceIndex + 1)..];
            }

            var cmdList = CombatScriptParser.ParseLineCommands(commands, character);
            var combatScript = new CombatScript([character], cmdList);

            try
            {
                await CombatScriptExecutor.ExecuteAsync(combatScript, _ct, Logger, combatScenes);
            }
            catch (RetryException e)
            {
                Logger.LogWarning("战斗前动作重试异常，跳过此动作继续：{Msg}", e.Message);
            }
            Logger.LogInformation("战斗前动作：{Action}", preAction);
            await Delay(300, _ct);
        }
    }

    /// <summary>战后拾取</summary>
    private async Task PostFightPickup(CombatScenes combatScenes, bool timeOutFlag, string lastFightName)
    {
        if (_taskParam.KazuhaPickupEnabled)
        {
            var picker = combatScenes.SelectAvatar("枫原万叶") ?? combatScenes.SelectAvatar("琴");

            string? oldPartyName = null;
            if (RunnerContext.Instance.PartyName is not null)
            {
                oldPartyName = RunnerContext.Instance.PartyName;
            }
            else if (picker is null && !string.IsNullOrEmpty(_taskParam.KazuhaPartyName))
            {
                Logger.LogWarning("换队拾取：当前队伍名称为空，尝试读取！");
                await Delay(1000, _ct);
                await _returnMainUiTask.Start(_ct);

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
                    var enterGameAppear = await NewRetry.WaitForElementAppear(
                        ElementRecognition.Get("PartyBtnChooseView"),
                        () => { },
                        _ct,
                        15,
                        500
                    );
                    if (attempt == 5 && !enterGameAppear)
                    {
                        Logger.LogWarning("换队拾取：读取队伍名称失败，跳过换队拾取步骤");
                        return;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_taskParam.KazuhaPartyName))
            {
                await Delay(1000, _ct);

                var timeWaitStart = 0;
                while (timeWaitStart < 6000)
                {
                    using var ra = CaptureToRectArea();
                    var partyViewBtn = ra.Find(ElementRecognition.Get("PartyBtnChooseView"));
                    if (partyViewBtn.IsExist())
                    {
                        var rawPartyName = ra.Find(new RecognitionObject
                        {
                            RecognitionType = RecognitionTypes.Ocr,
                            RegionOfInterest = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale),
                                partyViewBtn.Height)
                        }).Text;

                        if (string.IsNullOrWhiteSpace(rawPartyName))
                        {
                            oldPartyName = string.Empty;
                        }
                        else
                        {
                            var tempName = rawPartyName
                                .Replace("\"", "")
                                .Replace("\r\n", "")
                                .Replace("\r", "");

                            int firstNewLineIndex = tempName.IndexOf('\n');
                            if (firstNewLineIndex != -1)
                            {
                                tempName = tempName.Substring(0, firstNewLineIndex);
                            }

                            oldPartyName = tempName.Trim();
                        }

                        Logger.LogInformation("换队拾取：当前队伍名称读取为：{oldPartyName}", oldPartyName);
                        Logger.LogDebug("OCR原始识别文本（含转义）：{rawPartyName}", rawPartyName);
                        RunnerContext.Instance.PartyName = oldPartyName;
                        break;
                    }
                    await Delay(200, _ct);
                    timeWaitStart += 200;
                }
            }

            var switchPartyFlag = false;
            if (picker == null && !timeOutFlag && !string.IsNullOrEmpty(_taskParam.KazuhaPartyName) && oldPartyName != _taskParam.KazuhaPartyName)
            {
                try
                {
                    Logger.LogInformation($"切换为拾取队伍：{_taskParam.KazuhaPartyName}");
                    var success = await new SwitchPartyTask().Start(_taskParam.KazuhaPartyName, _ct);
                    if (success)
                    {
                        Logger.LogInformation($"成功切换队伍为{_taskParam.KazuhaPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = _taskParam.KazuhaPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        var cs = await RunnerContext.Instance.GetCombatScenes(_ct);
                        picker = cs.SelectAvatar("枫原万叶") ?? cs.SelectAvatar("琴");
                    }
                }
                catch (Exception e)
                {
                    Logger.LogWarning("切换队伍异常，跳过此步骤！{Msg}", e.Message);
                }
            }

            if (picker != null)
            {
                Simulation.ReleaseAllKey();

                if (picker.Name == "枫原万叶")
                {
                    var time = TimeSpan.FromSeconds(picker.GetSkillCdSeconds());

                    bool shouldSkip = lastFightName == picker.Name && time.TotalSeconds > 3;
                    bool forcePickup = _taskParam.QinDoublePickUp;

                    if (forcePickup || !shouldSkip)
                    {
                        Logger.LogInformation("使用 枫原万叶-长E 拾取掉落物");
                        if (picker.TrySwitch(10))
                        {
                            await Delay(100, _ct);
                            await picker.WaitSkillCd(_ct);
                            await SimulateHoldElementalSkillAsync(800, _ct);
                            await SimulateMouseLeftClickLoopAsync(6, _ct);
                            await Delay(1500, _ct);
                            picker.AfterUseSkill();
                        }
                    }
                    else
                    {
                        Logger.LogInformation("距最近一次万叶出招，时间过短，跳过此次万叶拾取！");
                    }
                }
                else if (picker.Name == "琴")
                {
                    Logger.LogInformation("使用 琴-长E 拾取掉落物");

                    var actionsToUse = PickUpCollectHandler.PickUpActions
                        .Where(action => action.StartsWith("琴-长E" + " ", StringComparison.OrdinalIgnoreCase))
                        .Select(action => action.Replace("琴-长E", "琴", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    var find = _taskParam.QinDoublePickUp;
                    if (picker.TrySwitch(10))
                    {
                        await Delay(100, _ct);
                        foreach (var miningActionStr in actionsToUse)
                        {
                            var pickUpAction = CombatScriptParser.ParseContext(miningActionStr);

                            for (int i = 0; i < 2; i++)
                            {
                                await picker.WaitSkillCd(_ct);
                                foreach (var command in pickUpAction.CombatCommands)
                                {
                                    command.Execute(combatScenes);
                                    Task.Run(() =>
                                    {
                                        if (Monitor.TryEnter(PickLock))
                                        {
                                            try
                                            {
                                                if (find)
                                                {
                                                    using (var imagePick = CaptureToRectArea())
                                                    {
                                                        if (imagePick.Find(AutoPickAssets.Get(imagePick, TaskContext.Instance().Config.AutoPickConfig.PickKey).PickRo).IsExist())
                                                        {
                                                            find = false;
                                                        }
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Monitor.Exit(PickLock);
                                            }
                                        }
                                    });
                                }

                                if (!find)
                                {
                                    break;
                                }

                                if (i == 0)
                                {
                                    Logger.LogInformation("自动拾取；尝试再次执行 琴-长E 拾取");
                                    picker.AfterUseSkill();
                                }
                                else
                                {
                                    break;
                                }
                            }

                            Simulation.ReleaseAllKey();
                        }
                    }
                }
            }

            if (switchPartyFlag && !string.IsNullOrEmpty(oldPartyName))
            {
                try
                {
                    Logger.LogInformation($"切换为原队伍：{oldPartyName}");
                    var success = await new SwitchPartyTask().Start(oldPartyName, _ct);
                    if (success)
                    {
                        Logger.LogInformation($"切换为原队伍{oldPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = oldPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        await RunnerContext.Instance.GetCombatScenes(_ct);
                    }
                }
                catch (Exception e)
                {
                    Logger.LogWarning("恢复原队伍失败，跳过此步骤！{Msg}", e.Message);
                }
            }
        }

        if (_taskParam is { PickDropsAfterFightEnabled: true })
        {
            await new ScanPickTask().Start(_ct);
        }
    }

    /// <summary>
    /// 检查并记录屏幕分辨率
    /// </summary>
    private void LogScreenResolution()
    {
        AssertUtils.CheckGameResolution("自动战斗");
    }
}
