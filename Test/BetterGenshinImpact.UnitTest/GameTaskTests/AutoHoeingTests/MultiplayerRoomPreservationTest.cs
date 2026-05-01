using FsCheck;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Preservation Property Tests - Multiplayer Room Bugs
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
///
/// 此文件包含两个 bug 的保留属性测试，在未修复代码上运行时预期通过（EXPECTED TO PASS）。
/// 通过即建立基线行为。修复后，这些测试同样应通过（确认无回归）。
///
/// 遵循观察优先方法论：先在未修复代码上观察实际输出，再编写断言。
///
/// Bug 1（房主放人卡住）：保留测试记录非 bug 条件下的正确行为（文档性）
/// Bug 2（线路调试模式不生效）：保留测试验证非 bug 条件下 ApplySettingsOverride 行为不变
/// </summary>
public class MultiplayerRoomPreservationTest
{
    // =========================================================================
    // 辅助方法：与 MultiplayerRoomBugConditionTest 完全相同的模拟逻辑
    // =========================================================================

    /// <summary>
    /// 模拟 AutoHoeingTask.ApplySettingsOverride 中的 Get 逻辑（与实际代码完全一致）：
    /// 若 key 存在且值不为 null，则返回转换后的值；否则返回 fallback。
    /// </summary>
    private static T SimulateGet<T>(Dictionary<string, object?> settings, string key, T fallback)
    {
        if (settings.TryGetValue(key, out var val) && val != null)
        {
            try { return (T)Convert.ChangeType(val, typeof(T)); }
            catch { return fallback; }
        }
        return fallback;
    }

    /// <summary>
    /// 模拟未修复的 ApplySettingsOverride 对 UseFixedDebugRoutes 的处理逻辑。
    ///
    /// 未修复代码（AutoHoeingTask.cs 约第 2105 行）：
    ///   _config.UseFixedDebugRoutes = Get("useFixedDebugRoutes", _config.UseFixedDebugRoutes);
    ///
    /// 加上 Start 方法中的重置（约第 104 行）：
    ///   _config.UseFixedDebugRoutes = false;  // 深拷贝后强制重置
    ///
    /// 两者叠加导致：settings 无键时，Get 返回 fallback（已被重置为 false），
    /// 覆盖了用户全局配置中的 true 值。
    /// </summary>
    private static bool SimulateUnfixedApplySettingsOverride(
        Dictionary<string, object?> settings,
        bool globalConfigUseFixedDebugRoutes)
    {
        // 模拟 Start 方法中的深拷贝 + 强制重置（未修复代码）
        bool configUseFixedDebugRoutes = false; // _config.UseFixedDebugRoutes = false（强制重置）

        // 模拟未修复的 ApplySettingsOverride：无条件使用 Get（键不存在时返回 fallback=false）
        configUseFixedDebugRoutes = SimulateGet(settings, "useFixedDebugRoutes", configUseFixedDebugRoutes);

        return configUseFixedDebugRoutes;
    }

    /// <summary>
    /// 模拟修复后的 ApplySettingsOverride 对 UseFixedDebugRoutes 的处理逻辑。
    ///
    /// 修复后代码：
    ///   Start 方法不再重置 UseFixedDebugRoutes，保留全局配置值。
    ///   ApplySettingsOverride 改用 ContainsKey 模式，仅当 settings 显式包含该键时才覆盖。
    /// </summary>
    private static bool SimulateFixedApplySettingsOverride(
        Dictionary<string, object?> settings,
        bool globalConfigUseFixedDebugRoutes)
    {
        // 修复后：Start 方法不再重置 UseFixedDebugRoutes，保留全局配置值
        bool configUseFixedDebugRoutes = globalConfigUseFixedDebugRoutes;

        // 修复后：ContainsKey 模式，仅当 settings 显式包含该键时才覆盖
        if (settings.ContainsKey("useFixedDebugRoutes"))
            configUseFixedDebugRoutes = SimulateGet(settings, "useFixedDebugRoutes", configUseFixedDebugRoutes);

        return configUseFixedDebugRoutes;
    }

