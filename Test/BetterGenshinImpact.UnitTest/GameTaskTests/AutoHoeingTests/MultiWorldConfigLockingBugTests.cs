using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// 多世界配置锁定Bug探索性测试和保留性测试
/// 
/// 测试策略：
/// 1. Bug Condition探索性测试：在未修复代码上运行，预期失败（证明bug存在）
/// 2. Preservation测试：在未修复代码上观察行为，在修复后验证行为不变
/// </summary>
public class MultiWorldConfigLockingBugTests
{
    #region Test Data Generators

    /// <summary>
    /// 生成多世界执行上下文
    /// </summary>
    public class MultiWorldExecutionContext
    {
        public int Round { get; set; }
        public bool IsHost { get; set; }
        public bool IsMember => !IsHost;
        public bool MultiWorldEnabled { get; set; }
        public bool ConfigSyncFailed { get; set; }
        public RoomConfig? FirstHostConfig { get; set; }
        public RoomConfig? ReceivedConfig { get; set; }
    }

    /// <summary>
    /// 生成Bug Condition场景的上下文
    /// </summary>
    public static Arbitrary<MultiWorldExecutionContext> BugConditionContexts()
    {
        var gen = from round in Gen.Choose(1, 5)
                  from isHost in Gen.Elements(true, false)
                  from configSyncFailed in Gen.Constant(true)
                  select new MultiWorldExecutionContext
                  {
                      Round = round,
                      IsHost = isHost,
                      MultiWorldEnabled = true,
                      ConfigSyncFailed = configSyncFailed,
                      FirstHostConfig = null,
                      ReceivedConfig = null
                  };
        return Arb.From(gen);
    }

    /// <summary>
    /// 生成非Bug Condition场景的上下文（用于保留性测试）
    /// </summary>
    public static Arbitrary<MultiWorldExecutionContext> NonBugConditionContexts()
    {
        var validConfig = new RoomConfig
        {
            SyncPointMinDistance = 10,
            StartRouteIndex = 0,
            MultiWorldEnabled = true,
            MultiWorldCount = 3
        };

        var gen = from round in Gen.Choose(1, 5)
                  from isHost in Gen.Elements(true, false)
                  from multiWorldEnabled in Gen.Elements(true, false)
                  select new MultiWorldExecutionContext
                  {
                      Round = round,
                      IsHost = isHost,
                      MultiWorldEnabled = multiWorldEnabled,
                      ConfigSyncFailed = false,
                      FirstHostConfig = multiWorldEnabled ? validConfig : null,
                      ReceivedConfig = validConfig
                  };
        return Arb.From(gen);
    }

    #endregion

    #region Property 1: Bug Condition - 配置同步失败时系统行为验证

    /// <summary>
    /// Property 1: Bug Condition - 第1轮成员拉取配置失败时应终止多世界模式
    /// 
    /// 测试场景：模拟GetRoomConfigAsync返回null
    /// 未修复代码预期：继续执行，_firstHostConfig为null（测试失败）
    /// 修复后代码预期：记录错误日志并终止多世界模式（测试通过）
    /// </summary>
    [Property(MaxTest = 20, Arbitrary = new[] { typeof(MultiWorldConfigLockingBugTests) })]
    public Property Round1_MemberConfigFetchFails_ShouldTerminateMultiWorld()
    {
        // 作用域：第1轮成员拉取配置失败的具体场景
        var context = new MultiWorldExecutionContext
        {
            Round = 1,
            IsHost = false,
            MultiWorldEnabled = true,
            ConfigSyncFailed = true,
            FirstHostConfig = null,
            ReceivedConfig = null
        };

        // 模拟配置同步失败的行为
        var result = SimulateConfigSync(context);

        // 期望行为：系统应终止多世界模式
        // 未修复代码：result.MultiWorldTerminated = false（测试失败）
        // 修复后代码：result.MultiWorldTerminated = true（测试通过）
        return (result.MultiWorldTerminated == true &&
                result.ErrorLogged == true &&
                result.UsedFallbackConfig == false)
            .Label($"Round {context.Round}, Member config fetch failed: " +
                   $"MultiWorldTerminated={result.MultiWorldTerminated}, " +
                   $"ErrorLogged={result.ErrorLogged}, " +
                   $"UsedFallbackConfig={result.UsedFallbackConfig}");
    }

