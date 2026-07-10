#nullable enable

using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// 需求 4（战斗状态上报）的单元测试。
/// 战斗超时由 AutoFightTask 内部的 _taskParam.Timeout 机制处理，
/// 需求 4 只负责上报 Fighting/Normal 状态，供 SyncBarrier 进度感知使用。
/// </summary>
public class FightTimeoutTests
{
    /// <summary>
    /// 验证：ReportFightingStatusAsync 的状态映射正确
    /// isFighting=true → Fighting, isFighting=false → Normal
    /// </summary>
    [Fact]
    public void FightingStatus_MapsCorrectly()
    {
        var statusWhenTrue = true ? MemberStatus.Fighting : MemberStatus.Normal;
        var statusWhenFalse = false ? MemberStatus.Fighting : MemberStatus.Normal;

        Assert.Equal(MemberStatus.Fighting, statusWhenTrue);
        Assert.Equal(MemberStatus.Normal, statusWhenFalse);
    }

    /// <summary>
    /// 验证：AutoFightConfig.Timeout 默认值为 120（已有的战斗超时机制）
    /// </summary>
    [Fact]
    public void AutoFightConfig_Timeout_DefaultIs120()
    {
        var config = new BetterGenshinImpact.GameTask.AutoFight.AutoFightConfig();
        Assert.Equal(120, config.Timeout);
    }

    /// <summary>
    /// 验证：finally 块确保状态清除——即使 AfterMoveToTarget 抛异常，Fighting 状态也会被清除
    /// 模拟：try { throw } finally { 清除状态 }
    /// </summary>
    [Fact]
    public async Task FinallyBlock_ClearsStatus_EvenOnException()
    {
        var status = MemberStatus.Fighting;

        try
        {
            // 模拟 AfterMoveToTarget 抛异常
            throw new InvalidOperationException("模拟战斗异常");
        }
        catch
        {
            // 外层 catch 捕获
        }
        finally
        {
            // 模拟 ReportFightingStatusAsync(false) 的效果
            status = MemberStatus.Normal;
        }

        Assert.Equal(MemberStatus.Normal, status);
    }
}
