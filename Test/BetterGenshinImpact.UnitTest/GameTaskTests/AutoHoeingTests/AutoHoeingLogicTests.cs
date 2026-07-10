using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using BetterGenshinImpact.GameTask.AutoHoeing.Services;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

public class AutoHoeingLogicTests
{
    #region ParseDescription

    [Fact]
    public void ParseDescription_WithTimeAndMonsters_ParsesCorrectly()
    {
        var desc = "预计用时45秒。包含以下怪物：3只丘丘人、1只丘丘暴徒。";
        var (time, monsters) = RouteInfoLoader.ParseDescription(desc);

        Assert.Equal(45, time);
        Assert.Equal(2, monsters.Count);
        Assert.Equal(3, monsters["丘丘人"]);
        Assert.Equal(1, monsters["丘丘暴徒"]);
    }

    [Fact]
    public void ParseDescription_NoTime_DefaultsTo60()
    {
        var desc = "包含以下怪物：2只史莱姆。";
        var (time, monsters) = RouteInfoLoader.ParseDescription(desc);

        Assert.Equal(60, time);
        Assert.Single(monsters);
        Assert.Equal(2, monsters["史莱姆"]);
    }

    [Fact]
    public void ParseDescription_Empty_DefaultValues()
    {
        var (time, monsters) = RouteInfoLoader.ParseDescription("");
        Assert.Equal(60, time);
        Assert.Empty(monsters);
    }

    [Fact]
    public void ParseDescription_NoMonsters_EmptyDict()
    {
        var desc = "预计用时30秒。";
        var (time, monsters) = RouteInfoLoader.ParseDescription(desc);
        Assert.Equal(30, time);
        Assert.Empty(monsters);
    }

    [Fact]
    public void ParseDescription_DecimalTime_ParsesCorrectly()
    {
        var desc = "预计用时45.5秒。包含以下怪物：1只遗迹守卫。";
        var (time, monsters) = RouteInfoLoader.ParseDescription(desc);
        Assert.Equal(45.5, time);
        Assert.Equal(1, monsters["遗迹守卫"]);
    }

    #endregion

    #region SelfOptimizer

    [Fact]
    public void SelfOptimizer_SevenRecords_TrimsMinMaxAndAverages()
    {
        var records = new List<double> { 40, 42, 43, 44, 45, 46, 50 };
        var result = SelfOptimizer.CalculateAdjustedTime(records, 60, 0);

        // Remove max(50) and min(40), average of {42,43,44,45,46} = 44
        Assert.Equal(44, result);
    }

    [Fact]
    public void SelfOptimizer_LessThanSeven_FillsWithDefault()
    {
        var records = new List<double> { 30, 40 };
        // curiosityFactor=0, default=60, fills 5 more with 60*(1-0)=60
        // pool: [30, 40, 60, 60, 60, 60, 60]
        // remove max(60) and min(30): [40, 60, 60, 60, 60]
        // average = 56
        var result = SelfOptimizer.CalculateAdjustedTime(records, 60, 0);
        Assert.Equal(56, result);
    }

    [Fact]
    public void SelfOptimizer_WithCuriosityFactor_AffectsFill()
    {
        var records = new List<double>(); // empty
        // curiosityFactor=0.5, default=60, fills 7 with 60*(1-0.5)=30
        // pool: [30,30,30,30,30,30,30]
        // remove max(30) and min(30): [30,30,30,30,30]
        // average = 30
        var result = SelfOptimizer.CalculateAdjustedTime(records, 60, 0.5);
        Assert.Equal(30, result);
    }

    [Fact]
    public void SelfOptimizer_Disabled_KeepsOriginalTime()
    {
        var routes = new List<RouteInfo>
        {
            new() { EstimatedTime = 50, AdjustedTime = 50, Records = new List<double> { 30, 30, 30, 30, 30, 30, 30 } }
        };
        SelfOptimizer.Apply(routes, disabled: true, curiosityFactor: 0);
        Assert.Equal(50, routes[0].AdjustedTime);
    }

    #endregion

    #region RouteMarker