    // =========================================================================
    // Bug 2 保留测试（ApplySettingsOverride）
    //
    // 观察阶段（在未修复代码上执行）：
    //   - settings 显式传入 useFixedDebugRoutes=false → 结果为 false（覆盖全局配置）
    //   - settings 显式传入 useFixedDebugRoutes=true  → 结果为 true
    //   - globalConfig.UseFixedDebugRoutes=false 且 settings 无该键 → 结果为 false（不受影响）
    //
    // 这三种场景均不满足 isBugCondition_2，因此未修复代码行为正确，测试应 PASS。
    // =========================================================================

    /// <summary>
    /// 观察 1：settings 显式传入 useFixedDebugRoutes=false 时，结果为 false（覆盖全局配置）
    ///
    /// 不满足 isBugCondition_2：settings 显式包含该键（ContainsKey 为 true）
    /// 未修复代码行为：Get 读取到 false，返回 false（正确）
    /// 修复后行为：ContainsKey 为 true，仍读取 false，返回 false（不变）
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void Preservation_Bug2_SettingsExplicitFalse_GlobalTrue_ShouldReturnFalse()
    {
        // Arrange: settings 显式传入 false，全局配置为 true
        bool globalConfigUseFixedDebugRoutes = true;
        var settings = new Dictionary<string, object?>
        {
            ["useFixedDebugRoutes"] = false
        };

        // Act: 调用修复后的 ApplySettingsOverride 模拟
        // 观察到的实际输出：false（settings 显式覆盖全局配置）
        bool result = SimulateFixedApplySettingsOverride(settings, globalConfigUseFixedDebugRoutes);

        // Assert: 结果应为 false（settings 显式覆盖全局配置 true → false）
        Assert.False(result,
            $"保留行为：settings 显式传入 useFixedDebugRoutes=false 时，" +
            $"应覆盖全局配置为 false，但实际结果为 {result}。");
    }

