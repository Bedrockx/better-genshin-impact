using BetterGenshinImpact.GameTask.AutoFriendship.Model;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFriendship;

/// <summary>
/// AutoFriendship 核心执行逻辑（纯函数类）
/// 
/// 从 AutoFriendshipTask 中提取的纯决策逻辑，不包含任何 UI、日志、异步操作。
/// 此类专注于状态转换和决策，便于单元测试。
/// 
/// 对应 JS 脚本 main.js 的执行流程：
/// 1. switchPartyIfNeeded
/// 2. 如果是盗宝团且启用了原住民清理：执行 AutoPath("盗宝团-准备") + AutoFight
/// 3. 执行 preparePath 导航
/// 4. 进入主循环 AutoFriendshipDev
/// </summary>
public static class AutoFriendshipExecutionLogic
{
    /// <summary>
    /// 执行单轮好感任务的核心决策逻辑
    /// 
    /// 对应 JS executeSingleFriendshipRound 和 C# ExecuteSingleFriendshipRoundAsync
    /// 
    /// 执行流程：
    /// 1. 导航到触发点
    /// 2. 掉落检测（如果启用了 loopTillNoExpOrMora）
    /// 3. 首轮检测（仅首轮，鳄鱼有 initialDelayMs）
    /// 4. 首轮未检测到，执行 relogin/wonderlandCycle 后再检测
    /// 5. 仍未检测到，终止任务
    /// 6. 导航到战斗点
    /// 7. 执行战斗（最多重试2次）
    ///    - 成功：runPostBattle() -> return true
    ///    - 失败：recoverAfterFailure() -> 重试
    ///    - 重试超限：容错处理 -> return true
    /// </summary>
    public static SingleRoundResult ExecuteSingleRound(SingleRoundInput input)
    {
        var state = new RoundExecutionState
        {
            RoundIndex = input.RoundIndex,
            NoExpOrMoraCount = input.NoExpOrMoraCount,
            DetectedExpOrMora = input.DetectedExpOrMora,
            ConsecutiveMaxRetryCount = input.ConsecutiveMaxRetryCount,
            IsRunning = true
        };

        // 步骤1: 导航到触发点（纯逻辑：记录需要导航）
        state.NavigationTarget = NavigationTarget.TriggerPoint;

        // 步骤2: 掉落检测
        if (!state.DetectedExpOrMora && input.Config.LoopTillNoExpOrMora)
        {
            state.NoExpOrMoraCount++;
            if (state.NoExpOrMoraCount >= 2)
            {
                return new SingleRoundResult
                {
                    Success = false,
                    Reason = RoundResultReason.ConsecutiveNoExpOrMora,
                    NoExpOrMoraCount = state.NoExpOrMoraCount,
                    ConsecutiveMaxRetryCount = state.ConsecutiveMaxRetryCount
                };
            }
        }
        else
        {
            state.NoExpOrMoraCount = 0;
            state.DetectedExpOrMora = false;
        }

        // 步骤3: 首轮检测（仅首轮，鳄鱼有 initialDelayMs）
        var initialDelayMs = GetInitialDelayMs(input.Config.EnemyType);
        bool? ocrStatus = null;

        if (state.RoundIndex == 0)
        {
            if (initialDelayMs > 0)
            {
                state.RequiresInitialDelay = true;
                state.InitialDelayMs = initialDelayMs;
            }

            ocrStatus = input.OcrDetectionResult;

            // 步骤4: 首轮未检测到，执行 relogin/wonderlandCycle 后再检测
            if (ocrStatus != true)
            {
                state.RequiresReloginOrWonderlandCycle = true;
                state.UseWonderlandCycle = input.Config.Use1000Stars;

                // 模拟二次检测
                ocrStatus = input.SecondOcrDetectionResult;
            }

            // 步骤5: 仍未检测到，终止任务
            if (ocrStatus != true)
            {
                return new SingleRoundResult
                {
                    Success = false,
                    Reason = RoundResultReason.TaskNotTriggered,
                    NoExpOrMoraCount = state.NoExpOrMoraCount,
                    ConsecutiveMaxRetryCount = state.ConsecutiveMaxRetryCount
                };
            }
        }

        // 步骤6: 导航到战斗点
        state.NavigationTarget = NavigationTarget.BattlePoint;

        // 步骤7: 执行战斗（最多重试2次）
        const int MaxRetryCount = 2;
        int retryCount = 0;

        while (true)
        {
            if (!state.IsRunning)
            {
                return new SingleRoundResult
                {
                    Success = false,
                    Reason = RoundResultReason.UserCancelled,
                    NoExpOrMoraCount = state.NoExpOrMoraCount,
                    ConsecutiveMaxRetryCount = state.ConsecutiveMaxRetryCount
                };
            }

            var battleResult = input.BattleResults[retryCount < input.BattleResults.Count ? retryCount : input.BattleResults.Count - 1];

            if (battleResult == BattleResultStatus.Success)
            {
                // 重置连续最大重试计数器
                state.ConsecutiveMaxRetryCount = 0;

                return new SingleRoundResult
                {
                    Success = true,
                    Reason = RoundResultReason.BattleSuccess,
                    NoExpOrMoraCount = state.NoExpOrMoraCount,
                    ConsecutiveMaxRetryCount = state.ConsecutiveMaxRetryCount,
                    RetryCount = retryCount
                };
            }

            // 战斗失败
            if (retryCount >= MaxRetryCount)
            {
                state.ConsecutiveMaxRetryCount++;

                if (state.ConsecutiveMaxRetryCount >= 2)
                {
                    return new SingleRoundResult
                    {
                        Success = false,
                        Reason = RoundResultReason.ConsecutiveMaxRetries,
                        NoExpOrMoraCount = state.NoExpOrMoraCount,
                        ConsecutiveMaxRetryCount = state.ConsecutiveMaxRetryCount
                    };
                }

                // 容错处理：传送至七天神像并切换队伍，然后进入下一轮
                return new SingleRoundResult
                {
                    Success = true,
                    Reason = RoundResultReason.FallbackRecovery,
                    NoExpOrMoraCount = state.NoExpOrMoraCount,
                    ConsecutiveMaxRetryCount = state.ConsecutiveMaxRetryCount,
                    RetryCount = retryCount,
                    RequiresTeleportToStatue = true,
                    RequiresSwitchParty = true
                };
            }

            // 恢复后重试
            retryCount++;
            state.RequiresRecovery = true;
            state.NavigationTarget = NavigationTarget.BattlePoint; // 重新导航到战斗点
        }
    }

