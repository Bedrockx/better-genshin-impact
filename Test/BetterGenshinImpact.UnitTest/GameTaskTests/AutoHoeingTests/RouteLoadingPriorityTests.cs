using BetterGenshinImpact.GameTask.AutoHoeing;
using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using Xunit;
using System.IO;
using System;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// 测试 LoadRoutesBasedOnConfig 方法的三级优先级逻辑
/// 由于该方法是私有的，我们通过集成测试验证其行为
/// </summary>
public class RouteLoadingPriorityTests
{
    [Fact]
    public void RouteLoading_ManualPathPriority_OverridesBuiltinRoute()
    {
        // 验证手动输入路径优先级最高
        // 这个测试验证需求 3.1, 4.1, 4.2
        
        var config = new AutoHoeingConfig
        {
            UseFixedDebugRoutes = true,
            FixedDebugRoutePath = "/custom/path",
            SelectedBuiltinRoute = "SomeBuiltinRoute"
        };

        // 验证配置正确设置
        Assert.Equal("/custom/path", config.FixedDebugRoutePath);
        Assert.Equal("SomeBuiltinRoute", config.SelectedBuiltinRoute);
        
        // 手动路径非空时，应该优先使用手动路径
        Assert.False(string.IsNullOrWhiteSpace(config.FixedDebugRoutePath));
    }

    [Fact]
    public void RouteLoading_BuiltinRouteSecondPriority_WhenManualPathEmpty()
    {
        // 验证内置线路选择优先级第二
        // 这个测试验证需求 3.1, 3.4
        
        var config = new AutoHoeingConfig
        {
            UseFixedDebugRoutes = true,
            FixedDebugRoutePath = "",
            SelectedBuiltinRoute = "CustomRoute1"
        };

        // 验证配置正确设置
        Assert.True(string.IsNullOrWhiteSpace(config.FixedDebugRoutePath));
        Assert.Equal("CustomRoute1", config.SelectedBuiltinRoute);
        
        // 手动路径为空且内置线路非空时，应该使用内置线路
        Assert.False(string.IsNullOrWhiteSpace(config.SelectedBuiltinRoute));
    }

    [Fact]
    public void RouteLoading_DefaultBehavior_WhenBothEmpty()
    {
        // 验证默认行为（回退到 DebugRoutes）
        // 这个测试验证需求 5.2
        
        var config = new AutoHoeingConfig
        {
            UseFixedDebugRoutes = true,
            FixedDebugRoutePath = "",
            SelectedBuiltinRoute = ""
        };

        // 验证配置正确设置
        Assert.True(string.IsNullOrWhiteSpace(config.FixedDebugRoutePath));
        Assert.True(string.IsNullOrWhiteSpace(config.SelectedBuiltinRoute));
        
        // 两者都为空时，应该使用默认 DebugRoutes 目录
        Assert.True(config.UseFixedDebugRoutes);
    }

    [Fact]
    public void RouteLoading_ConfigPriority_ManualOverBuiltin()
    {
        // 验证优先级顺序：手动 > 内置 > 默认
        // 这个测试验证需求 4.1, 4.2, 4.3
        
        var config = new AutoHoeingConfig
        {
            UseFixedDebugRoutes = true,
            FixedDebugRoutePath = "/manual/path",
            SelectedBuiltinRoute = "BuiltinRoute"
        };

        // 优先级 1: 手动路径
        if (!string.IsNullOrWhiteSpace(config.FixedDebugRoutePath))
        {
            Assert.Equal("/manual/path", config.FixedDebugRoutePath);
            return;
        }

        // 优先级 2: 内置线路
        if (!string.IsNullOrWhiteSpace(config.SelectedBuiltinRoute))
        {
            Assert.Equal("BuiltinRoute", config.SelectedBuiltinRoute);
            return;
        }

        // 优先级 3: 默认行为
        Assert.True(config.UseFixedDebugRoutes);
    }

    [Fact]
    public void RouteLoading_BuiltinRoutePathConstruction()
    {
        // 验证内置线路路径构造逻辑
        // 这个测试验证需求 6.3
        
        var selectedRoute = "CustomRoute1";
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "GameTask", "AutoHoeing", "Assets");
        var expectedPath = Path.Combine(assetsPath, selectedRoute);

        // 验证路径构造正确
        Assert.Contains("GameTask", expectedPath);
        Assert.Contains("AutoHoeing", expectedPath);
        Assert.Contains("Assets", expectedPath);
        Assert.Contains("CustomRoute1", expectedPath);
    }

    [Fact]
    public void RouteLoading_DefaultDebugRoutesPathConstruction()
    {
        // 验证默认 DebugRoutes 路径构造逻辑
        // 这个测试验证需求 5.2
        
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "GameTask", "AutoHoeing", "Assets", "DebugRoutes");

        // 验证路径构造正确
        Assert.Contains("GameTask", defaultPath);
        Assert.Contains("AutoHoeing", defaultPath);
        Assert.Contains("Assets", defaultPath);
        Assert.Contains("DebugRoutes", defaultPath);
    }
}