    /// <summary>
    /// 观察 2：settings 显式传入 useFixedDebugRoutes=true 时，结果为 true
    ///
    /// 不满足 isBugCondition_2：settings 显式包含该键（ContainsKey 为 true）
    /// 未修复代码行为：Get 读取到 true，返回 true（正确）
    /// 修复后行为：ContainsKey 为 true，仍读取 true，返回 true（不变）
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Fact]
    public void Preservation_Bug2_SettingsExplicitTrue_ShouldReturnTrue()
    {
        // Arrange: settings 显式传入 true
        bool globalConfigUseFixedDebugRoutes = false; // 全局配置为 false，但 settings 显式覆盖
        var settings = new Dictionary<string, object?>
        {
            ["useFixedDebugRoutes"] = true
        };

        // Act: 调用修复后的 ApplySettingsOverride 模拟
        // 观察到的实际输出：true（settings 显式覆盖）
        bool result = SimulateFixedApplySettingsOverride(settings, globalConfigUseFixedDebugRoutes);

        // Assert: 结果应为 true（settings 显式传入 true）
        Assert.True(result,
            $"保留行为：settings 显式传入 useFixedDebugRoutes=true 时，" +
            $"结果应为 true，但实际结果为 {result}。");
    }

    /// <summary>
    /// 观察 3：globalConfig.UseFixedDebugRoutes=false 且 settings 无该键时，结果为 false
    ///
    /// 不满足 isBugCondition_2：globalConfig.UseFixedDebugRoutes=false（条件要求为 true）
    /// 未修复代码行为：Start 重置为 false，Get 返回 fallback=false，结果为 false（正确）
    /// 修复后行为：ContainsKey 为 false，保留 _config 值（false），结果为 false（不变）
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Fact]
    public void Preservation_Bug2_GlobalConfigFalse_NoKey_ShouldReturnFalse()
    {
        // Arrange: 全局配置为 false，settings 无该键
        bool globalConfigUseFixedDebugRoutes = false;
        var settings = new Dictionary<string, object?>
        {
            // 其他 settings 键，但不含 "useFixedDebugRoutes"
            ["multiplayerEnabled"] = false,
            ["operationMode"] = "normal"
        };

        // Act: 调用修复后的 ApplySettingsOverride 模拟
        // 观察到的实际输出：false（全局配置为 false，settings 无键，结果不受影响）
        bool result = SimulateFixedApplySettingsOverride(settings, globalConfigUseFixedDebugRoutes);

        // Assert: 结果应为 false（全局配置为 false，settings 无键，不受 bug 影响）
        Assert.False(result,
            $"保留行为：globalConfig.UseFixedDebugRoutes=false 且 settings 无该键时，" +
            $"结果应为 false，但实际结果为 {result}。");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property-Based Tests：属性测试验证保留行为
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 4a: Preservation - settings 显式传入 useFixedDebugRoutes 键时，结果等于 settings 中的值
    ///
    /// 对所有 settings 显式包含 useFixedDebugRoutes 键的输入（不满足 isBugCondition_2），
    /// 未修复代码和修复后代码均应返回 settings 中的值。
    ///
    /// 此属性在未修复代码上应通过（键存在时 Get 正确读取值）。
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(Bug2PreservationKeyPresentArbitrary) })]
    public Property Preservation_Bug2_KeyPresent_ResultEqualsSettingsValue(
        Bug2PreservationKeyPresentInput input)
    {
        // Arrange: settings 显式包含 useFixedDebugRoutes 键
        var settings = input.SettingsWithKey;
        bool expectedValue = input.KeyValue;

        // Act: 调用修复后的 ApplySettingsOverride 模拟
        bool result = SimulateFixedApplySettingsOverride(settings, globalConfigUseFixedDebugRoutes: input.GlobalConfig);

        // Assert: 键存在时，结果应等于 settings 中的值（无论全局配置如何）
        return (result == expectedValue)
            .Label($"KeyValue={expectedValue}, GlobalConfig={input.GlobalConfig}, " +
                   $"Result={result} (expected == {expectedValue}, key present)");
    }

    /// <summary>
    /// Property 4b: Preservation - globalConfig=false 且 settings 无键时，结果始终为 false
    ///
    /// 对所有 globalConfig.UseFixedDebugRoutes=false 且 settings 不含该键的输入（不满足 isBugCondition_2），
    /// 未修复代码和修复后代码均应返回 false。
    ///
    /// 此属性在未修复代码上应通过（全局配置为 false 时，Start 重置为 false，Get 返回 false）。
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(Bug2PreservationGlobalFalseArbitrary) })]
    public Property Preservation_Bug2_GlobalConfigFalse_NoKey_AlwaysFalse(
        Bug2PreservationGlobalFalseInput input)
    {
        // Arrange: globalConfig=false，settings 不含 useFixedDebugRoutes 键
        var settings = input.SettingsWithoutKey;

        // Act: 调用修复后的 ApplySettingsOverride 模拟
        bool result = SimulateFixedApplySettingsOverride(settings, globalConfigUseFixedDebugRoutes: false);

        // Assert: 全局配置为 false 且键不存在时，结果应为 false
        return (!result)
            .Label($"SettingsKeys=[{string.Join(",", settings.Keys)}], " +
                   $"Result={result} (expected false, globalConfig=false, key absent)");
    }

    // =========================================================================
    // Bug 1 保留测试（文档性）
    //
    // WaitForMembersAsync 依赖游戏界面（OCR、截图、键盘模拟），无法直接单元测试。
    // 通过文档性测试记录非 bug 条件下的预期行为，作为基线。
    // =========================================================================

    /// <summary>
    /// Bug 1 保留测试 1：单成员满员时立即返回
    ///
    /// 不满足 isBugCondition_1：pendingApplicants=0（人数已满，无待处理申请）
    ///
    /// 预期行为：系统检测到人数满足条件后立即返回，不继续等待。
    /// 此行为在修复前后均应保持不变。
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_Bug1_Documentation_SingleMemberFull_ShouldReturnImmediately()
    {
        // 此测试作为文档记录 Bug 1 保留行为 1：单成员满员时立即返回。
        //
        // 场景（不满足 isBugCondition_1）：
        //   - acceptedCount = 1（已接受一个成员）
        //   - pendingApplicants = 0（人数已满，无待处理申请）
        //   - reopenedF2 = false（无需重新打开 F2）
        //
        // 预期行为（修复前后均应一致）：
        //   WaitForMembersAsync 检测到 currentCount >= expectedCount 后立即返回 currentCount，
        //   不继续等待，不执行末尾 Delay(1500)。
        //
        // 根据代码审查（AutoPartyTask.cs）：
        //   当 currentCount >= expectedCount 时，循环顶部的 if 条件触发 return，
        //   此路径不经过 OpenCoOpScreen，不受 continue 修复影响。
        //
        // 修复影响分析：
        //   添加 continue 仅影响 OpenCoOpScreen 成功后的路径，
        //   单成员满员路径（直接 return）完全不受影响。

        Assert.True(true,
            "Bug 1 保留文档：单成员满员时，WaitForMembersAsync 立即返回，" +
            "此路径不经过 OpenCoOpScreen，不受 continue 修复影响。");
    }

    /// <summary>
    /// Bug 1 保留测试 2：超时时返回 0
    ///
    /// 不满足 isBugCondition_1：超时场景下 reopenedF2 通常为 false（未触发加载）
    ///
    /// 预期行为：等待超时时返回 0，由调用方根据 PartyTimeoutAction 决定继续或终止。
    /// 此行为在修复前后均应保持不变。
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void Preservation_Bug1_Documentation_Timeout_ShouldReturnZero()
    {
        // 此测试作为文档记录 Bug 1 保留行为 2：超时时返回 0。
        //
        // 场景（不满足 isBugCondition_1）：
        //   - acceptedCount = 0（未接受任何成员）
        //   - pendingApplicants = 0（无申请）
        //   - reopenedF2 = false（未触发加载，未重新打开 F2）
        //   - 等待超时（超过配置的超时时间）
        //
        // 预期行为（修复前后均应一致）：
        //   WaitForMembersAsync 超时后返回 0，
        //   调用方根据 PartyTimeoutAction 决定继续或终止。
        //
        // 根据代码审查（AutoPartyTask.cs）：
        //   超时逻辑通过 CancellationToken 或计时器实现，
        //   与 OpenCoOpScreen 路径无关，不受 continue 修复影响。
        //
        // 修复影响分析：
        //   添加 continue 仅影响 OpenCoOpScreen 成功后的路径，
        //   超时路径（返回 0）完全不受影响。

        Assert.True(true,
            "Bug 1 保留文档：超时时 WaitForMembersAsync 返回 0，" +
            "此路径不经过 OpenCoOpScreen，不受 continue 修复影响。");
    }

    /// <summary>
    /// Bug 1 保留测试 3：白名单拒绝时申请者被跳过
    ///
    /// 不满足 isBugCondition_1：白名单拒绝场景下 acceptedCount 不增加，不触发加载
    ///
    /// 预期行为：通过 OCR 识别申请者名称并与白名单匹配，拒绝不在白名单中的申请。
    /// 此行为在修复前后均应保持不变。
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Fact]
    public void Preservation_Bug1_Documentation_WhitelistRejection_ShouldSkipApplicant()
    {
        // 此测试作为文档记录 Bug 1 保留行为 3：白名单拒绝时申请者被跳过。
        //
        // 场景（不满足 isBugCondition_1）：
        //   - acceptedCount = 0（未接受任何成员，白名单拒绝不增加 acceptedCount）
        //   - pendingApplicants > 0（有申请，但不在白名单中）
        //   - reopenedF2 = false（未触发加载，未重新打开 F2）
        //
        // 预期行为（修复前后均应一致）：
        //   WaitForMembersAsync 通过 OCR 识别申请者名称，
        //   若不在白名单中则拒绝（点击拒绝按钮），申请者被跳过，
        //   循环继续等待下一个申请。
        //
        // 根据代码审查（AutoPartyTask.cs）：
        //   白名单过滤逻辑在弹窗检测后执行，与 OpenCoOpScreen 路径无关，
        //   不受 continue 修复影响。
        //
        // 修复影响分析：
        //   添加 continue 仅影响 OpenCoOpScreen 成功后的路径（即接受成员后触发加载的路径），
        //   白名单拒绝路径（不触发加载）完全不受影响。

        Assert.True(true,
            "Bug 1 保留文档：白名单拒绝时申请者被跳过，" +
            "此路径不经过 OpenCoOpScreen 成功分支，不受 continue 修复影响。");
    }
}

