using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 万叶聚物动作的阶段标记，由 <see cref="KazuhaCollectExecutor.RunAsync"/> 通过 onProgress 回调
/// 透出，供调用方做日志/事件穿透。
/// </summary>
public enum KazuhaCollectStage
{
    Started,
    Switched,
    SkillCdReady,           // 自然就绪
    SkillCdTimeoutForce,    // 超时直放（区别于自然就绪）
    SkillReleaseConfirmed,
    PlungeAttackDone,
    PickupWaitDone,
    SkillReleaseRetried,    // EB2 重试块开始（双判失败 → 重试一次）
}

/// <summary>
/// 万叶聚物动作执行器（共享 helper）。
/// 不引用 SignalR、不依赖 MultiplayerCoordinator，纯执行 + 视觉验证。
/// 单机版（AutoLeyLineOutcropTask）与联机版（KazuhaCollectSyncCoordinator）共用。
/// </summary>
public static class KazuhaCollectExecutor
{
    /// <summary>
    /// 聚物动作的最终结果。
    /// SkipReason 取值：team_no_kazuha / switch_failed / e_skill_not_released / null（成功时）。
    /// </summary>
    public sealed record Outcome(bool Success, string? SkipReason);

    /// <summary>
    /// 执行万叶聚物动作序列：选万叶 → 切人 → 等 E 技 CD（带上限超时不算失败）→ 长 E
    /// → OCR + Bv.IsSkillReady 双重确认 → 6 次下落攻击 → 拾取等待。
    /// </summary>
    /// <param name="combatScenes">已识别好的队伍场景</param>
    /// <param name="waitSkillCdSeconds">等 E 技 CD 上限秒数（超时按 SkillCdTimeoutForce 直放，由 OCR + 视觉双判决定成败）</param>
    /// <param name="postPickupWaitMs">下落攻击完成后的拾取等待毫秒（默认 3000ms）</param>
    /// <param name="onProgress">可选阶段回调，供联机版穿透阶段日志/事件</param>
    /// <param name="assumeAlreadySwitched">
    /// 联机快速路径开关：true 时跳过开头 SelectAvatar + Delay(200) + TrySwitch（已由 BeginPreparationAsync 完成预切人）。
    /// 必须同时传 <paramref name="preselectedKazuha"/> 非 null，否则 ArgumentException。默认 false 保持单机原行为。
    /// </param>
    /// <param name="preselectedKazuha">
    /// 预切好的万叶 Avatar 引用，配合 <paramref name="assumeAlreadySwitched"/>=true 使用，用于 WaitSkillCd / AfterUseSkill / Bv.IsSkillReady。
    /// </param>
    /// <param name="ct">取消令牌；触发取消时 finally 仍会释放按键，并向上抛 OperationCanceledException</param>
    public static async Task<Outcome> RunAsync(
        CombatScenes combatScenes,
        int waitSkillCdSeconds,
        int postPickupWaitMs = 3000,
        Action<KazuhaCollectStage>? onProgress = null,
        bool assumeAlreadySwitched = false,
        Avatar? preselectedKazuha = null,
        CancellationToken ct = default)
    {
        ValidateFastPathArgs(assumeAlreadySwitched, preselectedKazuha);

        try
        {
            Avatar? kazuha;
            if (assumeAlreadySwitched)
            {
                // 快速路径：调用方（KazuhaCollectSyncCoordinator.BeginPreparationAsync）
                // 已完成 SelectAvatar + Delay(200) + TrySwitch；此处直接复用 kazuha 引用，
                // 跳过单机原开头切人序列（节省 ~200ms+ 切人 Delay）。
                kazuha = preselectedKazuha;
                onProgress?.Invoke(KazuhaCollectStage.Started);
                onProgress?.Invoke(KazuhaCollectStage.Switched);
            }
            else
            {
                // 原路径：单机 (AutoLeyLineOutcropTask) 调用方 / 联机兜底分支走这里
                kazuha = combatScenes.SelectAvatar("枫原万叶");
                if (kazuha == null)
                {
                    return new Outcome(false, "team_no_kazuha");
                }

                onProgress?.Invoke(KazuhaCollectStage.Started);

                await Delay(200, ct);

                if (!kazuha.TrySwitch(10))
                {
                    return new Outcome(false, "switch_failed");
                }

                onProgress?.Invoke(KazuhaCollectStage.Switched);
            }

            // 等 CD：超时不算失败（requirements 3.5），由后续 OCR + 视觉双判决定实际成败
            try
            {
                using var cdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cdCts.CancelAfter(TimeSpan.FromSeconds(waitSkillCdSeconds));
                await kazuha!.WaitSkillCd(cdCts.Token);
                onProgress?.Invoke(KazuhaCollectStage.SkillCdReady);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 仅当外层 ct 未触发时才视为本地超时直放；外层 ct 触发的取消应继续向上抛
                onProgress?.Invoke(KazuhaCollectStage.SkillCdTimeoutForce);
            }

            // EB1: 长 E 前视觉确认（参考 AutoFightTask.cs L1361 / kazuha-collect-pre-cast-visual-check-and-retry）。
            // retryCount=1：内部不重试只判一次；isResetCd=false：不动 ManualSkillCd 状态机（Q1 决议）；
            // needLog=true：日志含 CD 状态。
            // 返回 true → 视觉确认 E 已就绪，直接进入长按 E；
            // 返回 false → 视觉看到 CD 数字 → 再调一次 WaitSkillCd 等真 CD（仍受外层 ct 约束）。
            if (!await AutoFightSkill.AvatarSkillAsync(
                    Logger, kazuha!,
                    skills: false, retryCount: 1, ct: ct,
                    image: null, needLog: true, isResetCd: false))
            {
                try
                {
                    using var preCastCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    preCastCts.CancelAfter(TimeSpan.FromSeconds(waitSkillCdSeconds));
                    await kazuha!.WaitSkillCd(preCastCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 视觉确认后再次超时 → 走原 SkillCdTimeoutForce 兜底（不再重复发阶段日志，避免日志重复）
                }
            }

            await SimulateHoldElementalSkillAsync(1000, ct);
            await Delay(200, ct);

            // OCR + 视觉双重确认：长 E 是否真的释放
            bool firstReleaseFailed = false;
            using (var region = CaptureToRectArea())
            {
                using var eRa = region.DeriveCrop(AutoFightAssets.Instance.ECooldownRect);
                using var eRaWhite = OpenCvCommonHelper.InRangeHsv(
                    eRa.SrcMat,
                    new Scalar(0, 0, 235),
                    new Scalar(0, 25, 255));
                var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);

                var hasOcrCd = double.TryParse(text, out var ocrCd) && ocrCd > 0;
                var isVisualReady = Bv.IsSkillReady(region, kazuha!.Index, false);

                if (ShouldRetryRelease(hasOcrCd, isVisualReady))
                {
                    firstReleaseFailed = true;
                }
                else
                {
                    kazuha.AfterUseSkill(region);
                }
            }

            if (firstReleaseFailed)
            {
                // EB2: 长 E 释放失败 → 重试一次（参考 AutoFightTask.cs L1380-1395，Q2 决议）。
                Logger.LogWarning("万叶长E技能未成功释放，尝试再次释放");
                onProgress?.Invoke(KazuhaCollectStage.SkillReleaseRetried);

                kazuha!.TrySwitch(20);
                kazuha.UseSkill(true);
                await Delay(50, ct);
                await SimulateHoldElementalSkillAsync(800, ct);
                await Delay(200, ct);

                using var region2 = CaptureToRectArea();
                using var eRa2 = region2.DeriveCrop(AutoFightAssets.Instance.ECooldownRect);
                using var eRaWhite2 = OpenCvCommonHelper.InRangeHsv(
                    eRa2.SrcMat,
                    new Scalar(0, 0, 235),
                    new Scalar(0, 25, 255));
                var text2 = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite2);

                var hasOcrCd2 = double.TryParse(text2, out var ocrCd2) && ocrCd2 > 0;
                var isVisualReady2 = Bv.IsSkillReady(region2, kazuha.Index, false);

                if (ShouldRetryRelease(hasOcrCd2, isVisualReady2))
                {
                    // 重试仍失败 → 保持原 reason 码语义不变
                    return new Outcome(false, "e_skill_not_released");
                }

                kazuha.AfterUseSkill(region2);
            }

            onProgress?.Invoke(KazuhaCollectStage.SkillReleaseConfirmed);

            await SimulateMouseLeftClickLoopAsync(6, ct);
            await Delay(1500, ct);
            onProgress?.Invoke(KazuhaCollectStage.PlungeAttackDone);

            await Delay(postPickupWaitMs, ct);
            onProgress?.Invoke(KazuhaCollectStage.PickupWaitDone);

            return new Outcome(true, null);
        }
        finally
        {
            // BC4 / requirements 3.10：所有路径（含取消、异常、提前 return）都要释放按键
            try
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
            }
            catch
            {
                // finally 内吞掉，避免遮蔽原始异常
            }