    [Fact]
    public void RouteMarker_ExcludeTag_MarksUnavailable()
    {
        var routes = new List<RouteInfo>
        {
            new() { FileName = "route1.json", FullPath = "/path/route1.json", Tags = new List<string> { "蕈兽" }, MonsterInfo = new() }
        };
        var groupTags = new List<List<string>> { new() { "蕈兽" }, new() { "蕈兽" } };
        var excludeTags = new List<string> { "蕈兽" };

        RouteMarker.MarkRoutes(routes, groupTags, new List<string>(), excludeTags);
        Assert.False(routes[0].Available);
    }

    [Fact]
    public void RouteMarker_PriorityTag_MarksPrioritized()
    {
        var routes = new List<RouteInfo>
        {
            new() { FileName = "route1.json", FullPath = "/path/route1.json", Tags = new List<string> { "高危" }, MonsterInfo = new() }
        };
        var groupTags = new List<List<string>> { new(), new() };
        var priorityTags = new List<string> { "高危" };

        RouteMarker.MarkRoutes(routes, groupTags, priorityTags, new List<string>());
        Assert.True(routes[0].Prioritized);
        Assert.True(routes[0].Available);
    }

    [Fact]
    public void RouteMarker_UniqueTag_MarksUnavailable()
    {
        // "蕈兽" only in group0, not in group1-9 → unique → route unavailable
        var routes = new List<RouteInfo>
        {
            new() { FileName = "route1.json", FullPath = "/path/route1.json", Tags = new List<string> { "蕈兽" }, MonsterInfo = new() }
        };
        var groupTags = new List<List<string>>
        {
            new() { "蕈兽", "高危" },  // group0 = union of all
            new() { "高危" },           // group1
        };

        RouteMarker.MarkRoutes(routes, groupTags, new List<string>(), new List<string>());
        // "蕈兽" is in group0 but not in group1 → unique tag → unavailable
        Assert.False(routes[0].Available);
    }

    [Fact]
    public void RouteMarker_NoMatchingTags_StaysAvailable()
    {
        var routes = new List<RouteInfo>
        {
            new() { FileName = "route1.json", FullPath = "/path/route1.json", Tags = new List<string> { "须弥" }, MonsterInfo = new() }
        };
        var groupTags = new List<List<string>> { new() { "蕈兽" }, new() { "蕈兽" } };

        RouteMarker.MarkRoutes(routes, groupTags, new List<string>(), new List<string>());
        Assert.True(routes[0].Available);
    }

    #endregion

    #region RouteGroupAssigner

    [Fact]
    public void GroupAssigner_NoGroupTags_AssignsToGroup1()
    {
        var routes = new List<RouteInfo>
        {
            new() { Selected = true, Tags = new List<string> { "须弥" } }
        };
        var groupTags = new List<List<string>>
        {
            new() { "蕈兽" },  // group0
            new() { "蕈兽" },  // group1
        };

        RouteGroupAssigner.AssignGroups(routes, groupTags);
        Assert.Equal(1, routes[0].Group);
    }

    [Fact]
    public void GroupAssigner_MatchesGroup2Tag_AssignsToGroup2()
    {
        var routes = new List<RouteInfo>
        {
            new() { Selected = true, Tags = new List<string> { "蕈兽" } }
        };
        var groupTags = new List<List<string>>
        {
            new() { "蕈兽", "高危" },  // group0
            new() { "蕈兽" },           // group1 → maps to group 2
            new() { "高危" },           // group2 → maps to group 3
        };

        RouteGroupAssigner.AssignGroups(routes, groupTags);
        Assert.Equal(2, routes[0].Group);
    }

    [Fact]
    public void GroupAssigner_NotSelected_SkipsAssignment()
    {
        var routes = new List<RouteInfo>
        {
            new() { Selected = false, Tags = new List<string> { "蕈兽" }, Group = 0 }
        };
        var groupTags = new List<List<string>> { new() { "蕈兽" }, new() { "蕈兽" } };

        RouteGroupAssigner.AssignGroups(routes, groupTags);
        Assert.Equal(0, routes[0].Group);
    }