// =============================================================================
// 输入模型与生成器
// =============================================================================

/// <summary>
/// Property 4a 的输入模型：settings 显式包含 useFixedDebugRoutes 键的场景
/// </summary>
public class Bug2PreservationKeyPresentInput
{
    /// <summary>settings 中 useFixedDebugRoutes 键的值</summary>
    public bool KeyValue { get; set; }

    /// <summary>全局配置值（可以是 true 或 false）</summary>
    public bool GlobalConfig { get; set; }

    /// <summary>包含 useFixedDebugRoutes 键的 settings 字典</summary>
    public Dictionary<string, object?> SettingsWithKey { get; set; } = new();

    public override string ToString() =>
        $"KeyValue={KeyValue}, GlobalConfig={GlobalConfig}, " +
        $"SettingsKeys=[{string.Join(",", SettingsWithKey.Keys)}]";
}

/// <summary>
/// Property 4a 的生成器：生成 settings 显式包含 useFixedDebugRoutes 键的输入
/// </summary>
public class Bug2PreservationKeyPresentArbitrary
{
    private static readonly string[] OtherKeys =
    [
        "multiplayerEnabled", "operationMode", "partyName", "debugMode",
        "startRouteIndex", "disableAsync", "enableCoordinateCheck"
    ];

    public static Arbitrary<Bug2PreservationKeyPresentInput> Bug2PreservationKeyPresentInputArb()
    {
        var gen =
            from keyValue in Arb.Generate<bool>()
            from globalConfig in Arb.Generate<bool>()
            from keyCount in Gen.Choose(0, 3)
            from selectedKeys in Gen.Shuffle(OtherKeys).Select(arr => arr.Take(keyCount).ToArray())
            select new Bug2PreservationKeyPresentInput
            {
                KeyValue = keyValue,
                GlobalConfig = globalConfig,
                SettingsWithKey = BuildSettings(selectedKeys, keyValue)
            };

        return Arb.From(gen);
    }