            try
            {
                Simulation.ReleaseAllKey();
            }
            catch
            {
                // 同上
            }
        }
    }

    /// <summary>
    /// 联机快速路径参数校验：assumeAlreadySwitched=true 时必须同时传 preselectedKazuha 非 null。
    /// 抽出 public 方法是为了 PBT-4 跨项目直接覆盖参数组合，无需构造完整 CombatScenes / Avatar。
    /// 详见 design.md §6 PBT-4。
    /// </summary>
    public static void ValidateFastPathArgs(bool assumeAlreadySwitched, Avatar? preselectedKazuha)
    {
        if (assumeAlreadySwitched && preselectedKazuha == null)
        {
            throw new ArgumentException(
                "assumeAlreadySwitched=true 时必须传 preselectedKazuha（联机快速路径需要 kazuha 引用用于 WaitSkillCd / AfterUseSkill / Bv.IsSkillReady）",
                nameof(preselectedKazuha));
        }
    }

    /// <summary>
    /// OCR + 视觉双判后是否需要重试：仅当"OCR 没读到 CD 数字 ∧ 视觉显示就绪"时为 true
    /// （即"E 没被消耗 = 长 E 没释放成功"）。其他三种组合视为长 E 已释放（含视觉读不到的兜底情况）。
    /// 抽 public static 纯函数是为了 PBT 可直接覆盖 4 种组合（design.md §6 PBT-1）。
    ///
    /// 真值表：
    ///   hasOcrCd=true  isVisualReady=true   → false（OCR 读到 CD + 视觉就绪：异常状态保守视为已释放）
    ///   hasOcrCd=true  isVisualReady=false  → false（OCR 读到 CD + 视觉冷却：一致就绪→冷却已释放）
    ///   hasOcrCd=false isVisualReady=true   → true （OCR 无 CD + 视觉就绪：技能没被消耗 → 重试）
    ///   hasOcrCd=false isVisualReady=false  → false（OCR 无 CD + 视觉冷却：视觉异常但已用，保守视为已释放）
    /// </summary>
    public static bool ShouldRetryRelease(bool hasOcrCd, bool isVisualReady) => !hasOcrCd && isVisualReady;
}
