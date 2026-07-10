#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶回点"异常坐标重新播种 + 重识别 + 有限重试 + 仍异常放弃"的编排 helper。
///
/// 由 hoeing-kazuha-return-abnormal-coord-reseed-moveto-fix spec 引入，作为**两条回点路径
/// 共同调用的唯一编排入口**，结构性保证两条路径对称（防复发核心约束 bugfix §3.9）：
/// - 路径 A：AutoFightTask.KazuhaContinuousReturnLoopAsync（战斗中持续回点后台循环）
/// - 路径 B：PathExecutor 战后聚物回点分支
///
/// 决策判定复用既有纯函数 <see cref="KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy"/>
/// 与 <see cref="KazuhaCollectPositionGuardDecisions.ComputeSeedAnchor"/>（恒等返回战斗点，§3.4）。
/// 本 helper 因含 IO（截图 + 识别 + 重播种 + 延时）无法纯函数化，故用委托注入这些 IO，
/// 使其在 PBT 中可用 stub 替换、可测。
///
/// reseedAnchor 委托语义仅对应 Navigation.SetPrevPosition(战斗点)，绝不 Navigation.Reset()（§3.5）——
/// 该约束体现在调用方（路径 A/B）传入的委托实现。
/// </summary>
public static class KazuhaReturnReseedGuard
{
    /// <summary>
    /// 每次"重新播种 + 重识别"之间的间隔毫秒数。
    /// 让小地图重绘稳定 / 角色位置有机会变化，使重识别产生不同结果（同一帧重算无意义）。
    /// 用户指令定为 100ms：小地图帧间隔约 16~33ms，100ms 覆盖 3~6 帧足够重绘；
    /// 3 次重试总延时 ≤ 300ms，不显著拖慢回点。
    /// </summary>
    public const int ReseedReSampleDelayMs = 100;

    /// <summary>
    /// 派蒙检测（画面稳定门控）轮询间隔毫秒数。战斗结算画面 / 小地图重绘期间，
    /// 每 50ms 检测一次 Bv.IsInMainUi，命中主界面即结束等待。
    /// </summary>
    public const int ScreenStablePollIntervalMs = 50;

    /// <summary>
    /// 派蒙检测最大轮询次数。50ms × 10 = 500ms 总窗口上限；
    /// 耗尽仍未命中主界面则退化到现有重播种 + 重识别行为（不崩溃、不死循环）。
    /// </summary>
    public const int ScreenStableMaxPolls = 10;

