using System;
using System.Collections.Generic;
using System.Linq;
using BgiCoordinatorServer.Services;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BgiCoordinatorServer.Tests;

/// <summary>
/// Bug 探索测试（hoeing-multiplayer-member-syncpoint-timeout-falling-behind-fix / Task 1）。
///
/// 目标：在【未修复】代码上坐实"现状无落后者放行谓词 / 孤立落后者死等满 120s"这一缺陷。
///
/// 重要：目标纯函数 BgiCoordinatorServer.Services.LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller
/// 目前【尚未存在】（将在 Task 2 创建）。因此本测试文件【预期编译失败】（找不到该类型，CS0103/CS0246），
/// 这正是 bugfix workflow 探索测试在未修复代码上"预期失败"的表现——坐实"现状缺少放行谓词"。
///
/// 这些用例后续会被 Task 5 / 6.1 复用为 Fix Checking，故断言方向已写对：
///   - 孤立落后者（syncProgress 严格小于所有其他在线玩家）→ 期望放行 true
///   - 进度相等 / route_sync_done 全局点 → 期望不放行 false（防误放正常碰头 / 全局点不介入）
/// </summary>
public class LaggingMemberReleaseDecisionsTests
{
    // 用例 1：集合点孤立（C050_fight_1，progress=21000004），其它三人都已严格走过
    //   → 期望修复后放行 true（孤立落后者，别让它死等满 120s）
    [Fact]
    public void RallyPoint_IsolatedLaggingCaller_ShouldRelease()
    {
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(
            21000004,
            new long[] { 29000000, 29000000, 28001000 });
        Assert.True(actual);
    }

    // 用例 2：传送点孤立（progress=20000000），两位队友都已严格走过
    //   → 期望修复后放行 true（孤立落后者）
    [Fact]
    public void TeleportPoint_IsolatedLaggingCaller_ShouldRelease()
    {
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(
            20000000,
            new long[] { 21000000, 22000000 });
        Assert.True(actual);
    }

    // 用例 3：进度相等（非缺陷对照）——存在某队友 CurrentProgress == syncProgress
    //   是"马上要到这个点"的正常碰头玩家，必须继续等
    //   → 期望不放行 false（严格小于防误放）
    [Fact]
    public void EqualProgress_NormalRendezvous_ShouldNotRelease()
    {
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(
            20000000,
            new long[] { 20000000, 20000005 });
        Assert.False(actual);
    }

    // 用例 4：route_sync_done 全局同步点（syncProgress=-1 < 0）→ 新逻辑一律不介入，保持"等所有"
    //   → 期望不放行 false
    [Fact]
    public void GlobalSyncPoint_NegativeSyncProgress_ShouldNotRelease()
    {
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(
            -1,
            new long[] { 1, 2, 3 });
        Assert.False(actual);
    }

    // ===== FsCheck PBT（Task 5 / design Testing Strategy §Property-Based Tests）=====

    // Property 1：严格小于所有他人 → 放行（孤立落后者）
    // **Validates: Requirements 2.1, 2.2, 2.3**
    [Property]
    public Property StrictlyBehindAll_Releases(long sp, NonEmptyArray<long> deltas)
    {
        if (sp < 0) sp = -sp; // 限定 syncProgress >= 0
        // others = sp + 正增量，保证每个都严格大于 sp
        var others = deltas.Get.Select(d => sp + (Math.Abs(d) % 1000 + 1)).ToList();
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(sp, others);
        return actual.ToProperty();
    }

    // Property 3：存在某他人进度相等(==sp) → 不放行（防误放正常碰头）
    // **Validates: Requirements 2.1, 3.3**
    [Property]
    public Property AnyEqual_DoesNotRelease(long sp, NonEmptyArray<long> deltas)
    {
        if (sp < 0) sp = -sp;
        var others = deltas.Get.Select(d => sp + (Math.Abs(d) % 1000 + 1)).ToList();
        others.Add(sp); // 注入一个进度相等者
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(sp, others);
        return (!actual).ToProperty();
    }

    // 存在某他人进度更小 → 不放行（caller 不再落后所有人）
    // **Validates: Requirements 2.1, 3.3**
    [Property]
    public Property AnySmaller_DoesNotRelease(long sp, NonEmptyArray<long> deltas)
    {
        if (sp < 0) sp = -sp;
        if (sp < 1) sp = 1;
        var others = deltas.Get.Select(d => sp + (Math.Abs(d) % 1000 + 1)).ToList();
        others.Add(sp - 1); // 注入一个严格更小者
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(sp, others);
        return (!actual).ToProperty();
    }

    // 边界：others 为空 / null → 不放行
    // **Validates: Requirements 3.1**
    [Fact]
    public void EmptyOrNullOthers_DoesNotRelease()
    {
        Assert.False(LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(20000000, new List<long>()));
        Assert.False(LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(20000000, null));
    }

    // 边界：syncProgress < 0（route_sync_done 全局点）→ 一律不放行
    // **Validates: Requirements 3.7**
    [Property]
    public Property NegativeSyncProgress_NeverReleases(NonEmptyArray<long> others)
    {
        var actual = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(-1, others.Get.ToList());
        return (!actual).ToProperty();
    }

    // 与现状豁免叠加一致：所有 others 都 > sp 时，谓词与现状豁免条件 All(cp>sp) 方向一致
    // **Validates: Requirements 3.3**
    [Property]
    public Property ConsistentWithExistingExemption(long sp, PositiveInt a, PositiveInt b)
    {
        if (sp < 0) sp = -sp;
        var others = new List<long> { sp + a.Get, sp + b.Get };
        var release = LaggingMemberReleaseDecisions.ShouldReleaseLaggingCaller(sp, others);
        var allExemptedByExisting = others.All(cp => cp > sp); // 现状豁免条件
        return (release == allExemptedByExisting).ToProperty();
    }
}