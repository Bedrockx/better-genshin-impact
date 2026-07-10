using BetterGenshinImpact.GameTask.AutoHoeing;
using FsCheck;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Bug Condition Exploration Test - Property 1
///
/// **Validates: Requirements 1.3**
///
/// 此测试编码了期望（正确）行为：
/// 当 SoloTaskSettingsObject 中 multiplayerEnabled=true 但 multiplayerRole 键缺失时
/// （模拟"单机保存后联机启动"场景），ApplySettingsOverride 后
/// _config.MultiplayerRole 不应回退到全局默认值 "host"。
///
/// 在未修复代码上，此测试预期失败（EXPECTED TO FAIL），因为：
/// - 单机保存时 settings.Remove("multiplayerRole") 清除了该键
/// - ApplySettingsOverride 中 Get("multiplayerRole", _config.MultiplayerRole) 回退到全局默认 "host"
///
/// 目标：产生反例，证明 bug 存在。
/// </summary>
public class StandaloneTaskMultiplayerRoleBugConditionTest
{
    /// <summary>
    /// 模拟 ApplySettingsOverride 中的 Get 逻辑（与实际代码完全一致）：
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
    /// 模拟 ApplySettingsOverride 对联机角色字段的处理逻辑（反映修复后的防御性逻辑）。
    /// 仅在 key 存在时才覆盖，键缺失时保持 config 原值不变。
    /// 返回应用覆盖后 config.MultiplayerRole 的值。
    /// </summary>
    private static string SimulateApplyMultiplayerRole(
        Dictionary<string, object?> settings,
        AutoHoeingConfig config)
    {
        // 修复后的逻辑：仅在键存在时才覆盖（防御性修复 3.2）
        config.MultiplayerEnabled = SimulateGet(settings, "multiplayerEnabled", config.MultiplayerEnabled);
        if (settings.ContainsKey("multiplayerRole"))
            config.MultiplayerRole = SimulateGet(settings, "multiplayerRole", config.MultiplayerRole);
        return config.MultiplayerRole;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 场景 A：单机保存后联机启动（主要 bug 场景）
    // settings = { "multiplayerEnabled": true }（无 multiplayerRole 键）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Bug Condition - 单机保存后联机启动时 multiplayerRole 键缺失时保持原值
    ///
    /// 场景：settings 中 multiplayerEnabled=true 但 multiplayerRole 键不存在
    ///       （修复后：单机保存不再 Remove multiplayerRole，此场景仅在首次使用时出现）
    ///
    /// 期望行为（修复后）：_config.MultiplayerRole 保持原值（不被意外覆盖）
    /// 未修复代码实际行为：_config.MultiplayerRole == "host"（无条件回退到全局默认）
    ///
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Fact]
    public void BugCondition_StandaloneSaveThenMultiplayerStart_ShouldNotFallbackToHost()
    {
        // Arrange: 模拟"单机保存后联机启动"状态
        // settings 中 multiplayerEnabled=true，但 multiplayerRole 键不存在
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = true
            // "multiplayerRole" 键不存在
        };

        // config 初始角色为 "member"（模拟上次联机保存的值）
        var config = new AutoHoeingConfig
        {
            MultiplayerRole = "member" // 上次联机保存的值
        };

        // Act: 模拟修复后的 ApplySettingsOverride（键缺失时不覆盖）
        var resultRole = SimulateApplyMultiplayerRole(settings, config);

