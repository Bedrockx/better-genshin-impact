using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Common;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 小地图匹配调参配置（纯本地调试用，不进 RoomConfig / SignalR）。
///
/// 把原先散落在 MiniMapMatchConfig（静态常量）、NavigationInstance（const/硬编码）、
/// MiniMapPositionDiagnostics（静态字段）里的"魔法数字"集中为可观察、可持久化、
/// 可在 UI 实时调参（无需重启）的配置项。
///
/// 默认值严格等于 main-OldTeaBag-B61 当前硬编码值，保证不调参 = 现状行为（Requirement 7）。
/// </summary>
[Serializable]
public partial class MiniMapMatchTuningConfig : ObservableObject
{
    // ===== 硬编码默认值常量（= 现状）。校验回落与默认初值统一引用这里 =====
    public const float DefaultRank0Threshold = 0.99f;
    public const float DefaultRank1Threshold = 0.97f;
    public const float DefaultRank2Threshold = 0.95f;
    public const int DefaultGlobalMatchFallbackThreshold = 1;
    public const int DefaultGetPositionLockTimeoutMs = 100;
    public const bool DefaultZeroCoordGuardEnabled = true;
    public const bool DefaultDiagnosticsEnabled = false;
    public const bool DefaultDumpFailedFrame = false;
    public const int DefaultDumpThrottleMs = 2000;
    public const double DefaultGlobalMatchJumpGuardThreshold = 50;

    // ===== Requirement 1：置信度阈值组（rank0/1/2）=====
    [ObservableProperty] private float _rank0ConfidenceThreshold = DefaultRank0Threshold;
    [ObservableProperty] private float _rank1ConfidenceThreshold = DefaultRank1Threshold;
    [ObservableProperty] private float _rank2ConfidenceThreshold = DefaultRank2Threshold;

    // ===== Requirement 2：全局匹配回退连续失败阈值 =====
    [ObservableProperty] private int _globalMatchFallbackThreshold = DefaultGlobalMatchFallbackThreshold;

    // ===== Requirement 3：GetPosition 锁等待超时（ms）=====
    [ObservableProperty] private int _getPositionLockTimeoutMs = DefaultGetPositionLockTimeoutMs;

    // ===== Requirement 4：零坐标防呆开关 =====
    [ObservableProperty] private bool _zeroCoordGuardEnabled = DefaultZeroCoordGuardEnabled;

    // ===== Requirement 5：诊断设施开关 =====
    [ObservableProperty] private bool _diagnosticsEnabled = DefaultDiagnosticsEnabled;
    [ObservableProperty] private bool _dumpFailedFrame = DefaultDumpFailedFrame;
    [ObservableProperty] private int _dumpThrottleMs = DefaultDumpThrottleMs;

    // ===== 全局匹配回退跳变保护：全局命中坐标距回退前锚点超此阈值 → 判误匹配丢弃。
    // 仅作用于"局部失败→全局回退命中"分支，不影响正常局部匹配帧。
    // 默认 50（与万叶聚物 RecognizedPositionGuardThreshold 一致），<=0 视为关闭保护。=====
    [ObservableProperty] private double _globalMatchJumpGuardThreshold = DefaultGlobalMatchJumpGuardThreshold;
}

/// <summary>
/// 小地图匹配调参的纯函数校验器（无副作用，PBT 目标）。
/// 所有方法：输入候选值 → 返回 (生效值, 是否回落)。调用方负责按"是否回落"打告警日志。
/// </summary>
public static class MiniMapMatchTuningValidator
{
    /// <summary>
    /// 校验置信度阈值组：任一元素 ∉ [0,1]（含 NaN）→ 整组回落默认（Requirement 1.5）。
    /// 返回长度恒为 3 的数组（个数天然=3，Requirement 1.4 边界裁剪由调用点 Math.Clamp 负责）。
    /// </summary>
    public static (float[] thresholds, bool fellBack) ValidateThresholds(float r0, float r1, float r2)
    {
        if (IsValidUnit(r0) && IsValidUnit(r1) && IsValidUnit(r2))
        {
            return (new[] { r0, r1, r2 }, false);
        }
        return (new[]
        {
            MiniMapMatchTuningConfig.DefaultRank0Threshold,
            MiniMapMatchTuningConfig.DefaultRank1Threshold,
            MiniMapMatchTuningConfig.DefaultRank2Threshold
        }, true);
    }

    private static bool IsValidUnit(float v) => !float.IsNaN(v) && v >= 0f && v <= 1f;

    /// <summary>全局回退阈值 &lt; 1 → 回落默认（Requirement 2.4）。</summary>
    public static (int value, bool fellBack) ValidateFallbackThreshold(int candidate)
        => candidate < 1
            ? (MiniMapMatchTuningConfig.DefaultGlobalMatchFallbackThreshold, true)
            : (candidate, false);

    /// <summary>锁超时 &lt; 0 → 回落默认 100（Requirement 3.4）。</summary>
    public static (int value, bool fellBack) ValidateLockTimeoutMs(int candidate)
        => candidate < 0
            ? (MiniMapMatchTuningConfig.DefaultGetPositionLockTimeoutMs, true)
            : (candidate, false);

    /// <summary>存图节流 &lt; 0 → 回落默认 2000（Requirement 5.5）。</summary>
    public static (int value, bool fellBack) ValidateDumpThrottleMs(int candidate)
        => candidate < 0
            ? (MiniMapMatchTuningConfig.DefaultDumpThrottleMs, true)
            : (candidate, false);
}

/// <summary>
/// 小地图调参非法值告警节流帮助类。桥接 getter 每帧调用，校验失败若每次都打日志会刷爆日志。
/// 对每类非法值做 30s 时间节流打一条 LogWarning。告警虽节流但绝不静默。
/// </summary>
public static class MiniMapTuningWarn
{
    private static long _lastConfidenceTicks;
    private static long _lastFallbackTicks;
    private static long _lastLockTicks;
    private static long _lastThrottleTicks;
    private const long IntervalTicks = 30 * TimeSpan.TicksPerSecond; // 30s 一条，避免刷屏

    public static void OnceConfidence(float r0, float r1, float r2)
    {
        if (!ShouldLog(ref _lastConfidenceTicks)) return;
        TaskControl.Logger.LogWarning(
            "[小地图调参] 置信度阈值非法(rank0={R0},rank1={R1},rank2={R2})，已整组回落默认 {{0.99,0.97,0.95}}", r0, r1, r2);
    }

    public static void OnceFallbackThreshold(int v)
    {
        if (!ShouldLog(ref _lastFallbackTicks)) return;
        TaskControl.Logger.LogWarning("[小地图调参] 全局回退阈值非法({V}<1)，已回落默认 2", v);
    }

    public static void OnceLockTimeout(int v)
    {
        if (!ShouldLog(ref _lastLockTicks)) return;
        TaskControl.Logger.LogWarning("[小地图调参] GetPosition 锁超时非法({V}<0)，已回落默认 100ms", v);
    }

    public static void OnceDumpThrottle(int v)
    {
        if (!ShouldLog(ref _lastThrottleTicks)) return;
        TaskControl.Logger.LogWarning("[小地图调参] 存图节流非法({V}<0)，已回落默认 2000ms", v);
    }

    private static bool ShouldLog(ref long lastTicks)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref lastTicks);
        if (now - last < IntervalTicks) return false;
        Interlocked.Exchange(ref lastTicks, now);
        return true;
    }
}
