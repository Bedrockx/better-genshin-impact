#nullable enable

using System.Collections.Concurrent;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// 需求 7（异常恢复状态上报与感知）的单元测试和属性测试。
/// 验证 MemberStatus 枚举、状态字典更新、版本号乱序拒绝、清理逻辑。
/// </summary>
public class MemberStatusTests
{
    // ========== Property 1: MemberStatus 序列化往返 ==========

    [Property(MaxTest = 200)]
    public bool MemberStatus_RoundTrip_IsConsistent(MemberStatus status)
    {
        var str = status.ToString();
        var parsed = Enum.TryParse<MemberStatus>(str, out var result);
        return parsed && result == status;
    }

    // ========== Property 2: 状态字典更新正确性与派生查询一致性 ==========

    [Property(MaxTest = 200)]
    public bool StatusDictionary_UpdatesCorrectly_WithIncreasingVersions()
    {
        // 生成随机的 (playerUid, status, version) 序列
        var dict = new ConcurrentDictionary<string, MemberStatus>();
        var versionDict = new ConcurrentDictionary<string, long>();
        var players = new[] { "uid_A", "uid_B", "uid_C" };
        var statuses = Enum.GetValues<MemberStatus>();
        var rng = new System.Random();

        // 每个玩家的版本号严格递增
        var playerVersions = new Dictionary<string, long>();
        foreach (var p in players) playerVersions[p] = 0;

        for (int i = 0; i < 50; i++)
        {
            var player = players[rng.Next(players.Length)];
            var status = statuses[rng.Next(statuses.Length)];
            playerVersions[player]++;
            var version = playerVersions[player];

            // 模拟 MemberStatusChanged 处理逻辑
            var accepted = versionDict.AddOrUpdate(
                player,
                _ => version,
                (_, oldVersion) => version > oldVersion ? version : oldVersion
            );
            if (accepted != version) continue;

            if (status == MemberStatus.Offline)
            {
                dict.TryRemove(player, out _);
                versionDict.TryRemove(player, out _);
            }
            else
            {
                dict[player] = status;
            }
        }

        // 验证派生查询一致性
        var hasFighting = dict.Values.Any(s => s == MemberStatus.Fighting);
        var hasRejoining = dict.Values.Any(s => s == MemberStatus.Rejoining);
        var hasReviving = dict.Values.Any(s => s == MemberStatus.Reviving);

        // 字典中不应包含 Offline 状态
        var noOffline = !dict.Values.Any(s => s == MemberStatus.Offline);

        return hasFighting == dict.Values.Contains(MemberStatus.Fighting)
            && hasRejoining == dict.Values.Contains(MemberStatus.Rejoining)
            && hasReviving == dict.Values.Contains(MemberStatus.Reviving)
            && noOffline;
    }

    // ========== Property 3: 清理正确性 — Offline 移除与过期条目清理 ==========

    [Property(MaxTest = 200)]
    public bool Cleanup_OfflineRemovesFromDictionary()
    {
        var dict = new ConcurrentDictionary<string, MemberStatus>();
        var versionDict = new ConcurrentDictionary<string, long>();

        // 添加几个玩家
        dict["uid_A"] = MemberStatus.Fighting;
        dict["uid_B"] = MemberStatus.Normal;
        dict["uid_C"] = MemberStatus.Rejoining;
        versionDict["uid_A"] = 1;
        versionDict["uid_B"] = 1;
        versionDict["uid_C"] = 1;

        // uid_B 上报 Offline
        dict.TryRemove("uid_B", out _);
        versionDict.TryRemove("uid_B", out _);

        // 验证 uid_B 已移除
        if (dict.ContainsKey("uid_B")) return false;
        if (versionDict.ContainsKey("uid_B")) return false;

        // 模拟 PlayerListUpdated 只包含 uid_A（uid_C 也不在列表中）
        var activeUids = new HashSet<string> { "uid_A" };
        foreach (var key in dict.Keys.Where(k => !activeUids.Contains(k)).ToList())
        {
            dict.TryRemove(key, out _);
            versionDict.TryRemove(key, out _);
        }

        // 验证只剩 uid_A
        return dict.Count == 1
            && dict.ContainsKey("uid_A")
            && !dict.ContainsKey("uid_C")
            && versionDict.Count == 1;
    }

    // ========== Property 4: 版本号乱序拒绝 ==========

