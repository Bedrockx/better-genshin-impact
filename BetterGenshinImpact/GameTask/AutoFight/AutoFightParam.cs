using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;






public class AutoFightParam : BaseTaskParam<AutoFightTask>
{
    public class FightFinishDetectConfig
    {
        public bool FastCheckEnabled = false;
        public string FastCheckParams = "";
        public bool CheckAfterSwitchAvatar = false;
        public string CheckEndDelay = "";
        public string BeforeDetectDelay = "";
        public bool RotateFindEnemyEnabled = false;
        public bool SkipFightEndCheckWhenEnemyVisible = false;
        public double BlockCheckBeforeBattleSeconds = 0;
        public bool PaimonEndCheckEnabled = true;
        public double PaimonEndCheckDelay = 0.075;
    }

    public AutoFightParam(string path, AutoFightConfig autoFightConfig) : base(null, null)
    {
        CombatStrategyPath = path;
        ApplyConfig(autoFightConfig);
    }

    public FightFinishDetectConfig FinishDetectConfig { get; set; } = new();

    public string CombatStrategyPath { get; set; }

    public bool FightFinishDetectEnabled { get; set; } = false;
    public bool PickDropsAfterFightEnabled { get; set; } = false;
    public int PickDropsAfterFightSeconds { get; set; } = 15;
    public int BattleThresholdForLoot { get; set; } = -1;
    public int Timeout { get; set; } = 120;

    public bool KazuhaPickupEnabled = true;
    public string ActionSchedulerByCd = "";
    public string KazuhaPartyName;
    public string OnlyPickEliteDropsMode = "";
    public string GuardianAvatar { get; set; } = string.Empty;
    public bool GuardianCombatSkip { get; set; } = false;
    public bool GuardianAvatarHold = false;
    
    public bool CheckBeforeBurst { get; set; } = false;
    public bool IsFirstCheck { get; set; } = true;    
    public int RotaryFactor { get; set; } = 10;
    public bool BurstEnabled { get; set; } = false;
    
    public bool QinDoublePickUp { get; set; } = false;
    public static bool SwimmingEnabled  { get; set; } = false;
    /// <summary>
    /// 战斗中回点触发距离（游戏内距离，默认-1禁用；大于0时战斗中与战斗点距离超过该值则回点），仅 JSON 策略生效
    /// </summary>
    public double BackToFightDistance { get; set; } = -1;
    /// <summary>
    /// 单次回点超时（毫秒），整段回点动作（转向+移动）的总预算，默认2000，仅 JSON 策略生效
    /// </summary>
    public int BackToFightTimeoutMs { get; set; } = 2000;
    public bool EnableCombatTargeting { get; set; } = false;
    public int TargetingDetectionInterval { get; set; } = 50;
    public bool DrawRecognitionResults { get; set; } = true;
    public double LockLostWaitTime { get; set; } = 0.5;
    public double EndFightWhenNoTargetSeconds { get; set; } = 0;
    public double ChascaStableTime { get; set; } = 0.5;
    public bool ChascaAutoSaveScreenshot { get; set; } = false;
    public double ChascaNoRotateBeforeSeconds { get; set; } = 1;
    public double ChascaPressStrength { get; set; } = 1;
    public double ChascaInitialRotateX { get; set; } = 1000;
    public double ChascaBulletThreshold { get; set; } = 0.8;
    public int ChascaSequenceSlotCount { get; set; } = 2;
    public bool ChascaSmoothRotateEnabled { get; set; } = false;
    public double ChascaSmoothRotateSpeed { get; set; } = 80;
    public double ChascaRotateStepAngle { get; set; } = 50;
    public double ChascaAimForceX { get; set; } = 0.2625;
    public double ChascaAimForceY { get; set; } = 0.1875;
    public double ChascaSprayPressForce { get; set; } = 100;
    public double ChascaRollbackAngle { get; set; } = 15;
    public int ChascaDownArrowPressThreshold { get; set; } = 20;
    public bool ArlecchinoC2Enabled { get; set; } = true;
    public double ArlecchinoRefreshEBondThreshold { get; set; } = 40;
    public double ArlecchinoRefreshEMinCd { get; set; } = 8;
    public int ArlecchinoBondChargeThreshold { get; set; } = 55;
    /// <summary>
    /// 阿蕾奇诺：最近一次放 E 的时刻（战斗内跨多次 attack 调用保留，
    /// 用于 2 命以下重击收契的 5 秒等待判断）。由契量状态机更新读取。
    /// </summary>
    public System.DateTime ArlecchinoLastETime { get; set; } = System.DateTime.MinValue;
    /// <summary>
    /// 阿蕾奇诺：最近一次放 Q 的时刻（战斗内跨多次 attack 调用保留）。
    /// 用于 Q 释放后 5 秒内置冷却判断：5 秒内 Q 视为不可用，防止契空/红血时连续放 Q。由契量状态机更新读取。
    /// </summary>
    public System.DateTime ArlecchinoLastBurstTime { get; set; } = System.DateTime.MinValue;
    /// <summary>
    /// 阿蕾奇诺：最近一次重击收契/清印记的时刻（战斗内跨多次 attack 调用保留）。
    /// 用于重击内置 3 秒冷却（重击后 3 秒内不再重击）以及契空放 Q 需距重击超过 3 秒。由契量状态机更新读取。
    /// </summary>
    public System.DateTime ArlecchinoLastChargeTime { get; set; } = System.DateTime.MinValue;
    /// <summary>
    /// 阿蕾奇诺：E 的 5 秒强制冷却是否已被放 Q 刷新（战斗内跨多次 attack 调用保留）。
    /// 放 E 置 false；放 Q（含红血放 Q）置 true。true 时 E 强制冷却视为已清除、立即可放。
    /// </summary>
    public bool ArlecchinoECdRefreshedByQ { get; set; } = false;
    /// <summary>
    /// 阿蕾奇诺：E 挂印记后是否有印记可收取（战斗内跨多次 attack 调用保留）。
    /// true=当前 E 挂的印记尚未被收取（可由重击或 Q 消费一次）；false=无印记可收。
    /// 放 E 置 true；重击收契或放 Q 后置 false。由契量状态机更新读取。
    /// </summary>
    public bool ArlecchinoHasBondToCollect { get; set; } = false;
    /// <summary>
    /// 阿蕾奇诺：普攻动作循环（战斗策略语言，多个序列用 | 分隔）
    /// </summary>
    public string ArlecchinoNormalAttackLoop { get; set; } = "";
    /// <summary>
    /// 阿蕾奇诺：调试日志开关（每 500ms 输出一次契量状态 info 日志）
    /// </summary>
    public bool ArlecchinoDebugLogEnabled { get; set; } = false;
    /// <summary>
    /// 阿蕾奇诺：战斗结束检查轮次（每 N 轮检查一次；0 不检查）
    /// </summary>
    public int ArlecchinoFightEndCheckRound { get; set; } = 0;
    public DamageNumberRecognitionMode DamageNumberRecognitionMode { get; set; } = DamageNumberRecognitionMode.Color;

