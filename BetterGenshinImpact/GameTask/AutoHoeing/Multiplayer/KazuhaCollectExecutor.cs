using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
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
    /// 执行万叶聚物动作序列：选万叶 → 切人 → 等 E 技 CD（带上限超时不算失败）→ 长 E（UseSkill）
    /// → AvatarSkillAsync 释放确认（未放出走重试三件套）→ 6 次下落攻击 → 拾取等待。
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
    /// 预切好的万叶 Avatar 引用，配合 <paramref name="assumeAlreadySwitched"/>=true 使用，用于 WaitSkillCd / AfterUseSkill。
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
            await Delay(100, ct);
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

            // [诊断] 判 CD 前记录万叶是否真正出战，排查"切人未完成导致连通块检测假阴性"
            using (var raDiag = CaptureToRectArea())
            {
                Logger.LogInformation("[聚物][诊断] 判CD前: 万叶出战 IsActive={IsActive}, Index={Index}",
                    kazuha!.IsActive(raDiag), kazuha.Index);
            }

            var cdJudgedInCd = await AutoFightSkill.AvatarSkillAsync(
                Logger, kazuha!,
                skills: false, retryCount: 1, ct: ct,
                image: null, needLog: true, isResetCd: false);
            Logger.LogInformation("[聚物][诊断] 判CD结果: connectivityInCd={InCd} → 分支={Branch}",
                cdJudgedInCd, cdJudgedInCd ? "在CD→WaitSkillCd等待" : "就绪→立即放行");

            if (cdJudgedInCd)
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

            // 放长 E：对齐 AutoFightTask.cs L1819-1821 万叶拾取样板，用内建 UseSkill(true) +
            // NormalAttack 触发下落，复用 UseSkill 内建的"放完读 CD 确认 + 自动重试"闭环。
            kazuha!.UseSkill(true);
            await Delay(50, ct);
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);

            // 释放确认①：对齐原版单判（AutoFightTask.cs L1823）——
            // AvatarSkillAsync 返回 false = E 不在 CD = 没放出 → 重试三件套。
            var releasedInCd = await AutoFightSkill.AvatarSkillAsync(
                Logger, kazuha!, skills: false, retryCount: 1, ct: ct,
                image: null, needLog: true, isResetCd: false);
            Logger.LogInformation("[聚物][诊断] 释放确认①: releasedInCd={InCd} → {Branch}",
                releasedInCd, releasedInCd ? "已放出(E在CD)" : "未放出→重试三件套");

            if (!releasedInCd)
            {
                // 对齐 AutoFightTask.cs L1824-1834：UseSkill + Hold(800) + 6 连点下落攻击。
                Logger.LogWarning("万叶长E技能未成功释放，尝试再次释放");
                onProgress?.Invoke(KazuhaCollectStage.SkillReleaseRetried);

                kazuha.TrySwitch(20);
                await Delay(50, ct);
                kazuha.UseSkill(true);
                await Delay(50, ct);
                await SimulateHoldElementalSkillAsync(800, ct);
                await SimulateMouseLeftClickLoopAsync(6, ct);

                // 释放确认②（重试后）：联机降级上报路径，原样保留——
                // 重试仍未放出（单判 AvatarSkillAsync 返回 false）→ return Outcome 联机广播 Skipped。
                // 注意：此二次确认 + 失败上报是联机协同能力（原版自动战斗没有），逐字节保留，
                //       仅把判据从自拼 ShouldRetryRelease 双判改回原版单判。
                var releasedInCd2 = await AutoFightSkill.AvatarSkillAsync(
                    Logger, kazuha, skills: false, retryCount: 1, ct: ct,
                    image: null, needLog: true, isResetCd: false);
                Logger.LogInformation("[聚物][诊断] 释放确认②(重试后): releasedInCd2={InCd} → {Branch}",
                    releasedInCd2, releasedInCd2 ? "已放出(E在CD)" : "仍未放出→Outcome(false, e_skill_not_released)");

                if (!releasedInCd2)
                {
                    // 重试仍失败 → 保持原 reason 码语义不变（联机广播 Skipped）
                    return new Outcome(false, "e_skill_not_released");
                }
            }

            // 标记 E 技 CD（对齐原版 L1846 picker.AfterUseSkill()）。
            kazuha.AfterUseSkill();

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
                "assumeAlreadySwitched=true 时必须传 preselectedKazuha（联机快速路径需要 kazuha 引用用于 WaitSkillCd / AfterUseSkill）",
                nameof(preselectedKazuha));
        }
    }
}