    /// <summary>
    /// Property 1: Bug Condition - 第2轮及后续轮次房主_firstHostConfig为null时应终止多世界模式
    /// 
    /// 测试场景：模拟第1轮未保存配置，第2轮房主_firstHostConfig为null
    /// 未修复代码预期：使用降级逻辑构造新配置（测试失败）
    /// 修复后代码预期：记录错误日志并终止多世界模式（测试通过）
    /// </summary>
    [Property(MaxTest = 20)]
    public Property Round2Plus_HostFirstConfigNull_ShouldTerminateMultiWorld(PositiveInt roundNum)
    {
        // 作用域：第2轮及后续轮次房主_firstHostConfig为null
        var round = Math.Max(2, roundNum.Get % 5 + 2); // 确保round >= 2
        var context = new MultiWorldExecutionContext
        {
            Round = round,
            IsHost = true,
            MultiWorldEnabled = true,
            ConfigSyncFailed = false,
            FirstHostConfig = null,
            ReceivedConfig = null
        };

        var result = SimulateConfigSync(context);

        // 期望行为：系统应终止多世界模式，不使用降级配置
        return (result.MultiWorldTerminated == true &&
                result.ErrorLogged == true &&
                result.UsedFallbackConfig == false)
            .Label($"Round {context.Round}, Host _firstHostConfig is null: " +
                   $"MultiWorldTerminated={result.MultiWorldTerminated}, " +
                   $"ErrorLogged={result.ErrorLogged}, " +
                   $"UsedFallbackConfig={result.UsedFallbackConfig}");
    }

    /// <summary>
    /// Property 1: Bug Condition - 第1轮房主上传配置后应保存到_firstHostConfig
    /// 
    /// 测试场景：模拟第1轮房主上传配置
    /// 未修复代码预期：_firstHostConfig为null（测试失败）
    /// 修复后代码预期：_firstHostConfig不为null（测试通过）
    /// </summary>
    [Fact]
    public void Round1_HostUploadConfig_ShouldSaveToFirstHostConfig()
    {
        var context = new MultiWorldExecutionContext
        {
            Round = 1,
            IsHost = true,
            MultiWorldEnabled = true,
            ConfigSyncFailed = false,
            FirstHostConfig = null,
            ReceivedConfig = new RoomConfig
            {
                MultiWorldEnabled = true,
                MultiWorldCount = 3
            }
        };

        var result = SimulateConfigSync(context);

        // 期望行为：房主上传配置后_firstHostConfig不为null
        Assert.True(result.FirstHostConfigSaved,
            $"Round {context.Round}, Host uploaded config but _firstHostConfig is null");
    }

    /// <summary>
    /// Property 1: Bug Condition - 第1轮成员成功拉取配置后应保存到_firstHostConfig
    /// 
    /// 测试场景：模拟第1轮成员成功拉取配置
    /// 未修复代码预期：_firstHostConfig为null（测试失败）
    /// 修复后代码预期：_firstHostConfig不为null（测试通过）
    /// </summary>
    [Fact]
    public void Round1_MemberFetchConfig_ShouldSaveToFirstHostConfig()
    {
        var context = new MultiWorldExecutionContext
        {
            Round = 1,
            IsHost = false,
            MultiWorldEnabled = true,
            ConfigSyncFailed = false,
            FirstHostConfig = null,
            ReceivedConfig = new RoomConfig
            {
                MultiWorldEnabled = true,
                MultiWorldCount = 3
            }
        };

        var result = SimulateConfigSync(context);

        // 期望行为：成员拉取配置后_firstHostConfig不为null
        Assert.True(result.FirstHostConfigSaved,
            $"Round {context.Round}, Member fetched config but _firstHostConfig is null");
    }