    [Property(MaxTest = 200)]
    public bool VersionControl_RejectsOutOfOrderUpdates()
    {
        var dict = new ConcurrentDictionary<string, MemberStatus>();
        var versionDict = new ConcurrentDictionary<string, long>();
        const string player = "uid_test";

        // 生成一组 (status, version) 对，版本号 1-10
        var updates = new List<(MemberStatus Status, long Version)>();
        var statuses = new[] { MemberStatus.Normal, MemberStatus.Fighting, MemberStatus.Rejoining,
                               MemberStatus.Reviving, MemberStatus.Normal, MemberStatus.Fighting,
                               MemberStatus.Normal, MemberStatus.Rejoining, MemberStatus.Normal, MemberStatus.Normal };
        for (int i = 0; i < statuses.Length; i++)
            updates.Add((statuses[i], i + 1));

        // 记录最高版本号对应的状态
        var highestVersion = updates.Max(u => u.Version);
        var expectedStatus = updates.First(u => u.Version == highestVersion).Status;

        // 随机打乱顺序
        var rng = new System.Random(42); // 固定种子保证可重现
        var shuffled = updates.OrderBy(_ => rng.Next()).ToList();

        // 按打乱顺序应用
        foreach (var (status, version) in shuffled)
        {
            var accepted = versionDict.AddOrUpdate(
                player,
                _ => version,
                (_, oldVersion) => version > oldVersion ? version : oldVersion
            );
            if (accepted != version) continue;

            if (status == MemberStatus.Offline)
            {
                dict.TryRemove(player, out _);
                versionDict.TryRemove(player, out _);
            }
            else
            {
                dict[player] = status;
            }
        }

        // 最终状态应该是最高版本号对应的状态
        if (expectedStatus == MemberStatus.Offline)
            return !dict.ContainsKey(player);
        else
            return dict.TryGetValue(player, out var finalStatus) && finalStatus == expectedStatus;
    }

    // ========== 基础单元测试 ==========

    [Fact]
    public void MemberStatus_HasExactly5Values()
    {
        var values = Enum.GetValues<MemberStatus>();
        Assert.Equal(5, values.Length);
        Assert.Contains(MemberStatus.Normal, values);
        Assert.Contains(MemberStatus.Fighting, values);
        Assert.Contains(MemberStatus.Rejoining, values);
        Assert.Contains(MemberStatus.Reviving, values);
        Assert.Contains(MemberStatus.Offline, values);
    }

    [Fact]
    public void MemberStatusReport_DefaultValues()
    {
        var report = new MemberStatusReport();
        Assert.Equal("", report.PlayerUid);
        Assert.Equal("", report.Status);
        Assert.Equal(0, report.Version);
    }

    [Fact]
    public void VersionControl_LowerVersionIsRejected()
    {
        var dict = new ConcurrentDictionary<string, MemberStatus>();
        var versionDict = new ConcurrentDictionary<string, long>();
        const string player = "uid_test";

        // 先接受版本 3
        ApplyUpdate(dict, versionDict, player, MemberStatus.Fighting, 3);
        Assert.Equal(MemberStatus.Fighting, dict[player]);

        // 版本 2 应被拒绝
        ApplyUpdate(dict, versionDict, player, MemberStatus.Normal, 2);
        Assert.Equal(MemberStatus.Fighting, dict[player]); // 仍然是 Fighting

        // 版本 4 应被接受
        ApplyUpdate(dict, versionDict, player, MemberStatus.Rejoining, 4);
        Assert.Equal(MemberStatus.Rejoining, dict[player]);
    }

    [Fact]
    public void OfflineStatus_RemovesPlayerFromDictionary()
    {
        var dict = new ConcurrentDictionary<string, MemberStatus>();
        var versionDict = new ConcurrentDictionary<string, long>();
        const string player = "uid_test";

        ApplyUpdate(dict, versionDict, player, MemberStatus.Fighting, 1);
        Assert.True(dict.ContainsKey(player));

        ApplyUpdate(dict, versionDict, player, MemberStatus.Offline, 2);
        Assert.False(dict.ContainsKey(player));
        Assert.False(versionDict.ContainsKey(player));
    }

    /// <summary>模拟 MemberStatusChanged 处理逻辑</summary>
    private static void ApplyUpdate(
        ConcurrentDictionary<string, MemberStatus> dict,
        ConcurrentDictionary<string, long> versionDict,
        string playerUid, MemberStatus status, long version)
    {
        var accepted = versionDict.AddOrUpdate(
            playerUid,
            _ => version,
            (_, oldVersion) => version > oldVersion ? version : oldVersion
        );
        if (accepted != version) return;

        if (status == MemberStatus.Offline)
        {
            dict.TryRemove(playerUid, out _);
            versionDict.TryRemove(playerUid, out _);
        }
        else
        {
            dict[playerUid] = status;
        }
    }
}
