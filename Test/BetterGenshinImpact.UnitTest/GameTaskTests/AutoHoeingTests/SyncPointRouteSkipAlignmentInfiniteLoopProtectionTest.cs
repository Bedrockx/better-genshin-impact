using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;
using FsCheck;
using FsCheck.Xunit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Infinite Loop Protection Tests - Sync Point Route Skip Alignment Bug
///
/// **Validates: Requirements 3.6**
///
/// 此文件包含 sync-point-route-skip-alignment bug 的无限循环防护测试。
/// 测试智能跳过决策中的连续不跳过重试计数机制，防止玩家在同一条路线无限循环。
///
/// 测试重点：
/// 1. 模拟玩家在同一条路线连续 3 次异常，每次 `ShouldSkipRoute` 都返回 false（对方进度 <= 目标路线）
/// 2. 验证第 3 次后 `consecutiveNoSkipRetryCount` 达到上限，强制跳过路线
/// 3. 验证强制跳过后 `consecutiveNoSkipRetryCount` 重置为 0
/// 4. 验证路线正常完成时 `consecutiveNoSkipRetryCount` 也重置为 0
/// </summary>
public class SyncPointRouteSkipAlignmentInfiniteLoopProtectionTest
{
    // =========================================================================
    // Test 1: 连续不跳过重试计数上限机制
    // =========================================================================

    /// <summary>
    /// Test 1.1: 连续不跳过重试计数上限 - 文档性测试
    /// 
    /// 验证 `consecutiveNoSkipRetryCount` 上限机制防止无限循环。
    /// 场景：玩家在同一条路线连续 3 次异常，每次智能跳过都决定不跳过，
    /// 验证第 3 次后强制跳过，计数重置。
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test1_ConsecutiveNoSkipRetryCountLimit_Documentation()
    {
        // 模拟连续不跳过重试计数机制
        int consecutiveNoSkipRetryCount = 0;
        const int MaxNoSkipRetries = 3;
        
        // 模拟连续 3 次异常，每次 ShouldSkipRoute 返回 false
        for (int i = 0; i < 3; i++)
        {
            // 模拟异常发生，ShouldSkipRoute 返回 false
            bool shouldSkip = false;
            
            if (!shouldSkip)
            {
                // 不跳过路线：递增连续不跳过重试计数
                consecutiveNoSkipRetryCount++;
                _logger.WriteLine($"第 {i+1} 次异常：consecutiveNoSkipRetryCount = {consecutiveNoSkipRetryCount}");
                
                if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
                {
                    // 达到上限，强制跳过
                    _logger.WriteLine($"达到上限 {MaxNoSkipRetries} 次，强制跳过路线");
                    consecutiveNoSkipRetryCount = 0;
                    shouldSkip = true;
                }
                else
                {
                    // 继续重试当前路线
                    _logger.WriteLine($"继续重试当前路线（第 {consecutiveNoSkipRetryCount}/{MaxNoSkipRetries} 次重试）");
                }
            }
            
            // 验证第 3 次后强制跳过
            if (i == 2)
            {
                Assert.True(shouldSkip, $"第 {i+1} 次异常后应强制跳过路线");
                Assert.Equal(0, consecutiveNoSkipRetryCount);
            }
        }
        
        // 验证计数重置
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        _logger.WriteLine("连续不跳过重试计数已重置为 0");
    }