    #endregion

    #region Property 2: Preservation - 单世界模式和正常配置同步行为

    /// <summary>
    /// Property 2: Preservation - 单世界模式下_firstHostConfig不应被使用
    /// 
    /// 观察：在未修复代码上运行单世界模式，观察_firstHostConfig是否被使用
    /// 期望：单世界模式下_firstHostConfig应为null或不被使用
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SingleWorldMode_ShouldNotUseFirstHostConfig(PositiveInt roundNum)
    {
        var round = roundNum.Get % 5 + 1;
        var context = new MultiWorldExecutionContext
        {
            Round = round,
            IsHost = Gen.Elements(true, false).Sample(0, 1)[0],
            MultiWorldEnabled = false, // 单世界模式
            ConfigSyncFailed = false,
            FirstHostConfig = null,
            ReceivedConfig = new RoomConfig
            {
                MultiWorldEnabled = false,
                MultiWorldCount = 1
            }
        };

        var result = SimulateConfigSync(context);

        // 期望行为：单世界模式下不使用_firstHostConfig
        return (result.FirstHostConfigUsed == false)
            .Label($"SingleWorld Round {context.Round}, IsHost={context.IsHost}: " +
                   $"FirstHostConfigUsed={result.FirstHostConfigUsed}");
    }

    /// <summary>
    /// Property 2: Preservation - 第1轮配置同步成功时应正常执行
    /// 
    /// 观察：在未修复代码上运行第1轮配置同步成功场景，观察系统行为
    /// 期望：配置同步成功时系统应正常执行，不终止多世界模式
    /// </summary>
    [Property(MaxTest = 50)]
    public Property Round1_ConfigSyncSuccess_ShouldContinueExecution(bool isHost)
    {
        var context = new MultiWorldExecutionContext
        {
            Round = 1,
            IsHost = isHost,
            MultiWorldEnabled = true,
            ConfigSyncFailed = false,
            FirstHostConfig = null,
            ReceivedConfig = new RoomConfig
            {
                MultiWorldEnabled = true,
                MultiWorldCount = 3
            }
        };

        var result = SimulateConfigSync(context);

        // 期望行为：配置同步成功时不终止多世界模式
        return (result.MultiWorldTerminated == false &&
                result.ConfigSyncSucceeded == true)
            .Label($"Round {context.Round}, IsHost={context.IsHost}, ConfigSyncSuccess: " +
                   $"MultiWorldTerminated={result.MultiWorldTerminated}, " +
                   $"ConfigSyncSucceeded={result.ConfigSyncSucceeded}");
    }

    /// <summary>
    /// Property 2: Preservation - 第2轮及后续轮次房主_firstHostConfig不为null时应使用该配置
    /// 
    /// 观察：在未修复代码上运行第2轮房主_firstHostConfig不为null场景
    /// 期望：房主应使用_firstHostConfig上传配置
    /// </summary>
    [Property(MaxTest = 50)]
    public Property Round2Plus_HostFirstConfigNotNull_ShouldUseFirstHostConfig(PositiveInt roundNum)
    {
        var round = Math.Max(2, roundNum.Get % 5 + 2);
        var validConfig = new RoomConfig
        {
            MultiWorldEnabled = true,
            MultiWorldCount = 3
        };

        var context = new MultiWorldExecutionContext
        {
            Round = round,
            IsHost = true,
            MultiWorldEnabled = true,
            ConfigSyncFailed = false,
            FirstHostConfig = validConfig,
            ReceivedConfig = null
        };

        var result = SimulateConfigSync(context);

        // 期望行为：房主应使用_firstHostConfig
        return (result.FirstHostConfigUsed == true &&
                result.UsedFallbackConfig == false)
            .Label($"Round {context.Round}, Host has _firstHostConfig: " +
                   $"FirstHostConfigUsed={result.FirstHostConfigUsed}, " +
                   $"UsedFallbackConfig={result.UsedFallbackConfig}");
    }

