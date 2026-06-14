using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
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
        Func<CancellationToken, Task>? onBeforeHoldE = null,
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

            // 判 CD 前临场稳定（对齐 AutoFightTask.cs L1820-1827）：
            // 切人 + 等画面稳，避免切人/落地动画期间截帧导致 E 图标连通块偏少被误判"就绪"。
            kazuha!.TrySwitch(20);
            await Delay(50, ct);
            using (var raActive = CaptureToRectArea())
            {
                if (!kazuha.IsActive(raActive))
                {
                    // TrySwitch 失败兜底：万叶未真正出战时再切一次，避免被误判为就绪
                    kazuha.TrySwitch(20);
                    await Delay(50, ct);
                }
            }

            // 视觉先判 → CD 中走 WaitSkillCd(ct) 无上限（对齐 AutoFightTask.cs L1822 万叶长 E 拾取样板）。
            // 视觉就绪（AvatarSkillAsync 返回 false）→ 立即触发 SkillCdReady，跳过等待；
            // 视觉看到 CD（AvatarSkillAsync 返回 true）→ 等到 E 的 CD 真正结束再放。
            // retryCount=1：内部不重试只判一次；isResetCd=false：不动 ManualSkillCd 状态机；
            // needLog=true：日志含 CD 状态。
            if (await AutoFightSkill.AvatarSkillAsync(
                    Logger, kazuha!,
                    skills: false, retryCount: 1, ct: ct,
                    image: null, needLog: true, isResetCd: false))
            {
                // 对齐自动战斗：WaitSkillCd(ct) 无本地超时上限，等到 E 的 CD 真正结束再放。
                // 外层 ct 取消时 WaitSkillCd 自然抛 OperationCanceledException 向上传播
                // （由 finally 释放按键，符合 bugfix 3.8）。
                await kazuha!.WaitSkillCd(ct);
                onProgress?.Invoke(KazuhaCollectStage.SkillCdReady);
            }
            else
            {
                // 视觉就绪 → 立即放行，与原"自然就绪"路径阶段语义保持一致
                onProgress?.Invoke(KazuhaCollectStage.SkillCdReady);
            }

            // multiplayer-kazuha-collect-point-broadcast: HoldE 起手前注入 hook，
            // 联机分支用此 hook 算 (collectX, collectY) 并 fire-and-forget 上报；
            // 单机调用方不传即跳过，行为零差异（任何异常透传 OperationCanceledException
            // 否则吞掉，不让 hook 抛出打断 HoldE 序列）。
            if (onBeforeHoldE != null)
            {
                try { await onBeforeHoldE(ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "[联机][聚物] onBeforeHoldE hook 抛异常，已忽略并继续 HoldE");
                }
            }

            await SimulateHoldElementalSkillAsync(1000, ct);
            await Delay(200, ct);

            // 色块连通性检测 + 视觉双重确认：长 E 是否真的释放
            // 主判据 connectivityInCd：AvatarSkillAsync 连通性检测（与 CD 等待阶段、原版万叶拾取样板
            //   AutoFightTask.cs L1829 同一套），返回 true = E 图标区有纯白连通块（倒计时数字）= E 在 CD = 已释放。
            //   替代原 OCR 识别倒计时数字（OcrFactory.Paddle.OcrWithoutDetector），后者读纯白小数字不稳、偶发误读。
            // 二次确认 isVisualReady：Bv.IsSkillReady，仅当连通性判"不在 CD"时配合判定，避免连通性假阴性引发不必要重试。
            // ShouldRetryRelease(connectivityInCd, isVisualReady) 真值表不变（!arg1 && arg2）：
            //   仅当"连通性判未释放 AND 视觉也判就绪"才重试。
            bool firstReleaseFailed = false;
            var connectivityInCd = await AutoFightSkill.AvatarSkillAsync(
                Logger, kazuha!, skills: false, retryCount: 1, ct: ct,
                image: null, needLog: true, isResetCd: false);
            using (var region = CaptureToRectArea())
            {
                var isVisualReady = Bv.IsSkillReady(region, kazuha!.Index, false);

                if (ShouldRetryRelease(connectivityInCd, isVisualReady))
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

                var connectivityInCd2 = await AutoFightSkill.AvatarSkillAsync(
                    Logger, kazuha, skills: false, retryCount: 1, ct: ct,
                    image: null, needLog: true, isResetCd: false);
                using var region2 = CaptureToRectArea();
                var isVisualReady2 = Bv.IsSkillReady(region2, kazuha.Index, false);

                if (ShouldRetryRelease(connectivityInCd2, isVisualReady2))
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
