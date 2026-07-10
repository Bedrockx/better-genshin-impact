#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 距离预判帧 (0,0) 重试结果：要么"恢复出一个非 (0,0) 坐标"，要么"窗口耗尽仍 (0,0)"。
/// </summary>
public readonly struct PreDistanceResolveResult
{
    /// <summary>true=重试拿到非 (0,0) 坐标（调用方应改写 currentPos 落入既有真分支）；
    /// false=窗口耗尽仍 (0,0)（调用方退化到现状等价行为，维持 __coordTrusted=true）。</summary>
    public bool Recovered { get; }

    /// <summary>Recovered=true 时恢复出的坐标（非 (0,0)，可能仍是远点，交由既有守卫块判定）。</summary>
    public Point2f Pos { get; }

    /// <summary>实际 reSampleStable 调用次数，用于日志 / PBT 断言受 deadline 约束。</summary>
    public int Attempts { get; }

    private PreDistanceResolveResult(bool recovered, Point2f pos, int attempts)
    {
        Recovered = recovered; Pos = pos; Attempts = attempts;
    }
    public static PreDistanceResolveResult Recover(Point2f pos, int attempts) => new(true, pos, attempts);
    public static PreDistanceResolveResult Fail(int attempts) => new(false, default, attempts);
}

/// <summary>
/// 距离预判帧 (0,0) 重试的决策纯函数（无 IO，PBT 友好）。
/// </summary>
public static class KazuhaReturnPreDistanceDecisions
{
    /// <summary>
    /// 是否还在重试时间窗内：当前时刻 nowMs 距起始 startMs 的已用时长 &lt; timeoutMs。
    /// timeoutMs &lt;= 0 时视为"窗口已耗尽"（不重试），保证不死循环。
    /// </summary>
    public static bool ShouldRetryWithinDeadline(long startMs, long nowMs, int timeoutMs)
    {
        if (timeoutMs <= 0) return false;
        return (nowMs - startMs) < timeoutMs;
    }

    /// <summary>识别坐标是否为有效（非 (0,0)）坐标 —— 恢复成功判据。</summary>
    public static bool IsRecovered(Point2f pos) => pos is not { X: 0, Y: 0 };
}

/// <summary>
/// 万叶战后回点"距离预判帧 (0,0)"入口前重试编排 helper。
///
/// 由 hoeing-kazuha-return-predistance-zero-coord-skip-moveto-fix spec 引入。职责单一：
/// 当距离预判帧 GetPosition 返回 (0,0)（小地图识别不出）时，在约 2 秒时间窗内用更鲁棒的
/// GetPositionStable（全局匹配，局部失败/跳跃>150 自动 Reset 全局）分多次重试，任一次拿到非
/// (0,0) 坐标即返回 Recovered；窗口耗尽仍 (0,0) 则返回 Fail，由调用方退化到现状等价行为。
///
/// 与 KazuhaReturnReseedGuard 并列、独立。本 helper 作用于守卫块**入口前**，不替换守卫块内部
/// 远点 / (0,0) reSampleStable 逻辑。IO（reSampleStable / delay / 时间源 / log）用委托注入，PBT 可 stub。
///
/// Q4：不重播种 —— reSampleStable 走 GetPositionStable 全局匹配，不依赖 _prevX/_prevY 局部锚点，
/// 调用前不 SetPrevPosition。
/// Q5：每次迭代检查 ct，OperationCanceledException 照常上抛（不吞）。
/// </summary>
public static class KazuhaReturnPreDistanceResolver
{
    /// <param name="timeoutMs">重试总时长窗口上限（= AutoHoeingConfig.KazuhaReturnPreDistanceZeroRetryTimeoutMs，默认 2000）。</param>
    /// <param name="reSampleStable">全局匹配重识别委托：CaptureToRectArea + Navigation.GetPositionStable。不重播种（Q4）。</param>
    /// <param name="delay">每次重试间延时委托：Task.Delay(KazuhaReturnReseedGuard.ReseedReSampleDelayMs, ct)（复用现 100ms，Q1）。</param>
    /// <param name="nowMs">单调时间源委托（返回当前毫秒数，便于 PBT 注入假时钟）：生产传 () => Environment.TickCount64。</param>
    /// <param name="log">日志委托。</param>
    /// <param name="ct">取消令牌；每次迭代检查，OperationCanceledException 照常上抛（Q5）。</param>
    public static async Task<PreDistanceResolveResult> ResolveZeroCoordAsync(
        int timeoutMs,
        Func<Point2f> reSampleStable,
        Func<CancellationToken, Task> delay,
        Func<long> nowMs,
        Action<string> log,
        CancellationToken ct)
    {
        var startMs = nowMs();
        int attempts = 0;
        log($"[距离预判] 识别失败(0,0)，进入约 {timeoutMs}ms GetPositionStable 全局匹配重试窗");

        while (KazuhaReturnPreDistanceDecisions.ShouldRetryWithinDeadline(startMs, nowMs(), timeoutMs))
        {
            ct.ThrowIfCancellationRequested();   // Q5：取消照常上抛
            await delay(ct);                      // 复用现有约 100ms 延时，让小地图重绘稳定
            attempts++;
            var pos = reSampleStable();           // CaptureToRectArea + Navigation.GetPositionStable（全局匹配）

            if (KazuhaReturnPreDistanceDecisions.IsRecovered(pos))
            {
                log($"[距离预判] 第{attempts}次 GetPositionStable 恢复有效坐标，采纳并走 MoveTo 接近");
                return PreDistanceResolveResult.Recover(pos, attempts);
            }
            log($"[距离预判] 第{attempts}次 GetPositionStable 仍识别失败(0,0)，继续重试");
        }

        log($"[距离预判] 约 {timeoutMs}ms 窗口内 GetPositionStable 共 {attempts} 次仍 (0,0)，退化到现状跳过 MoveTo");
        return PreDistanceResolveResult.Fail(attempts);
    }
}