    /// <summary>
    /// 核心编排：判定首次坐标是否可信；异常则重播种战斗点锚点 + 重识别，最多 maxRetry 次；
    /// 任一次落入阈值即返回可信坐标，全部仍异常则返回"放弃本轮移动"。
    /// </summary>
    /// <param name="initialPos">调用方已识别的首帧坐标（调用前已做 (0,0) 过滤）。</param>
    /// <param name="fightPointX">本段战斗点 X（种子锚点，小地图坐标系）。</param>
    /// <param name="fightPointY">本段战斗点 Y。</param>
    /// <param name="threshold">异常判定阈值（= AutoHoeingConfig.KazuhaReturnAbnormalCoordThreshold，默认 50）。</param>
    /// <param name="maxRetry">最大重试次数（= AutoHoeingConfig.KazuhaReturnReseedRetryCount，默认 3）。</param>
    /// <param name="zeroCoordStableRetry">(0,0) 识别失败时 GetPositionStable 全局匹配重试上限（= AutoHoeingConfig.KazuhaReturnZeroCoordStableRetryCount，默认 3）。</param>
    /// <param name="reseedAnchor">重播种委托：Navigation.SetPrevPosition(战斗点)。绝不 Reset。</param>
    /// <param name="reSample">重识别委托：CaptureToRectArea + Navigation.GetPosition，返回新坐标。</param>
    /// <param name="reSampleStable">(0,0) 失败时的全局匹配重识别委托：CaptureToRectArea + Navigation.GetPositionStable。不重播种（Q4）。</param>
    /// <param name="delay">延时委托：Task.Delay(ReseedReSampleDelayMs, ct)。</param>
    /// <param name="isScreenStable">画面稳定门控：返回当前帧是否在稳定主界面，由调用方 CaptureToRectArea + Bv.IsInMainUi 实现。</param>
    /// <param name="screenStablePollDelay">派蒙检测轮询延时：Task.Delay(ScreenStablePollIntervalMs, ct)。</param>
    /// <param name="log">日志委托。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<ReseedGuardResult> EvaluateAndReseedAsync(
        Point2f initialPos,
        double fightPointX, double fightPointY,
        double threshold,
        int maxRetry,
        int zeroCoordStableRetry,
        Action reseedAnchor,
        Func<Point2f> reSample,
        Func<Point2f> reSampleStable,
        Func<CancellationToken, Task> delay,
        Func<bool> isScreenStable,                        // 新增：当前帧是否在稳定主界面
        Func<CancellationToken, Task> screenStablePollDelay, // 新增：50ms 轮询延时
        Action<string> log,
        CancellationToken ct)
    {
        var (seedX, seedY) = KazuhaCollectPositionGuardDecisions.ComputeSeedAnchor(fightPointX, fightPointY);

        // 首次：用调用方已识别的 initialPos 判定。
        // 正常坐标（距战斗点 ≤ 阈值）第一次即采纳，零重播种 / 零重识别 / 零延时（Preservation）。
        if (KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy(initialPos, seedX, seedY, threshold))
        {
            return ReseedGuardResult.Move(initialPos, 0);
        }

        // 命中异常：进入"重播种 + 重识别"重试循环。
        var pos = initialPos;
        for (int attempt = 1; attempt <= maxRetry; attempt++)
        {
            reseedAnchor();              // SetPrevPosition(战斗点)：把漂移的陈旧锚点拨回战斗点
            await delay(ct);             // 极短延时让小地图重绘稳定 / 角色位置变化

            // 画面稳定门控（本次修复）：重识别前先派蒙检测等画面稳定。
            // 间隔 50ms 轮询，最多 ScreenStableMaxPolls(=10) 次 ≈ 500ms；
            // 命中主界面立即结束等待；耗尽仍未稳定则退化，继续执行下方 reSample（维持现状）。
            for (int poll = 0; poll < ScreenStableMaxPolls; poll++)
            {
                if (isScreenStable())
                {
                    if (poll > 0) log($"[重识别] 第{attempt}次重识别前派蒙检测：画面已稳定（轮询{poll}次）");
                    break;
                }
                await screenStablePollDelay(ct);
            }

            pos = reSample();            // 重新截图 + GetPosition

            // 重识别失败（(0,0)）：改走 GetPositionStable 全局匹配内层重试（见下）。
            if (pos is { X: 0, Y: 0 })
            {
                // (0,0)：识别失败（图像层面匹配不上）。局部匹配 + 重播种无法恢复，
                // 改走更鲁棒的 GetPositionStable（全局匹配）内层重试。
                // 注意：内层不调用 reseedAnchor()——全局匹配不需要局部锚点，重播种反干扰（Q4）。
                log($"[重识别] 第{attempt}次识别失败(0,0)，改走 GetPositionStable 全局匹配重试");
                for (int s = 1; s <= zeroCoordStableRetry; s++)
                {
                    await delay(ct);                 // 复用现有 100ms 延时，让小地图重绘稳定（Q3）
                    var stablePos = reSampleStable();  // CaptureToRectArea + GetPositionStable（全局匹配）

                    if (stablePos is { X: 0, Y: 0 })
                    {
                        log($"[重识别] GetPositionStable 第{s}次仍识别失败(0,0)，继续重试");
                        continue;
                    }

                    if (KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy(stablePos, seedX, seedY, threshold))
                    {
                        log($"[重识别] GetPositionStable 第{s}次恢复并落入阈值，采纳坐标");
                        return ReseedGuardResult.Move(stablePos, attempt);
                    }

                    // 全局匹配恢复出"非 (0,0) 但 > 阈值"的漂移远点：脱离 (0,0) 失败语义，
                    // 把它当作本轮外层 attempt 的结果，跳出内层、回到外层继续漂移远点重试逻辑。
                    log($"[重识别] GetPositionStable 第{s}次恢复但仍超阈值，转交外层漂移重试");
                    pos = stablePos;
                    break;
                }

                // 内层 stable 重试若全部 (0,0)（pos 仍为 (0,0)）→ 小地图持续识别不出，
                // 全局匹配也救不回 → 直接放弃本轮移动。
                if (pos is { X: 0, Y: 0 })
                {
                    log($"[重识别] GetPositionStable 连续{zeroCoordStableRetry}次仍识别失败(0,0)，放弃本轮移动");
                    return ReseedGuardResult.Abandon(attempt);
                }

                // pos 已被内层置为漂移远点：回到外层循环顶继续 reseedAnchor + reSample 重试。
                continue;
            }

            if (KazuhaCollectPositionGuardDecisions.IsRecognizedPositionTrustworthy(pos, seedX, seedY, threshold))
            {
                log($"[重识别] 第{attempt}次重识别落入阈值，采纳坐标");
                return ReseedGuardResult.Move(pos, attempt);
            }
        }

        // N 次重播种 + 重识别仍异常 → 放弃本轮移动（绝不以异常坐标 MoveTo / MoveCloseTo）。
        log($"[重识别] 连续{maxRetry}次重播种+重识别仍异常，放弃本轮移动");
        return ReseedGuardResult.Abandon(maxRetry);
    }
}

/// <summary>
/// helper 返回：要么"采纳一个可信坐标继续移动"，要么"放弃本轮移动"。
/// </summary>
public readonly struct ReseedGuardResult
{
    /// <summary>true = 拿到可信坐标可继续移动；false = N 次重试仍异常，放弃本轮移动。</summary>
    public bool ShouldMove { get; }

    /// <summary>ShouldMove = true 时的可信坐标（已落入阈值内）。</summary>
    public Point2f TrustedPos { get; }

    /// <summary>实际重识别重试次数（含触发的重播种次数），用于日志 / PBT 断言 ≤ N。</summary>
    public int RetryUsed { get; }

    private ReseedGuardResult(bool shouldMove, Point2f pos, int retryUsed)
    {
        ShouldMove = shouldMove;
        TrustedPos = pos;
        RetryUsed = retryUsed;
    }

    /// <summary>采纳可信坐标，继续移动。</summary>
    public static ReseedGuardResult Move(Point2f pos, int retryUsed) => new(true, pos, retryUsed);

    /// <summary>放弃本轮移动。</summary>
    public static ReseedGuardResult Abandon(int retryUsed) => new(false, default, retryUsed);
}