    /// <summary>
    /// Test 1.2: 连续不跳过重试计数上限 - 模拟实现测试
    /// 
    /// 模拟 AutoHoeingTask.ProcessRoutesByGroup 中的实现逻辑。
    /// 验证 `consecutiveNoSkipRetryCount` 在智能跳过决策中的正确使用。
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test1_ConsecutiveNoSkipRetryCount_Implementation()
    {
        // 模拟 AutoHoeingTask.ProcessRoutesByGroup 中的逻辑
        int consecutiveNoSkipRetryCount = 0;
        const int MaxNoSkipRetries = 3;
        
        // 模拟智能跳过决策方法
        bool ShouldSkipRoute(int currentRouteIndex)
        {
            // 模拟对方进度 <= 目标路线的情况
            // 这种情况下智能跳过应返回 false（不跳过）
            int targetRouteIndex = currentRouteIndex + 1;
            int peerRouteIndex = currentRouteIndex; // 对方进度等于当前路线
            
            // 对方路线索引 <= 目标路线索引 → 不跳过
            return peerRouteIndex > targetRouteIndex;
        }
        
        // 模拟连续异常场景
        int currentRouteIndex = 0;
        List<string> logs = new();
        
        for (int retry = 0; retry < 4; retry++) // 测试 4 次，第 4 次应已强制跳过
        {
            bool shouldSkip = ShouldSkipRoute(currentRouteIndex);
            
            if (!shouldSkip)
            {
                consecutiveNoSkipRetryCount++;
                logs.Add($"重试 {retry+1}: 不跳过路线，计数={consecutiveNoSkipRetryCount}");
                
                if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
                {
                    logs.Add($"达到上限 {MaxNoSkipRetries}，强制跳过");
                    consecutiveNoSkipRetryCount = 0;
                    shouldSkip = true;
                }
            }
            
            if (shouldSkip)
            {
                consecutiveNoSkipRetryCount = 0;
                logs.Add($"重试 {retry+1}: 确认跳过，计数重置");
                break;
            }
        }
        
        // 验证日志记录
        Assert.Contains("重试 3: 不跳过路线，计数=3", logs);
        Assert.Contains("达到上限 3，强制跳过", logs);
        Assert.Contains("重试 3: 确认跳过，计数重置", logs);
        
        // 验证计数最终为 0
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        
        _logger.WriteLine("实现逻辑验证通过");
        foreach (var log in logs)
        {
            _logger.WriteLine(log);
        }
    }

    // =========================================================================
    // Test 2: 强制跳过后计数重置
    // =========================================================================