    /// <summary>
    /// 基于经验值判断是否执行战后拾取
    /// </summary>
    public bool ExpBasedPickupEnabled { get; set; } = false;

    public AutoFightParam(string? strategyName = null) : base(null, null)
    {
        SetCombatStrategyPath(strategyName);
        SetDefault();
    }

    /// <summary>  
    /// 设置战斗策略路径
    /// </summary>  
    /// <param name="strategyName">策略名称</param>  
    public void SetCombatStrategyPath(string? strategyName = null)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            strategyName = TaskContext.Instance().Config.AutoFightConfig.StrategyName;
        }

        if ("根据队伍自动选择".Equals(strategyName))
        {
            CombatStrategyPath =  Global.Absolute(@"User\AutoFight\");
        }
        else
        {
            CombatStrategyPath =  Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        }
    }

    /// <summary>
    /// 解析策略文件路径，自动检测 .json 或 .txt 扩展名。
    /// 优先检测 .json，未命中则回退 .txt。
    /// </summary>
    /// <param name="strategyName">策略名称（不含扩展名）</param>
    /// <returns>(完整路径, 类型标识: "json" / "txt")</returns>
    public static (string path, string type) ResolveStrategyPath(string strategyName)
    {
        if ("根据队伍自动选择".Equals(strategyName))
        {
            var dir = Global.Absolute(@"User\AutoFight\");
            return (dir, "txt");
        }

        var baseDir = Global.Absolute(@"User\AutoFight\");

        // 优先检测 .json
        var jsonPath = System.IO.Path.Combine(baseDir, strategyName + ".json");
        if (System.IO.File.Exists(jsonPath))
        {
            return (jsonPath, "json");
        }

        // 回退 .txt
        var txtPath = System.IO.Path.Combine(baseDir, strategyName + ".txt");
        return (txtPath, "txt");
    }

    public void SetDefault()
    {
        ApplyConfig(TaskContext.Instance().Config.AutoFightConfig);
    }

    private void ApplyConfig(AutoFightConfig autoFightConfig)
    {
        Timeout = autoFightConfig.Timeout;
        FightFinishDetectEnabled = autoFightConfig.FightFinishDetectEnabled;
        PickDropsAfterFightEnabled = autoFightConfig.PickDropsAfterFightEnabled;
        PickDropsAfterFightSeconds = autoFightConfig.PickDropsAfterFightSeconds;
        KazuhaPickupEnabled = autoFightConfig.KazuhaPickupEnabled;
        ActionSchedulerByCd = autoFightConfig.ActionSchedulerByCd;

        FinishDetectConfig.FastCheckEnabled = autoFightConfig.FinishDetectConfig.FastCheckEnabled;
        FinishDetectConfig.FastCheckParams = autoFightConfig.FinishDetectConfig.FastCheckParams;
        FinishDetectConfig.CheckAfterSwitchAvatar = autoFightConfig.FinishDetectConfig.CheckAfterSwitchAvatar;
        FinishDetectConfig.CheckEndDelay = autoFightConfig.FinishDetectConfig.CheckEndDelay;
        FinishDetectConfig.BeforeDetectDelay = autoFightConfig.FinishDetectConfig.BeforeDetectDelay;
        FinishDetectConfig.RotateFindEnemyEnabled = autoFightConfig.FinishDetectConfig.RotateFindEnemyEnabled;
        FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible = autoFightConfig.FinishDetectConfig.SkipFightEndCheckWhenEnemyVisible;
        FinishDetectConfig.BlockCheckBeforeBattleSeconds = autoFightConfig.FinishDetectConfig.BlockCheckBeforeBattleSeconds;
        FinishDetectConfig.PaimonEndCheckEnabled = autoFightConfig.FinishDetectConfig.PaimonEndCheckEnabled;
        FinishDetectConfig.PaimonEndCheckDelay = autoFightConfig.FinishDetectConfig.PaimonEndCheckDelay;

        KazuhaPartyName = autoFightConfig.KazuhaPartyName;
        OnlyPickEliteDropsMode = autoFightConfig.OnlyPickEliteDropsMode;
        BattleThresholdForLoot = autoFightConfig.BattleThresholdForLoot ?? BattleThresholdForLoot;

        GuardianAvatar = autoFightConfig.GuardianAvatar;
        GuardianCombatSkip = autoFightConfig.GuardianCombatSkip;
        GuardianAvatarHold = autoFightConfig.GuardianAvatarHold;
        BurstEnabled = autoFightConfig.BurstEnabled;
        CheckBeforeBurst = autoFightConfig.FinishDetectConfig.CheckBeforeBurst;
        IsFirstCheck = autoFightConfig.FinishDetectConfig.IsFirstCheck;
        RotaryFactor = autoFightConfig.FinishDetectConfig.RotaryFactor;
        SwimmingEnabled = autoFightConfig.SwimmingEnabled;
        QinDoublePickUp = autoFightConfig.QinDoublePickUp;
        EnableCombatTargeting = autoFightConfig.EnableCombatTargeting;
        TargetingDetectionInterval = autoFightConfig.TargetingDetectionInterval;
        DrawRecognitionResults = autoFightConfig.DrawRecognitionResults;
        LockLostWaitTime = autoFightConfig.LockLostWaitTime;
        EndFightWhenNoTargetSeconds = autoFightConfig.EndFightWhenNoTargetSeconds;
        ChascaStableTime = autoFightConfig.ChascaStableTime;
        ChascaAutoSaveScreenshot = autoFightConfig.ChascaAutoSaveScreenshot;
        ChascaNoRotateBeforeSeconds = autoFightConfig.ChascaNoRotateBeforeSeconds;
        ChascaPressStrength = autoFightConfig.ChascaPressStrength;
        ChascaInitialRotateX = autoFightConfig.ChascaInitialRotateX;
        ChascaBulletThreshold = autoFightConfig.ChascaBulletThreshold;
        ChascaSequenceSlotCount = autoFightConfig.ChascaSequenceSlotCount;
        ChascaSmoothRotateEnabled = autoFightConfig.ChascaSmoothRotateEnabled;
        ChascaSmoothRotateSpeed = autoFightConfig.ChascaSmoothRotateSpeed;
        ChascaRotateStepAngle = autoFightConfig.ChascaRotateStepAngle;
        ChascaAimForceX = autoFightConfig.ChascaAimForceX;
        ChascaAimForceY = autoFightConfig.ChascaAimForceY;
        ChascaSprayPressForce = autoFightConfig.ChascaSprayPressForce;
        ChascaRollbackAngle = autoFightConfig.ChascaRollbackAngle;
        ChascaDownArrowPressThreshold = autoFightConfig.ChascaDownArrowPressThreshold;
        ArlecchinoC2Enabled = autoFightConfig.ArlecchinoC2Enabled;
        ArlecchinoRefreshEBondThreshold = autoFightConfig.ArlecchinoRefreshEBondThreshold;
        ArlecchinoRefreshEMinCd = autoFightConfig.ArlecchinoRefreshEMinCd;
        ArlecchinoBondChargeThreshold = autoFightConfig.ArlecchinoBondChargeThreshold;
        ArlecchinoNormalAttackLoop = autoFightConfig.ArlecchinoNormalAttackLoop;
        ArlecchinoDebugLogEnabled = autoFightConfig.ArlecchinoDebugLogEnabled;
        ArlecchinoFightEndCheckRound = autoFightConfig.ArlecchinoFightEndCheckRound;
        DamageNumberRecognitionMode = autoFightConfig.DamageNumberRecognitionMode;
        ExpBasedPickupEnabled = autoFightConfig.ExpBasedPickupEnabled;
        BackToFightDistance = autoFightConfig.BackToFightDistance;
        BackToFightTimeoutMs = autoFightConfig.BackToFightTimeoutMs;
    }
}