    /// <summary>
    /// 获取初始延迟（仅鳄鱼有）
    /// </summary>
    public static int GetInitialDelayMs(EnemyType enemyType)
    {
        return enemyType switch
        {
            EnemyType.Crocodile => 5000,
            _ => 0 // 盗宝团、愚人众、蕈兽、雷萤术士都没有 initialDelayMs
        };
    }

    /// <summary>
    /// 判断是否需要执行清理战斗（盗宝团且启用了丘丘人清理）
    /// </summary>
    public static bool ShouldExecuteClearBattle(AutoFriendshipConfig config)
    {
        return config.EnemyType == EnemyType.HilichurlBrigade
               && config.QiuQiuRenTimeoutSeconds > 0;
    }

    /// <summary>
    /// 获取 preparePath 的路径名称
    /// </summary>
    public static string? GetPreparePathName(AutoFriendshipConfig config)
    {
        return config.EnemyType switch
        {
            EnemyType.HilichurlBrigade => "盗宝团-准备",
            _ => null // 其他敌人类型没有 preparePath
        };
    }

    /// <summary>
    /// 获取触发点路径名称
    /// </summary>
    public static string GetTriggerLocationName(EnemyType enemyType)
    {
        return enemyType switch
        {
            EnemyType.Fatui => "愚人众-触发",
            EnemyType.HilichurlBrigade => "盗宝团-触发",
            EnemyType.Crocodile => "鳄鱼-触发",
            EnemyType.Fungus => "蕈兽-触发",
            EnemyType.ElectroMage => "雷萤-触发",
            _ => throw new ArgumentException($"未知的敌人类型: {enemyType}")
        };
    }

