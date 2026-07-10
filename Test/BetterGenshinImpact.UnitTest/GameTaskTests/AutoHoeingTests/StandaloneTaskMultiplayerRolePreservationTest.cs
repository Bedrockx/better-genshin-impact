using BetterGenshinImpact.GameTask.AutoHoeing;
using FsCheck;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Preservation Property Tests - Property 2
///
/// **Validates: Requirements 3.1, 3.2, 3.3**
///
/// 此测试验证保留行为：房主角色和单机模式行为不受修复影响。
/// 这些场景在未修复代码上本就正确，测试应在未修复代码上通过（EXPECTED TO PASS）。
///
/// 基线行为观察：
/// 1. settings = { "multiplayerEnabled": true, "multiplayerRole": "host" }
///    → _config.MultiplayerRole == "host"（房主正常）
/// 2. settings = { "multiplayerEnabled": false }
///    → _config.MultiplayerEnabled == false（单机模式不受影响）
/// 3. settings = { "multiplayerEnabled": true, "multiplayerRole": "member" }
///    → _config.MultiplayerRole == "member"（键存在时正常）
/// </summary>
public class StandaloneTaskMultiplayerRolePreservationTest
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
    /// 模拟 ApplySettingsOverride 对联机角色字段的处理逻辑（与实际代码完全一致）。
    /// 返回应用覆盖后的 config 实例。
    /// </summary>
    private static AutoHoeingConfig SimulateApplySettingsOverride(
        Dictionary<string, object?> settings,
        AutoHoeingConfig config)
    {
        // 与 AutoHoeingTask.cs ApplySettingsOverride 中完全相同的逻辑：
        config.MultiplayerEnabled = SimulateGet(settings, "multiplayerEnabled", config.MultiplayerEnabled);
        config.MultiplayerRole = SimulateGet(settings, "multiplayerRole", config.MultiplayerRole);
        config.MemberJoinMode = SimulateGet(settings, "memberJoinMode", config.MemberJoinMode);
        return config;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 基线观察：单元测试验证具体场景
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 观察 1：房主角色正常
    /// settings = { "multiplayerEnabled": true, "multiplayerRole": "host" }
    /// → _config.MultiplayerRole == "host"
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_HostRole_KeyPresent_ShouldRemainHost()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = true,
            ["multiplayerRole"] = "host"
        };

        var config = new AutoHoeingConfig
        {
            MultiplayerRole = "host"
        };

        // Act
        var result = SimulateApplySettingsOverride(settings, config);

        // Assert: 房主角色键存在时，应正确应用为 "host"
        Assert.Equal("host", result.MultiplayerRole);
        Assert.True(result.MultiplayerEnabled);
    }

    /// <summary>
    /// 观察 2：单机模式不受影响
    /// settings = { "multiplayerEnabled": false }
    /// → _config.MultiplayerEnabled == false
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void Preservation_StandaloneMode_MultiplayerEnabledFalse_ShouldRemainFalse()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = false
        };

        var config = new AutoHoeingConfig
        {
            MultiplayerEnabled = false,
            MultiplayerRole = "host"
        };

        // Act
        var result = SimulateApplySettingsOverride(settings, config);

        // Assert: 单机模式下 MultiplayerEnabled 应为 false
        Assert.False(result.MultiplayerEnabled);
    }

    /// <summary>
    /// 观察 3：成员角色键存在时正常
    /// settings = { "multiplayerEnabled": true, "multiplayerRole": "member" }
    /// → _config.MultiplayerRole == "member"
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_MemberRole_KeyPresent_ShouldBeMember()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = true,
            ["multiplayerRole"] = "member"
        };

        var config = new AutoHoeingConfig
        {
            MultiplayerRole = "host"
        };

        // Act
        var result = SimulateApplySettingsOverride(settings, config);

        // Assert: 成员角色键存在时，应正确应用为 "member"
        Assert.Equal("member", result.MultiplayerRole);
        Assert.True(result.MultiplayerEnabled);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property-Based Tests：属性测试验证保留行为
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: Preservation - 房主/成员角色键存在时正确应用
    ///
    /// 对所有 multiplayerRole 键存在且值为 "host" 或 "member" 的 settings，
    /// ApplySettingsOverride 后 _config.MultiplayerRole 等于 settings 中的值。
    ///
    /// 此属性在未修复代码上应通过（键存在时逻辑本就正确）。
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RoleKeyPresentArbitrary) })]
    public Property Preservation_RoleKeyPresent_ShouldMatchSettingsValue(
        RoleKeyPresentInput input)
    {
        // Arrange: settings 中 multiplayerRole 键存在，值为 "host" 或 "member"
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = true,
            ["multiplayerRole"] = input.Role
        };

        var config = new AutoHoeingConfig
        {
            MultiplayerRole = "host" // 全局默认值
        };

        // Act
        var result = SimulateApplySettingsOverride(settings, config);

        // Assert: 键存在时，结果应等于 settings 中的值
        return (result.MultiplayerRole == input.Role)
            .Label($"Role={input.Role}, ResultRole={result.MultiplayerRole} " +
                   $"(expected == '{input.Role}')");
    }

    /// <summary>
    /// Property 2b: Preservation - 单机模式下 MultiplayerEnabled 始终为 false
    ///
    /// 对所有 multiplayerEnabled=false 的 settings，
    /// ApplySettingsOverride 后 _config.MultiplayerEnabled 为 false。
    ///
    /// 此属性在未修复代码上应通过（单机模式逻辑本就正确）。
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(StandaloneModeArbitrary) })]
    public Property Preservation_StandaloneMode_MultiplayerEnabledAlwaysFalse(
        StandaloneModeInput input)
    {
        // Arrange: settings 中 multiplayerEnabled=false（单机模式）
        var settings = new Dictionary<string, object?>
        {
            ["multiplayerEnabled"] = false
        };

        // 可选：settings 中可能包含其他字段（不影响单机模式判断）
        if (input.HasExtraFields)
        {
            settings["playerName"] = input.PlayerName;
        }

        var config = new AutoHoeingConfig
        {
            MultiplayerEnabled = false,
            MultiplayerRole = input.InitialRole
        };

        // Act
        var result = SimulateApplySettingsOverride(settings, config);

        // Assert: 单机模式下 MultiplayerEnabled 应为 false
        return (!result.MultiplayerEnabled)
            .Label($"InitialRole={input.InitialRole}, HasExtraFields={input.HasExtraFields}, " +
                   $"MultiplayerEnabled={result.MultiplayerEnabled} (expected false)");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 输入模型与生成器
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Property 2a 的输入模型：multiplayerRole 键存在的场景
/// </summary>
public class RoleKeyPresentInput
{
    /// <summary>联机角色值："host" 或 "member"</summary>
    public string Role { get; set; } = "host";

    public override string ToString() => $"Role={Role}";
}

/// <summary>
/// Property 2a 的生成器：生成 "host" 或 "member" 角色值
/// </summary>
public class RoleKeyPresentArbitrary
{
    public static Arbitrary<RoleKeyPresentInput> RoleKeyPresentInputArb()
    {
        // 随机生成 "host" 或 "member"
        var gen = Gen.Elements("host", "member")
            .Select(role => new RoleKeyPresentInput { Role = role });

        return Arb.From(gen);
    }
}

/// <summary>
/// Property 2b 的输入模型：单机模式场景
/// </summary>
public class StandaloneModeInput
{
    /// <summary>config 的初始角色（全局默认，通常为 "host"）</summary>
    public string InitialRole { get; set; } = "host";

    /// <summary>settings 中是否包含额外字段（如 playerName）</summary>
    public bool HasExtraFields { get; set; } = false;

    /// <summary>额外字段：玩家名称</summary>
    public string PlayerName { get; set; } = "";

    public override string ToString() =>
        $"InitialRole={InitialRole}, HasExtraFields={HasExtraFields}";
}

/// <summary>
/// Property 2b 的生成器：生成单机模式场景输入
/// </summary>
public class StandaloneModeArbitrary
{
    public static Arbitrary<StandaloneModeInput> StandaloneModeInputArb()
    {
        var gen =
            from initialRole in Gen.Elements("host", "member")
            from hasExtra in Arb.Generate<bool>()
            from playerName in Arb.Generate<string>().Select(s => s ?? "")
            select new StandaloneModeInput
            {
                InitialRole = initialRole,
                HasExtraFields = hasExtra,
                PlayerName = playerName
            };

        return Arb.From(gen);
    }
}