    #endregion

    #region TimeRestrictionChecker

    [Fact]
    public void TimeRestriction_ParseSingleHour()
    {
        var checker = new TimeRestrictionChecker();
        checker.ParseRestrictions("8");
        // Just verify it doesn't throw
    }

    [Fact]
    public void TimeRestriction_ParseRange()
    {
        var checker = new TimeRestrictionChecker();
        checker.ParseRestrictions("8-11");
    }

    [Fact]
    public void TimeRestriction_ParseMinuteRange()
    {
        var checker = new TimeRestrictionChecker();
        checker.ParseRestrictions("23:11-23:55");
    }

    [Fact]
    public void TimeRestriction_ParseMultiple()
    {
        var checker = new TimeRestrictionChecker();
        checker.ParseRestrictions("8，12-14，23:00-23:59");
    }

    [Fact]
    public void TimeRestriction_EmptyString_NoRestrictions()
    {
        var checker = new TimeRestrictionChecker();
        checker.ParseRestrictions("");
        Assert.False(checker.IsInRestrictedPeriod());
    }

    #endregion

    #region RouteSelector

    [Fact]
    public void RouteSelector_SelectsRoutesToMeetTargets()
    {
        var routes = BuildTestRoutes();
        var selector = new RouteSelector();
        var result = selector.SelectOptimalRoutes(routes, 0.25, 10, 20, "原文件顺序");

        Assert.True(result.TotalElites >= 10);
        Assert.True(result.TotalMonsters >= 20);
        Assert.True(routes.Any(r => r.Selected));
    }

    [Fact]
    public void RouteSelector_GreedyPrune_RemovesRedundant()
    {
        var routes = BuildTestRoutes();
        var selector = new RouteSelector();
        // Low targets so pruning can remove some
        var result = selector.SelectOptimalRoutes(routes, 0.25, 3, 5, "原文件顺序");

        var selectedCount = routes.Count(r => r.Selected);
        // Should have pruned some routes
        Assert.True(selectedCount < routes.Count);
    }

    [Fact]
    public void RouteSelector_SortByEfficiency()
    {
        var routes = BuildTestRoutes();
        var selector = new RouteSelector();
        selector.SelectOptimalRoutes(routes, 0.25, 5, 10, "效率降序");

        var selected = routes.Where(r => r.Selected).ToList();
        if (selected.Count >= 2)
        {
            Assert.True(selected[0].E1 >= selected[1].E1);
        }
    }

    [Fact]
    public void RouteSelector_PrioritizedRoutes_AlwaysSelected()
    {
        var routes = BuildTestRoutes();
        routes[0].Prioritized = true;
        routes[0].Available = true;

        var selector = new RouteSelector();
        selector.SelectOptimalRoutes(routes, 0.25, 5, 10, "原文件顺序");

        Assert.True(routes[0].Selected);
    }

    private static List<RouteInfo> BuildTestRoutes()
    {
        return new List<RouteInfo>
        {
            new() { FileName = "r1.json", EliteMonsterCount = 3, NormalMonsterCount = 10, EliteMoraGain = 600, NormalMoraGain = 405, AdjustedTime = 40, Available = true, Tags = new() },
            new() { FileName = "r2.json", EliteMonsterCount = 5, NormalMonsterCount = 5, EliteMoraGain = 1000, NormalMoraGain = 202, AdjustedTime = 50, Available = true, Tags = new() },
            new() { FileName = "r3.json", EliteMonsterCount = 0, NormalMonsterCount = 15, EliteMoraGain = 0, NormalMoraGain = 607, AdjustedTime = 30, Available = true, Tags = new() },
            new() { FileName = "r4.json", EliteMonsterCount = 2, NormalMonsterCount = 8, EliteMoraGain = 400, NormalMoraGain = 324, AdjustedTime = 35, Available = true, Tags = new() },
            new() { FileName = "r5.json", EliteMonsterCount = 4, NormalMonsterCount = 12, EliteMoraGain = 800, NormalMoraGain = 486, AdjustedTime = 45, Available = true, Tags = new() },
        };
    }

    #endregion
}