    /// <summary>
    /// 获取战斗点路径名称
    /// </summary>
    public static string GetBattleLocationName(EnemyType enemyType)
    {
        return enemyType switch
        {
            EnemyType.Fatui => "愚人众-战斗",
            EnemyType.HilichurlBrigade => "盗宝团-战斗",
            EnemyType.Crocodile => "鳄鱼-战斗",
            EnemyType.Fungus => "蕈兽-战斗",
            EnemyType.ElectroMage => "雷萤-战斗",
            _ => throw new ArgumentException($"未知的敌人类型: {enemyType}")
        };
    }

    /// <summary>
    /// 获取失败返回路径
    /// </summary>
    public static string GetFailReturnPath(EnemyType enemyType)
    {
        return enemyType switch
        {
            EnemyType.Fatui => "愚人众-失败返回",
            EnemyType.HilichurlBrigade => "盗宝团-失败返回",
            EnemyType.Crocodile => "鳄鱼-失败返回",
            EnemyType.Fungus => "蕈兽-失败返回",
            EnemyType.ElectroMage => "雷萤-失败返回",
            _ => throw new ArgumentException($"未知的敌人类型: {enemyType}")
        };
    }

    /// <summary>
    /// 获取失败等待时间（毫秒）
    /// JS 版本中雷萤的 failReturnSleepMs 为 0，其他敌人为 5000
    /// </summary>
    public static int GetFailWaitTimeMs(EnemyType enemyType)
    {
        return enemyType switch
        {
            EnemyType.ElectroMage => 0,
            _ => 5000
        };
    }

    /// <summary>
    /// 获取战后路径名称（仅蕈兽有值）
    /// JS 版本中只有蕈兽有 postBattlePath ("蕈兽-战后")
    /// </summary>
    public static string? GetPostBattlePathName(AutoFriendshipConfig config)
    {
        return config.EnemyType switch
        {
            EnemyType.Fungus => "蕈兽-战后",
            _ => null
        };
    }

    /// <summary>
    /// 评估主循环是否应该继续
    /// </summary>
    public static bool ShouldContinueMainLoop(MainLoopState state)
    {
        if (!state.IsRunning) return false;
        if (state.CurrentRoundIndex >= state.TotalRounds && state.TotalRounds > 0) return false;
        if (state.Terminated) return false;

        return true;
    }

    /// <summary>
    /// 计算主循环进度
    /// </summary>
    public static MainLoopProgress CalculateProgress(MainLoopState state)
    {
        var elapsed = DateTime.Now - state.StartTime;
        var timePerTask = elapsed.TotalMilliseconds / (state.CompletedRounds + 1);
        var remainingTasks = state.TotalRounds - state.CompletedRounds - 1;
        var remainingTime = TimeSpan.FromMilliseconds(timePerTask * Math.Max(0, remainingTasks));
        var estimatedCompletion = DateTime.Now + remainingTime;

        return new MainLoopProgress
        {
            CurrentRound = state.CompletedRounds + 1,
            TotalRounds = state.TotalRounds,
            ProgressPercentage = state.TotalRounds > 0 ? (state.CompletedRounds + 1) / (double)state.TotalRounds * 100 : 0,
            ElapsedTime = elapsed,
            TimePerTask = TimeSpan.FromMilliseconds(timePerTask),
            RemainingTime = remainingTime,
            EstimatedCompletionTime = estimatedCompletion
        };
    }
}

/// <summary>
/// 单轮执行输入
/// </summary>
public class SingleRoundInput
{
    /// <summary>
    /// 当前轮次索引（从0开始）
    /// </summary>
    public int RoundIndex { get; set; }

    /// <summary>
    /// 配置
    /// </summary>
    public required AutoFriendshipConfig Config { get; set; }

    /// <summary>
    /// 连续未检测到经验或摩拉的次数
    /// </summary>
    public int NoExpOrMoraCount { get; set; }

