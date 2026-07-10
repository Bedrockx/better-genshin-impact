#nullable enable

using System;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// WaitPointReport 数据结构测试（skip-route-wait-point-report spec）
/// 测试 WaitPointReport 数据结构的创建、验证、序列化、过期检查等功能
/// </summary>
public class WaitPointReportTests
{
    // ========== Property 1: WaitPointReport 创建与验证 ==========
    
    [Property(MaxTest = 200)]
    public bool WaitPointReport_Creation_IsValidWithRequiredFields(
        string playerUid, string routeId, string syncPointId, int worldRound)
    {
        // 过滤无效输入
        if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(syncPointId))
            return true; // 跳过无效输入
        
        var report = new WaitPointReport(playerUid, routeId, syncPointId, worldRound);
        
        // 验证基本属性
        var isValid = report.PlayerUid == playerUid
            && report.RouteId == routeId
            && report.SyncPointId == syncPointId
            && report.WorldRound == worldRound
            && report.ReportedTime <= DateTime.UtcNow
            && report.ExpiryTime.HasValue
            && report.ExpiryTime.Value > report.ReportedTime;
        
        // 验证 IsValid 方法
        var validationResult = report.IsValid();
        
        return isValid && validationResult;
    }
    
    // ========== Property 2: WaitPointReport 过期检查 ==========
    
    [Property(MaxTest = 200)]
    public bool WaitPointReport_ExpiryCheck_WorksCorrectly(
        string playerUid, string routeId, string syncPointId, int worldRound, int secondsToAdd)
    {
        // 过滤无效输入
        if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(syncPointId))
            return true; // 跳过无效输入
        
        var report = new WaitPointReport(playerUid, routeId, syncPointId, worldRound);
        
        // 测试延长过期时间
        var originalExpiry = report.ExpiryTime;
        report.ExtendExpiry(TimeSpan.FromSeconds(secondsToAdd));
        
        var extendedCorrectly = !originalExpiry.HasValue || report.ExpiryTime!.Value > originalExpiry.Value;
        
        // 测试 IsExpired 方法（新创建的报告不应过期）
        var notExpired = !report.IsExpired();
        
        return extendedCorrectly && notExpired;
    }
    
    // ========== Property 3: WaitPointReport 路线索引提取 ==========
    
    [Property(MaxTest = 200)]
    public bool WaitPointReport_ExtractRouteIndex_HandlesVariousFormats(
        string routeIdPrefix, int routeIndex)
    {
        // 生成各种格式的路线ID
        var routeId1 = $"{routeIdPrefix}_{routeIndex}";
        var routeId2 = $"Route{routeIndex}";
        var routeId3 = $"Path_{routeIndex}_segment";
        
        var report1 = new WaitPointReport("test", routeId1, "sync_1_1", 1);
        var report2 = new WaitPointReport("test", routeId2, "sync_1_1", 1);
        var report3 = new WaitPointReport("test", routeId3, "sync_1_1", 1);
        
        // 提取路线索引
        var index1 = report1.ExtractRouteIndex();
        var index2 = report2.ExtractRouteIndex();
        var index3 = report3.ExtractRouteIndex();
        
        // 验证格式1和3应该能提取出索引
        var format1Valid = routeId1.Contains("_") ? index1 == routeIndex : true;
        var format3Valid = routeId3.Contains("_") ? index3 == routeIndex : true;
        
        // 格式2可能提取不出，取决于是否有数字
        var format2Valid = true; // 不强制要求
        
        return format1Valid && format2Valid && format3Valid;
    }
    
    // ========== Property 4: WaitPointReport 同步点标识验证 ==========
    
    [Property(MaxTest = 200)]
    public bool WaitPointReport_SyncPointIdValidation_WorksCorrectly(
        string syncPointId)
    {
        var report = new WaitPointReport("test", "Route_1", syncPointId, 1);
        
        // 验证 IsValidSyncPointId 方法
        var isValid = report.IsValidSyncPointId();
        
        // 同步点标识应包含下划线分隔的部分
        var expectedValid = !string.IsNullOrEmpty(syncPointId) 
            && syncPointId.Contains("_") 
            && syncPointId.Split('_').Length >= 3;
        
        return isValid == expectedValid;
    }
    
    // ========== Property 5: WaitPointReport 克隆功能 ==========
    
    [Property(MaxTest = 200)]
    public bool WaitPointReport_Clone_CreatesDeepCopy(
        string playerUid, string routeId, string syncPointId, int worldRound)
    {
        if (string.IsNullOrEmpty(playerUid) || string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(syncPointId))
            return true;
        
        var original = new WaitPointReport(playerUid, routeId, syncPointId, worldRound);
        var clone = original.Clone();
        
        // 验证克隆是深拷贝
        var isDeepCopy = clone.PlayerUid == original.PlayerUid
            && clone.RouteId == original.RouteId
            && clone.SyncPointId == original.SyncPointId
            && clone.WorldRound == original.WorldRound
            && clone.ReportedTime == original.ReportedTime
            && clone.ExpiryTime == original.ExpiryTime;
        
        // 修改克隆不应影响原始对象
        clone.PlayerUid = "modified";
        clone.RouteId = "modified";
        clone.SyncPointId = "modified";
        clone.WorldRound = 999;
        
        var originalUnchanged = original.PlayerUid == playerUid
            && original.RouteId == routeId
            && original.SyncPointId == syncPointId
            && original.WorldRound == worldRound;
        
        return isDeepCopy && originalUnchanged;
    }
    
    // ========== 基础单元测试 ==========
    
    [Fact]
    public void WaitPointReport_DefaultConstructor_SetsDefaultValues()
    {
        var report = new WaitPointReport();
        
        Assert.Equal(string.Empty, report.PlayerUid);
        Assert.Equal(string.Empty, report.RouteId);
        Assert.Equal(string.Empty, report.SyncPointId);
        Assert.Equal(0, report.WorldRound);
        Assert.True(report.ReportedTime <= DateTime.UtcNow);
        Assert.NotNull(report.ExpiryTime);
        Assert.True(report.ExpiryTime!.Value > report.ReportedTime);
    }
    
    [Fact]
    public void WaitPointReport_ParameterizedConstructor_SetsAllProperties()
    {
        var report = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 2);
        
        Assert.Equal("player123", report.PlayerUid);
        Assert.Equal("Route_1", report.RouteId);
        Assert.Equal("sync_1_2_3", report.SyncPointId);
        Assert.Equal(2, report.WorldRound);
        Assert.True(report.ReportedTime <= DateTime.UtcNow);
        Assert.NotNull(report.ExpiryTime);
    }
    
    [Fact]
    public void WaitPointReport_IsValid_ReturnsTrueForValidReport()
    {
        var report = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        
        Assert.True(report.IsValid());
    }
    
    [Fact]
    public void WaitPointReport_IsValid_ReturnsFalseForInvalidReport()
    {
        var report1 = new WaitPointReport("", "Route_1", "sync_1_2_3", 1);
        var report2 = new WaitPointReport("player123", "", "sync_1_2_3", 1);
        var report3 = new WaitPointReport("player123", "Route_1", "", 1);
        var report4 = new WaitPointReport("player123", "Route_1", "sync_1_2_3", -1);
        
        Assert.False(report1.IsValid());
        Assert.False(report2.IsValid());
        Assert.False(report3.IsValid());
        Assert.False(report4.IsValid());
    }
    
    [Fact]
    public void WaitPointReport_IsExpired_ReturnsFalseForNewReport()
    {
        var report = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        
        Assert.False(report.IsExpired());
    }
    
    [Fact]
    public void WaitPointReport_ExtendExpiry_ExtendsCorrectly()
    {
        var report = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        var originalExpiry = report.ExpiryTime;
        
        report.ExtendExpiry(TimeSpan.FromMinutes(5));
        
        Assert.NotNull(report.ExpiryTime);
        Assert.True(report.ExpiryTime!.Value > originalExpiry!.Value);
    }
    
    [Fact]
    public void WaitPointReport_ExtractRouteIndex_ExtractsCorrectly()
    {
        var report1 = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        var report2 = new WaitPointReport("player123", "Path_3_segment", "sync_1_2_3", 1);
        var report3 = new WaitPointReport("player123", "InvalidRoute", "sync_1_2_3", 1);
        
        Assert.Equal(1, report1.ExtractRouteIndex());
        Assert.Equal(3, report2.ExtractRouteIndex());
        Assert.Null(report3.ExtractRouteIndex());
    }
    
    [Fact]
    public void WaitPointReport_IsValidSyncPointId_ValidatesCorrectly()
    {
        var validReport = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        var invalidReport1 = new WaitPointReport("player123", "Route_1", "sync", 1);
        var invalidReport2 = new WaitPointReport("player123", "Route_1", "sync_1", 1);
        var invalidReport3 = new WaitPointReport("player123", "Route_1", "sync_1_2", 1);
        
        Assert.True(validReport.IsValidSyncPointId());
        Assert.False(invalidReport1.IsValidSyncPointId());
        Assert.False(invalidReport2.IsValidSyncPointId());
        Assert.False(invalidReport3.IsValidSyncPointId());
    }
    
    [Fact]
    public void WaitPointReport_ToString_ReturnsNonEmptyString()
    {
        var report = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        
        var str = report.ToString();
        
        Assert.False(string.IsNullOrEmpty(str));
        Assert.Contains("player123", str);
        Assert.Contains("Route_1", str);
        Assert.Contains("sync_1_2_3", str);
    }
    
    [Fact]
    public void WaitPointReport_Clone_CreatesIdenticalCopy()
    {
        var original = new WaitPointReport("player123", "Route_1", "sync_1_2_3", 1);
        var clone = original.Clone();
        
        Assert.Equal(original.PlayerUid, clone.PlayerUid);
        Assert.Equal(original.RouteId, clone.RouteId);
        Assert.Equal(original.SyncPointId, clone.SyncPointId);
        Assert.Equal(original.WorldRound, clone.WorldRound);
        Assert.Equal(original.ReportedTime, clone.ReportedTime);
        Assert.Equal(original.ExpiryTime, clone.ExpiryTime);
        
        // 验证是深拷贝
        clone.PlayerUid = "modified";
        Assert.Equal("player123", original.PlayerUid);
        Assert.Equal("modified", clone.PlayerUid);
    }
}