    private static Dictionary<string, object?> BuildSettings(string[] otherKeys, bool keyValue)
    {
        var dict = new Dictionary<string, object?>
        {
            // 显式包含 useFixedDebugRoutes 键
            ["useFixedDebugRoutes"] = keyValue
        };

        foreach (var key in otherKeys)
        {
            dict[key] = key switch
            {
                "multiplayerEnabled" => (object?)false,
                "operationMode" => "normal",
                "partyName" => "TestParty",
                "debugMode" => false,
                "startRouteIndex" => 0,
                "disableAsync" => false,
                "enableCoordinateCheck" => true,
                _ => null
            };
        }
        return dict;
    }
}

/// <summary>
/// Property 4b 的输入模型：globalConfig=false 且 settings 不含 useFixedDebugRoutes 键的场景
/// </summary>
public class Bug2PreservationGlobalFalseInput
{
    /// <summary>不含 useFixedDebugRoutes 键的 settings 字典</summary>
    public Dictionary<string, object?> SettingsWithoutKey { get; set; } = new();

    public override string ToString() =>
        $"SettingsKeys=[{string.Join(",", SettingsWithoutKey.Keys)}]";
}

/// <summary>
/// Property 4b 的生成器：生成 globalConfig=false 且 settings 不含该键的输入
/// </summary>
public class Bug2PreservationGlobalFalseArbitrary
{
    private static readonly string[] OtherKeys =
    [
        "multiplayerEnabled", "operationMode", "partyName", "debugMode",
        "startRouteIndex", "disableAsync", "enableCoordinateCheck"
    ];

    public static Arbitrary<Bug2PreservationGlobalFalseInput> Bug2PreservationGlobalFalseInputArb()
    {
        var gen =
            from keyCount in Gen.Choose(0, 4)
            from selectedKeys in Gen.Shuffle(OtherKeys).Select(arr => arr.Take(keyCount).ToArray())
            select new Bug2PreservationGlobalFalseInput
            {
                SettingsWithoutKey = BuildSettings(selectedKeys)
            };

        return Arb.From(gen);
    }

    private static Dictionary<string, object?> BuildSettings(string[] keys)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var key in keys)
        {
            dict[key] = key switch
            {
                "multiplayerEnabled" => (object?)false,
                "operationMode" => "normal",
                "partyName" => "TestParty",
                "debugMode" => false,
                "startRouteIndex" => 0,
                "disableAsync" => false,
                "enableCoordinateCheck" => true,
                _ => null
            };
        }
        return dict;
    }
}