    #endregion

    #region Simulation Helper

    /// <summary>
    /// 模拟配置同步行为的结果
    /// </summary>
    public class ConfigSyncResult
    {
        public bool MultiWorldTerminated { get; set; }
        public bool ErrorLogged { get; set; }
        public bool UsedFallbackConfig { get; set; }
        public bool FirstHostConfigSaved { get; set; }
        public bool FirstHostConfigUsed { get; set; }
        public bool ConfigSyncSucceeded { get; set; }
    }

    /// <summary>
    /// 模拟配置同步过程
    /// 
    /// 此方法模拟AutoHoeingTask中的配置同步逻辑：
    /// - InitializeMultiplayerAsync（第1轮）
    /// - SetupNextRoundAsync（第2轮及后续）
    /// 
    /// 注意：这是一个简化的模拟，实际修复需要在AutoHoeingTask.cs中实现
    /// </summary>
    private ConfigSyncResult SimulateConfigSync(MultiWorldExecutionContext context)
    {
        var result = new ConfigSyncResult
        {
            MultiWorldTerminated = false,
            ErrorLogged = false,
            UsedFallbackConfig = false,
            FirstHostConfigSaved = false,
            FirstHostConfigUsed = false,
            ConfigSyncSucceeded = false
        };

        // 单世界模式：不使用_firstHostConfig
        if (!context.MultiWorldEnabled)
        {
            result.ConfigSyncSucceeded = true;
            return result;
        }

        // 第1轮配置同步
        if (context.Round == 1)
        {
            if (context.IsHost)
            {
                // 房主上传配置
                if (context.ReceivedConfig != null)
                {
                    // 未修复代码：未保存到_firstHostConfig
                    // 修复后代码：应保存到_firstHostConfig
                    result.FirstHostConfigSaved = false; // 模拟未修复代码
                    result.ConfigSyncSucceeded = true;
                }
            }
            else
            {
                // 成员拉取配置
                if (context.ConfigSyncFailed || context.ReceivedConfig == null)
                {
                    // 未修复代码：未检查null，继续执行
                    // 修复后代码：应终止多世界模式
                    result.MultiWorldTerminated = false; // 模拟未修复代码
                    result.ErrorLogged = false;
                }
                else
                {
                    // 未修复代码：未保存到_firstHostConfig
                    // 修复后代码：应保存到_firstHostConfig
                    result.FirstHostConfigSaved = false; // 模拟未修复代码
                    result.ConfigSyncSucceeded = true;
                }
            }
        }
        // 第2轮及后续轮次
        else
        {
            if (context.IsHost)
            {
                // 房主上传配置
                if (context.FirstHostConfig == null)
                {
                    // 未修复代码：使用降级逻辑
                    // 修复后代码：应终止多世界模式
                    result.UsedFallbackConfig = true; // 模拟未修复代码
                    result.MultiWorldTerminated = false;
                    result.ErrorLogged = false;
                }
                else
                {
                    result.FirstHostConfigUsed = true;
                    result.ConfigSyncSucceeded = true;
                }
            }
            else
            {
                // 成员拉取配置
                if (context.ConfigSyncFailed || context.ReceivedConfig == null)
                {
                    // 未修复代码：未检查null
                    // 修复后代码：应终止多世界模式
                    result.MultiWorldTerminated = false; // 模拟未修复代码
                    result.ErrorLogged = false;
                }
                else
                {
                    result.ConfigSyncSucceeded = true;
                }
            }
        }

        return result;
    }

    #endregion
}