    /// <summary>
    /// 是否已检测到经验或摩拉
    /// </summary>
    public bool DetectedExpOrMora { get; set; }

    /// <summary>
    /// 连续达到最大重试次数的次数
    /// </summary>
    public int ConsecutiveMaxRetryCount { get; set; }

    /// <summary>
    /// OCR 首次检测结果（null 表示非首轮）
    /// </summary>
    public bool? OcrDetectionResult { get; set; }

    /// <summary>
    /// OCR 二次检测结果（relogin/wonderlandCycle 后）
    /// </summary>
    public bool? SecondOcrDetectionResult { get; set; }

    /// <summary>
    /// 战斗结果序列（按重试次数索引）
    /// </summary>
    public List<BattleResultStatus> BattleResults { get; set; } = new();
}

/// <summary>
/// 单轮执行结果
/// </summary>
public class SingleRoundResult
{
    /// <summary>
    /// 本轮是否成功（返回 true 表示继续下一轮，false 表示终止）
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 结果原因（统一使用 RoundResultReason，Success=false 时也表示失败原因）
    /// </summary>
    public RoundResultReason? Reason { get; set; }

    /// <summary>
    /// 连续未检测到经验或摩拉的次数
    /// </summary>
    public int NoExpOrMoraCount { get; set; }

    /// <summary>
    /// 连续达到最大重试次数的次数
    /// </summary>
    public int ConsecutiveMaxRetryCount { get; set; }

    /// <summary>
    /// 当前重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 是否需要传送至七天神像
    /// </summary>
    public bool RequiresTeleportToStatue { get; set; }

    /// <summary>
    /// 是否需要切换队伍
    /// </summary>
    public bool RequiresSwitchParty { get; set; }

    /// <summary>
    /// 是否需要恢复操作
    /// </summary>
    public bool RequiresRecovery { get; set; }
}

/// <summary>
/// 轮次结果原因
/// </summary>
public enum RoundResultReason
{
    /// <summary>
    /// 战斗成功完成
    /// </summary>
    BattleSuccess,

    /// <summary>
    /// 容错恢复（传送至神像）
    /// </summary>
    FallbackRecovery,

    /// <summary>
    /// 连续两次未检测到经验或摩拉
    /// </summary>
    ConsecutiveNoExpOrMora,

    /// <summary>
    /// 任务未触发（OCR 未识别）
    /// </summary>
    TaskNotTriggered,

    /// <summary>
    /// 连续两次达到最大重试次数
    /// </summary>
    ConsecutiveMaxRetries,

    /// <summary>
    /// 用户取消
    /// </summary>
    UserCancelled
}

/// <summary>
/// 导航目标
/// </summary>
public enum NavigationTarget
{
    None,
    TriggerPoint,
    BattlePoint
}

/// <summary>
/// 轮次执行状态
/// </summary>
public class RoundExecutionState
{
    public int RoundIndex { get; set; }
    public int NoExpOrMoraCount { get; set; }
    public bool DetectedExpOrMora { get; set; }
    public int ConsecutiveMaxRetryCount { get; set; }
    public bool IsRunning { get; set; }
    public NavigationTarget NavigationTarget { get; set; }
    public bool RequiresInitialDelay { get; set; }
    public int InitialDelayMs { get; set; }
    public bool RequiresReloginOrWonderlandCycle { get; set; }
    public bool UseWonderlandCycle { get; set; }
    public bool RequiresRecovery { get; set; }
}

/// <summary>
/// 主循环状态
/// </summary>
public class MainLoopState
{
    public bool IsRunning { get; set; }
    public int CurrentRoundIndex { get; set; }
    public int TotalRounds { get; set; }
    public int CompletedRounds { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public bool Terminated { get; set; }
    public DateTime StartTime { get; set; }
}

/// <summary>
/// 主循环进度
/// </summary>
public class MainLoopProgress
{
    public int CurrentRound { get; set; }
    public int TotalRounds { get; set; }
    public double ProgressPercentage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan TimePerTask { get; set; }
    public TimeSpan RemainingTime { get; set; }
    public DateTime EstimatedCompletionTime { get; set; }
}