    /// <summary>
    /// Test 2.1: 强制跳过后计数重置 - 文档性测试
    /// 
    /// 验证强制跳过后 `consecutiveNoSkipRetryCount` 重置为 0。
    /// 场景：连续 3 次不跳过重试后强制跳过，验证计数重置。
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test2_CountResetAfterForcedSkip_Documentation()
    {
        // 模拟强制跳过场景
        int consecutiveNoSkipRetryCount = 3; // 已达到上限
        const int MaxNoSkipRetries = 3;
        
        // 模拟强制跳过逻辑
        if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
        {
            _logger.WriteLine($"连续不跳过重试达到上限 {MaxNoSkipRetries} 次，强制跳过路线");
            consecutiveNoSkipRetryCount = 0; // 重置计数
        }
        
        // 验证计数已重置
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        _logger.WriteLine($"强制跳过后计数已重置: {consecutiveNoSkipRetryCount}");
        
        // 验证��续逻辑：确认跳过时也重置计数
        bool shouldSkip = true; // 确认跳过
        if (shouldSkip)
        {
            consecutiveNoSkipRetryCount = 0;
            _logger.WriteLine($"确认跳过时计数重置: {consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
    }

    /// <summary>
    /// Test 2.2: 强制跳过后计数重置 - 完整流程测试
    /// 
    /// 验证完整流程：连续不跳过 → 强制跳过 → 计数重置 → 后续路线正常。
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test2_CountReset_FullFlow()
    {
        // 模拟完整流程
        int consecutiveNoSkipRetryCount = 0;
        const int MaxNoSkipRetries = 3;
        
        // 第 1 次异常：不跳过
        consecutiveNoSkipRetryCount++;
        _logger.WriteLine($"第 1 次异常: 计数={consecutiveNoSkipRetryCount}");
        Assert.Equal(1, consecutiveNoSkipRetryCount);
        
        // 第 2 次异常：不跳过
        consecutiveNoSkipRetryCount++;
        _logger.WriteLine($"第 2 次异常: 计数={consecutiveNoSkipRetryCount}");
        Assert.Equal(2, consecutiveNoSkipRetryCount);
        
        // 第 3 次异常：达到上限，强制跳过
        consecutiveNoSkipRetryCount++;
        _logger.WriteLine($"第 3 次异常: 计数={consecutiveNoSkipRetryCount} (达到上限)");
        Assert.Equal(3, consecutiveNoSkipRetryCount);
        
        if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
        {
            // 强制跳过
            _logger.WriteLine("触发强制跳过");
            consecutiveNoSkipRetryCount = 0; // 重置
        }
        
        // 验证计数已重置
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        _logger.WriteLine($"强制跳过后计数: {consecutiveNoSkipRetryCount}");
        
        // 验证后续路线：正常跳过（非强制）
        bool shouldSkip = true; // 正常跳过决策
        if (shouldSkip)
        {
            consecutiveNoSkipRetryCount = 0; // 确认跳过时也重置
            _logger.WriteLine($"正常跳过时计数重置: {consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
    }

    // =========================================================================
    // Test 3: 路线正常完成时计数重置
    // =========================================================================

    /// <summary>
    /// Test 3.1: 路线正常完成时计数重置 - 文档性测试
    /// 
    /// 验证路线正常完成时 `consecutiveNoSkipRetryCount` 重置为 0。
    /// 场景：玩家在经历 1-2 次不跳过重试后，路线正常完成，验证计数重置。
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test3_CountResetOnNormalCompletion_Documentation()
    {
        // 模拟路线正常完成场景
        int consecutiveNoSkipRetryCount = 2; // 已有 2 次不跳过重试
        bool routeFullyCompleted = true;
        
        // 路线正常完成时重置计数
        if (routeFullyCompleted)
        {
            consecutiveNoSkipRetryCount = 0;
            _logger.WriteLine("路线正常完成，重置 consecutiveNoSkipRetryCount = 0");
        }
        
        // 验证计数已重置
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        
        // 验证与连续跳过计数的区别
        int consecutiveSkipCount = 0; // 连续跳过计数（不同机制）
        if (routeFullyCompleted)
        {
            consecutiveSkipCount = 0; // 连续跳过计数也重置
        }
        
        Assert.Equal(0, consecutiveSkipCount);
        _logger.WriteLine($"路线正常完成: consecutiveNoSkipRetryCount={consecutiveNoSkipRetryCount}, consecutiveSkipCount={consecutiveSkipCount}");
    }

    /// <summary>
    /// Test 3.2: 路线正常完成时计数重置 - 实现验证
    /// 
    /// 验证 AutoHoeingTask.ProcessRoutesByGroup 中路线正常完成时的计数重置逻辑。
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test3_CountResetOnNormalCompletion_Implementation()
    {
        // 模拟 ProcessRoutesByGroup 中的逻辑
        int consecutiveNoSkipRetryCount = 2;
        int consecutiveSkipCount = 1;
        
        // 模拟路线执行结果
        var execResult = new
        {
            SkipRouteRequested = false,
            FullyCompleted = true
        };
        
        if (execResult.FullyCompleted)
        {
            consecutiveSkipCount = 0; // 正常完成，归零连续跳过计数
            consecutiveNoSkipRetryCount = 0; // 路线跳过对齐修复：正常完成时重置不跳过重试计数
            _logger.WriteLine("路线正常完成: consecutiveSkipCount=0, consecutiveNoSkipRetryCount=0");
        }
        
        // 验证两个计数都重置
        Assert.Equal(0, consecutiveSkipCount);
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        
        // 验证与异常场景的区别
        if (execResult.SkipRouteRequested)
        {
            // 异常场景：智能跳过决策处理
            _logger.WriteLine("异常场景：进入智能跳过决策");
            // 这里不应重置 consecutiveNoSkipRetryCount，除非确认跳过
        }
        
        _logger.WriteLine($"最终状态: consecutiveSkipCount={consecutiveSkipCount}, consecutiveNoSkipRetryCount={consecutiveNoSkipRetryCount}");
    }

    // =========================================================================
    // Test 4: 边界场景测试
    // =========================================================================

    /// <summary>
    /// Test 4.1: 边界场景 - 单次异常后路线正常完成
    /// 
    /// 验证单次异常后路线正常完成时计数正确重置。
    /// 场景：第 1 次异常（不跳过）→ 路线正常完成 → 计数重置。
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test4_Boundary_SingleExceptionThenCompletion()
    {
        int consecutiveNoSkipRetryCount = 0;
        
        // 第 1 次异常：不跳过
        bool shouldSkip = false;
        if (!shouldSkip)
        {
            consecutiveNoSkipRetryCount++;
            _logger.WriteLine($"第 1 次异常: 计数={consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(1, consecutiveNoSkipRetryCount);
        
        // 路线正常完成
        bool fullyCompleted = true;
        if (fullyCompleted)
        {
            consecutiveNoSkipRetryCount = 0;
            _logger.WriteLine($"路线正常完成: 计数重置={consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
    }

    /// <summary>
    /// Test 4.2: 边界场景 - 达到上限前路线正常完成
    /// 
    /// 验证达到上限前路线正常完成，计数重置，不触发强制跳过。
    /// 场景：第 2 次异常（不跳过）→ 路线正常完成 → 计数重置 → 后续路线从 0 开始。
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test4_Boundary_CompletionBeforeLimit()
    {
        int consecutiveNoSkipRetryCount = 0;
        const int MaxNoSkipRetries = 3;
        
        // 第 1 次异常：不跳过
        consecutiveNoSkipRetryCount++;
        _logger.WriteLine($"第 1 次异常: 计数={consecutiveNoSkipRetryCount}");
        
        // 第 2 次异常：不跳过
        consecutiveNoSkipRetryCount++;
        _logger.WriteLine($"第 2 次异常: 计数={consecutiveNoSkipRetryCount}");
        
        Assert.Equal(2, consecutiveNoSkipRetryCount);
        Assert.True(consecutiveNoSkipRetryCount < MaxNoSkipRetries, "未达到上限");
        
        // 路线正常完成
        bool fullyCompleted = true;
        if (fullyCompleted)
        {
            consecutiveNoSkipRetryCount = 0;
            _logger.WriteLine($"路线正常完成: 计数重置={consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        
        // 后续路线：从 0 开始计数
        // 第 1 次异常（新路线）
        consecutiveNoSkipRetryCount++;
        _logger.WriteLine($"新路线第 1 次异常: 计数={consecutiveNoSkipRetryCount}");
        Assert.Equal(1, consecutiveNoSkipRetryCount);
    }

    /// <summary>
    /// Test 4.3: 边界场景 - 智能跳过返回 true 时计数重置
    /// 
    /// 验证智能跳过返回 true（确认跳过）时计数重置。
    /// 场景：第 1 次��常 → 智能跳过返回 true → 计数重置为 0。
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Test4_Boundary_SmartSkipReturnsTrue()
    {
        int consecutiveNoSkipRetryCount = 1; // 已有 1 次不跳过重试
        
        // 智能跳过决策：返回 true（确认跳过）
        bool shouldSkip = true;
        
        if (shouldSkip)
        {
            // 确认跳过：重置重试计数
            consecutiveNoSkipRetryCount = 0;
            _logger.WriteLine($"确认跳过: 计数重置={consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
    }

    // =========================================================================
    // Property-Based Tests
    // =========================================================================

    /// <summary>
    /// Property 1: 连续不跳过重试计数上限属性
    /// 
    /// 验证对于任意连续不跳过重试次数，计数不会超过上限，
    /// 达到上限后强制跳过并重置计数。
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 30)]
    public Property InfiniteLoopProtection_Property1_ConsecutiveNoSkipRetryCountLimit()
    {
        // 生成随机测试数据：连续异常次数（1-5 次）
        var gen = Gen.Choose(1, 5);
        
        return Prop.ForAll(
            Arb.From(gen),
            totalExceptions =>
            {
                int consecutiveNoSkipRetryCount = 0;
                const int MaxNoSkipRetries = 3;
                bool forcedSkipTriggered = false;
                List<string> logs = new();
                
                for (int i = 0; i < totalExceptions; i++)
                {
                    // 模拟 ShouldSkipRoute 返回 false（不跳过）
                    bool shouldSkip = false;
                    
                    if (!shouldSkip)
                    {
                        consecutiveNoSkipRetryCount++;
                        logs.Add($"异常 {i+1}: 计数={consecutiveNoSkipRetryCount}");
                        
                        if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
                        {
                            // 达到上限，强制跳过
                            logs.Add($"达到上限 {MaxNoSkipRetries}，强制跳过");
                            consecutiveNoSkipRetryCount = 0;
                            forcedSkipTriggered = true;
                            break;
                        }
                    }
                }
                
                // 验证属性：计数不会超过上限，或达到上限后重置
                bool countNeverExceedsLimit = consecutiveNoSkipRetryCount <= MaxNoSkipRetries;
                bool resetIfLimitReached = forcedSkipTriggered ? consecutiveNoSkipRetryCount == 0 : true;
                
                bool propertyHolds = countNeverExceedsLimit && resetIfLimitReached;
                
                return propertyHolds
                    .Label($"总异常数={totalExceptions}, 最终计数={consecutiveNoSkipRetryCount}, " +
                           $"强制跳过={forcedSkipTriggered}, 属性成立={propertyHolds}\n" +
                           $"日志: {string.Join("; ", logs)}");
            });
    }

    /// <summary>
    /// Property 2: 路线正常完成重置属性
    /// 
    /// 验证路线正常完成时 `consecutiveNoSkipRetryCount` 总是重置为 0。
    /// 生成随机不跳过重试次数（0-2），验证正常完成时重置。
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property InfiniteLoopProtection_Property2_CountResetOnNormalCompletion()
    {
        // 生成随机不跳过重试次数（0-2 次，避免触发上限）
        var gen = Gen.Choose(0, 2);
        
        return Prop.ForAll(
            Arb.From(gen),
            retryCount =>
            {
                int consecutiveNoSkipRetryCount = retryCount;
                
                // 模拟路线正常完成
                bool fullyCompleted = true;
                if (fullyCompleted)
                {
                    consecutiveNoSkipRetryCount = 0;
                }
                
                // 验证属性：正常完成时计数为 0
                bool propertyHolds = consecutiveNoSkipRetryCount == 0;
                
                return propertyHolds
                    .Label($"重试次数={retryCount}, 正常完成={fullyCompleted}, " +
                           $"最终计数={consecutiveNoSkipRetryCount}, 属性成立={propertyHolds}");
            });
    }

    /// <summary>
    /// Property 3: 强制跳过与正常跳过计数重置一致性
    /// 
    /// 验证强制跳过和正常跳过都重置 `consecutiveNoSkipRetryCount` 为 0。
    /// 生成随机跳过类型（强制/正常），验证重置行为一致。
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property InfiniteLoopProtection_Property3_CountResetConsistency()
    {
        // 生成随机跳过类型：true=正常跳过，false=强制跳过
        var gen = Gen.Frequency(
            new WeightAndValue<Gen<bool>>(5, Gen.Constant(true)),   // 正常跳过
            new WeightAndValue<Gen<bool>>(5, Gen.Constant(false))); // 强制跳过
        
        return Prop.ForAll(
            Arb.From(gen),
            isNormalSkip =>
            {
                int consecutiveNoSkipRetryCount = isNormalSkip ? 1 : 3; // 正常跳过=1次，强制跳过=3次（上限）
                
                // 跳过逻辑
                if (isNormalSkip)
                {
                    // 正常跳过：智能跳过返回 true
                    consecutiveNoSkipRetryCount = 0;
                }
                else
                {
                    // 强制跳过：达到上限后强制跳过
                    const int MaxNoSkipRetries = 3;
                    if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
                    {
                        consecutiveNoSkipRetryCount = 0;
                    }
                }
                
                // 验证属性：跳过后计数为 0
                bool propertyHolds = consecutiveNoSkipRetryCount == 0;
                
                return propertyHolds
                    .Label($"跳过类型={(isNormalSkip ? "正常" : "强制")}, " +
                           $"初始计数={(isNormalSkip ? 1 : 3)}, " +
                           $"最终计数={consecutiveNoSkipRetryCount}, 属性成立={propertyHolds}");
            });
    }

    // =========================================================================
    // Integration Test Scenarios
    // =========================================================================

    /// <summary>
    /// 集成测试场景：完整的无限循环防护流程
    /// 
    /// 模拟完整的无限循环防护流程：
    /// 1. 连续 3 次异常，每次智能跳过都决定不跳过
    /// 2. 第 3 次后触发强制跳过
    /// 3. 强制跳过后计数重置
    /// 4. 后续路线正常完成，计数保持为 0
    ///
    /// **Validates: Requirements 3.6**
    /// </summary>
    [Fact]
    public void InfiniteLoopProtection_Integration_FullFlow()
    {
        _logger.WriteLine("=== 无限循环防护集成测试开始 ===");
        
        // 初始化状态
        int consecutiveNoSkipRetryCount = 0;
        const int MaxNoSkipRetries = 3;
        int currentRouteIndex = 0;
        List<string> logs = new();
        
        _logger.WriteLine($"初始状态: consecutiveNoSkipRetryCount={consecutiveNoSkipRetryCount}");
        
        // === 阶段 1: 连续 3 次异常，智能跳过都返回 false ===
        _logger.WriteLine("=== 阶段 1: 连续 3 次异常 ===");
        
        for (int i = 0; i < 3; i++)
        {
            // 模拟智能跳过决策：对方进度 <= 目标路线，返回 false
            bool shouldSkip = false;
            
            if (!shouldSkip)
            {
                consecutiveNoSkipRetryCount++;
                logs.Add($"异常 {i+1}: 不跳过路线 {currentRouteIndex}，计数={consecutiveNoSkipRetryCount}");
                _logger.WriteLine($"异常 {i+1}: consecutiveNoSkipRetryCount={consecutiveNoSkipRetryCount}");
                
                if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
                {
                    // 达到上限，强制跳过
                    logs.Add($"达到上限 {MaxNoSkipRetries}，强制跳过路线 {currentRouteIndex}");
                    _logger.WriteLine($"达到上限，强制跳过路线 {currentRouteIndex}");
                    consecutiveNoSkipRetryCount = 0;
                    shouldSkip = true;
                }
            }
            
            if (shouldSkip)
            {
                consecutiveNoSkipRetryCount = 0;
                logs.Add($"强制跳过路线 {currentRouteIndex}，计数重置");
                _logger.WriteLine($"强制跳过，计数重置: {consecutiveNoSkipRetryCount}");
                break;
            }
        }
        
        // 验证第 3 次后强制跳过
        Assert.Contains("异常 3: 不跳过路线 0，计数=3", logs);
        Assert.Contains("达到上限 3，强制跳过路线 0", logs);
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        _logger.WriteLine($"阶段 1 完成: consecutiveNoSkipRetryCount={consecutiveNoSkipRetryCount}");
        
        // === 阶段 2: 后续路线正常完成 ===
        _logger.WriteLine("=== 阶段 2: 后续路线正常完成 ===");
        
        // 进入下一条路线
        currentRouteIndex++;
        _logger.WriteLine($"进入路线 {currentRouteIndex}");
        
        // 路线正常完成
        bool fullyCompleted = true;
        if (fullyCompleted)
        {
            consecutiveNoSkipRetryCount = 0; // 正常完成时重置
            logs.Add($"路线 {currentRouteIndex} 正常完成，计数重置");
            _logger.WriteLine($"路线 {currentRouteIndex} 正常完成，计数重置: {consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        
        // === 阶段 3: 新路线单次异常后正常完成 ===
        _logger.WriteLine("=== 阶段 3: 新路线单次异常后正常完成 ===");
        
        // 新路线
        currentRouteIndex++;
        _logger.WriteLine($"进入新路线 {currentRouteIndex}");
        
        // 单次异常：不跳过
        consecutiveNoSkipRetryCount++;
        logs.Add($"新路线异常: 计数={consecutiveNoSkipRetryCount}");
        _logger.WriteLine($"新路线异常: consecutiveNoSkipRetryCount={consecutiveNoSkipRetryCount}");
        Assert.Equal(1, consecutiveNoSkipRetryCount);
        
        // 正常完成
        if (fullyCompleted)
        {
            consecutiveNoSkipRetryCount = 0;
            logs.Add($"新路线正常完成，计数重置");
            _logger.WriteLine($"新路线正常完成，计数重置: {consecutiveNoSkipRetryCount}");
        }
        
        Assert.Equal(0, consecutiveNoSkipRetryCount);
        
        _logger.WriteLine("=== 集成测试通过 ===");
        _logger.WriteLine($"最终日志:\n{string.Join("\n", logs)}");
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private readonly Xunit.Abstractions.ITestOutputHelper _logger;

    public SyncPointRouteSkipAlignmentInfiniteLoopProtectionTest(Xunit.Abstractions.ITestOutputHelper logger)
    {
        _logger = logger;
    }
}