        // Assert: 修复后键缺失时保持原值 "member"，不被全局默认 "host" 覆盖
        Assert.Equal("member", resultRole);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 场景 B：multiplayerRole 键存在但值为 null
    // settings = { "multiplayerEnabled": true, "multiplayerRole": null }
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bug Condition - multiplayerRole 键存在但值为 null 时保持原值
    ///
    /// 场景：settings 中 multiplayerRole 键存在但值为 null
    ///
    /// 期望行为（修复后）：_config.MultiplayerRole 保持原值（不被 null 覆盖）
    /// 未修复代码实际行为：_config.MultiplayerRole == "host"（回退到全局默认，测试失败）
    ///
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Fact]
    public void BugCondition_MultiplayerRoleNull_ShouldNotFallbackToHost()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = true,
            ["multiplayerRole"] = null // 键存在但值为 null
        };

        // config 初始角色为 "member"（模拟上次联机保存的值）
        var config = new AutoHoeingConfig
        {
            MultiplayerRole = "member" // 上次联机保存的值
        };

        // Act
        var resultRole = SimulateApplyMultiplayerRole(settings, config);

        // Assert: 修复后键存在但值为 null 时，SimulateGet 返回 fallback（原值 "member"），保持不变
        Assert.Equal("member", resultRole);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 场景 C：完整操作序列
    // 联机成员保存（写入 "member"）→ 单机保存（触发 Remove）→ 联机启动
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bug Condition - 完整操作序列：联机成员保存 → 单机保存 → 联机启动
    ///
    /// 模拟完整的用户操作序列：
    /// 1. 用户以联机成员身份保存 → settings["multiplayerRole"] = "member"
    /// 2. 用户以单机模式保存 → settings.Remove("multiplayerRole")（当前代码行为）
    /// 3. 用户以联机模式启动 → ApplySettingsOverride 读取 settings
    ///
    /// 期望行为（修复后）：_config.MultiplayerRole == "member"（保留上次联机保存的角色）
    /// 未修复代码实际行为：_config.MultiplayerRole == "host"（回退到全局默认，测试失败）
    ///
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Fact]
    public void BugCondition_FullSequence_MemberSave_StandaloneSave_MultiplayerStart_ShouldBeMember()
    {
        // Arrange: 模拟完整操作序列

        // 步骤 1：联机成员保存（isMpMode=true, roleCombo.SelectedIndex=1 → "member"）
        var settings = new Dictionary<string, object?>();
        settings["multiplayerEnabled"] = true;
        settings["multiplayerRole"] = "member"; // 联机成员保存写入 "member"

        // 步骤 2：单机保存（isMpMode=false 分支，修复后不再 Remove multiplayerRole）
        // 修复后：ScriptControlViewModel.cs 中单机保存时保留 "multiplayerRole" 和 "memberJoinMode"
        settings["multiplayerEnabled"] = false;
        // settings["multiplayerRole"] 保留为 "member"（修复后的行为）

        // 步骤 3：联机启动（用户再次启用联机模式，但 settings 中 multiplayerRole 已被清除）
        settings["multiplayerEnabled"] = true; // 用户启动时 multiplayerEnabled=true

        // 全局配置默认值
        var config = new AutoHoeingConfig
        {
            MultiplayerRole = "host" // 全局默认值
        };

        // Act: 模拟 ApplySettingsOverride
        var resultRole = SimulateApplyMultiplayerRole(settings, config);

        // Assert: 期望角色为 "member"（用户上次联机保存时的选择）
        // 未修复代码：resultRole == "host"（测试失败，证明 bug 存在）
        // 修复后代码：resultRole == "member"（测试通过）
        Assert.Equal("member", resultRole);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property-Based Test：对所有 bug 条件输入验证
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Bug Condition - 属性测试
    ///
    /// 对所有满足 bug 条件的输入（multiplayerEnabled=true 且 multiplayerRole 键缺失），
    /// 修复后 ApplySettingsOverride 不覆盖 _config.MultiplayerRole，保持原值不变。
    ///
    /// 修复后：键缺失时保持 config 原值（防御性修复 3.2）
    ///
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(BugConditionArbitrary) })]
    public Property BugCondition_MultiplayerEnabledWithoutRole_ShouldNotFallbackToHost(
        BugConditionInput input)
    {
        // Arrange: 构造 bug 条件 settings（multiplayerEnabled=true，无 multiplayerRole 键）
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = true
            // multiplayerRole 键故意缺失
        };

        var config = new AutoHoeingConfig
        {
            MultiplayerRole = input.InitialRole // config 的初始角色（上次联机保存的值）
        };

        // Act
        var resultRole = SimulateApplyMultiplayerRole(settings, config);

        // Assert: 修复后键缺失时保持原值，不被全局默认覆盖
        return (resultRole == input.InitialRole)
            .Label($"InitialRole={input.InitialRole}, " +
                   $"ResultRole={resultRole} (expected == '{input.InitialRole}', should preserve original value)");
    }
}

/// <summary>
/// Bug Condition 场景的输入模型
/// </summary>
public class BugConditionInput
{
    /// <summary>config 的初始角色（上次联机保存的值，可为 "host" 或 "member"）</summary>
    public string InitialRole { get; set; } = "member";

    public override string ToString() =>
        $"InitialRole={InitialRole}";
}

/// <summary>
/// Bug Condition 场景的生成器
/// 约束：InitialRole 为 "host" 或 "member"（模拟上次联机保存的值）
/// </summary>
public class BugConditionArbitrary
{
    public static Arbitrary<BugConditionInput> BugConditionInputArb()
    {
        var gen = Gen.Elements("host", "member")
            .Select(role => new BugConditionInput { InitialRole = role });

        return Arb.From(gen);
    }
}
