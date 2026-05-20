#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 战后回点 → 万叶聚物 → 一起放行 的同步协调器。
/// 由 PathExecutor 在战后回点 Delay 处调用 <see cref="WaitAtFightPointAsync"/>。
///
/// 设计要点（详见 design.md "Components and Interfaces §1"）：
/// - 万叶玩家分支：等所有 Peer 到齐 → 广播 Started → 执行 KazuhaCollectExecutor → 按 Outcome 广播 Finished/Skipped。
/// - 普通玩家分支：subscribe-before-action 订阅 Finished/Skipped → 上报 AtFightPoint → 等待终态事件
///   → 任意终态后统一停 KazuhaSyncWaitSeconds 秒后离开。
/// - 启用门控：<see cref="KazuhaCollectSyncDecisions.IsEnabled"/> 任一项不满足直接 fallback 为原 Delay。
/// - 万叶身份（kazuha-player-auto-detection）：本地缓存 <c>_kazuhaPlayerUid</c>，由服务端广播 KazuhaPlayerUpdated(playerUid) 维护；
///   <c>IsCurrentPlayerKazuha</c> 直接对比 <c>_kazuhaPlayerUid == _client.PlayerUid</c>。替换原 ResolveKazuhaUid + PlayerList 索引方案。
/// - 取消：OperationCanceledException 透传，不广播 Finished/Skipped（requirements 5.6）。
/// </summary>
public sealed class KazuhaCollectSyncCoordinator : IDisposable
{
    private readonly CoordinatorClient _client;
    private readonly AutoHoeingConfig _config;
    private readonly MultiplayerCoordinator _parent;
    private readonly ILogger<KazuhaCollectSyncCoordinator> _logger = App.GetLogger<KazuhaCollectSyncCoordinator>();

    // kazuha-player-auto-detection: 本地缓存最新广播的 KazuhaPlayerUid。
    // 由 _client.KazuhaPlayerUpdated 事件维护；getter 通过 _kazuhaPlayerUid == _client.PlayerUid 判定本玩家是否为 Kazuha。
    private string? _kazuhaPlayerUid;
    private readonly Action<string> _onKazuhaPlayerUpdated;

    public KazuhaCollectSyncCoordinator(
        CoordinatorClient client,
        AutoHoeingConfig config,
        MultiplayerCoordinator parent)
    {
        _client = client;
        _config = config;
        _parent = parent;
        CurrentState = KazuhaCollectState.Idle;

        // 订阅 KazuhaPlayerUpdated 事件维护 _kazuhaPlayerUid
        _onKazuhaPlayerUpdated = playerUid =>
        {
            _kazuhaPlayerUid = string.IsNullOrEmpty(playerUid) ? null : playerUid;
        };
        _client.KazuhaPlayerUpdated += _onKazuhaPlayerUpdated;
    }

    /// <summary>
    /// 当前玩家是否被指定为万叶玩家。
    /// kazuha-player-auto-detection: 改为读取本地缓存的 <c>_kazuhaPlayerUid</c>（由服务端广播 KazuhaPlayerUpdated 维护），
    /// 替代原 <c>ResolveKazuhaUid + PlayerList</c> 索引方案。
    /// 满足 Property 4 (Uniqueness Across Clients)：所有客户端依据同一服务端广播判定，最多一个客户端 IsCurrentPlayerKazuha == true。
    /// </summary>
    public bool IsCurrentPlayerKazuha
    {
        get
        {
            var uid = _kazuhaPlayerUid;
            if (string.IsNullOrEmpty(uid)) return false;
            return uid == _client.PlayerUid;
        }
    }

    /// <summary>本周期是否启用同步流程（综合 EnableKazuhaSync / 连接状态）。</summary>
    public bool IsEnabled => KazuhaCollectSyncDecisions.IsEnabled(_config, _client.IsConnected);

    /// <summary>
    /// 用户是否在配置中启用了万叶聚物（不感知连接状态）。
    /// 由 PathExecutor 在战后回点判定是否走"回战斗点 → 进入 WaitAtFightPointAsync"分支；
    /// IsConnected==false 时仍进 WaitAtFightPointAsync，由其内部走兜底 Delay。
    /// 必须读此属性（持有 AutoHoeingTask 拷贝/覆盖后的 _config），
    /// 不能读 TaskContext.Instance().Config.AutoHoeingConfig（配置组覆盖未应用到全局）。
    /// </summary>
    public bool IsConfigEnabled => KazuhaCollectSyncDecisions.IsConfigEnabledForPathExecutor(_config);

    /// <summary>当前周期状态（用于日志/PBT 模型）。</summary>
    public KazuhaCollectState CurrentState { get; private set; }

    /// <summary>
    /// BeginPreparationAsync 的并行预备结果。
    /// SwitchedSuccessfully == true 时 CombatScenes / Kazuha 必非 null，可走 KazuhaCollectExecutor 快速路径；
    /// 否则走原"RunAsKazuhaAsync 内串行 GetCombatScenes + RunAsync 自切人"兜底。
    /// </summary>
    public sealed record PreparationResult(
        CombatScenes? CombatScenes,
        Avatar? Kazuha,
        bool SwitchedSuccessfully)
    {
        public static PreparationResult Skipped { get; } = new(null, null, false);
    }

    /// <summary>
    /// 战后回点 → MoveCloseTo 之前由 PathExecutor 后台 kick-off 的预备任务。
    /// 与 MoveCloseTo（走回战斗点）并行执行：缓存命中时调用 GetCombatScenes → SelectAvatar → Delay(200) → TrySwitch。
    /// 任何"不应预备"的场景（未启用 / 非万叶玩家 / 缓存未命中 / 切人失败 / 异常）一律返回 PreparationResult.Skipped，
    /// 让主流程 RunAsKazuhaAsync 走原兜底路径，绝不抛异常给 PathExecutor（OperationCanceledException 透传除外）。
    /// 关键约束：缓存未命中时不调用 GetCombatScenes，避免走 ReturnMainUiTask 分支按 ESC 中断 MoveCloseTo 走位。
    /// 详见 design.md §3.3。
    /// </summary>
    public Task<PreparationResult> BeginPreparationAsync(CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                if (!KazuhaCollectSyncDecisions.ShouldRunBackgroundPreparation(
                        IsEnabled, IsCurrentPlayerKazuha, RunnerContext.Instance.HasCombatScenesCached))
                {
                    return PreparationResult.Skipped;
                }

                var combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
                if (combatScenes == null) return PreparationResult.Skipped;

                var kazuha = combatScenes.SelectAvatar("枫原万叶");
                if (kazuha == null) return PreparationResult.Skipped;

                await Task.Delay(200, ct);

                if (!kazuha.TrySwitch(10))
                {
                    _logger.LogDebug("[联机][聚物] BeginPreparationAsync 预切人失败，主流程兜底");
                    return PreparationResult.Skipped;
                }

                _logger.LogDebug("[联机][聚物] BeginPreparationAsync 完成预切人，已就绪");
                return new PreparationResult(combatScenes, kazuha, true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机][聚物] BeginPreparationAsync 失败，主流程兜底");
                return PreparationResult.Skipped;
            }
        }, ct);
    }

    /// <summary>
    /// 战后到达战斗点后的同步等待入口。返回时已离开战斗点的"等待"阶段。
    /// 未启用门控时（IsConnected==false）直接 fallback 到 <c>Delay(KazuhaSyncWaitSeconds*1000)</c> 行为。
    /// <paramref name="prepTask"/> 为 PathExecutor 在 MoveCloseTo 之前 kick off 的 BeginPreparationAsync 任务，
    /// null 时（PathExecutor 未传 / 兜底）走原"RunAsKazuhaAsync 内串行 GetCombatScenes + RunAsync 自切人"路径。
    /// </summary>
    public async Task WaitAtFightPointAsync(Waypoint fightPointWaypoint, Task<PreparationResult>? prepTask, CancellationToken ct)
    {
        ResetForNextCycle();
        CurrentState = KazuhaCollectState.AtFightPoint;

        // 启用门控：!IsEnabled（即 IsConnected==false，因为 EnableKazuhaSync==false 时 PathExecutor 已经不调用本方法）
        // → 用 KazuhaSyncWaitSeconds 作为统一兜底等待（Open Q5: 保留兜底 Delay，时长由 KazuhaSyncWaitSeconds 接管）
        if (!IsEnabled)
        {
            _logger.LogDebug("[联机][聚物] 未启用同步流程（IsConnected==false），按 KazuhaSyncWaitSeconds 兜底等待 {Sec}s",
                _config.KazuhaSyncWaitSeconds);
            try
            {
                await Task.Delay(_config.KazuhaSyncWaitSeconds * 1000, ct);
            }
            finally
            {
                CurrentState = KazuhaCollectState.Skipped;
            }
            return;
        }

        // syncKey: 用当前路线索引 + waypoint 哈希唯一标识本段战斗点（房主与成员都本地构造，相同输入产生相同 key）
        var syncKey = $"{_client.CurrentRouteIndex}:{fightPointWaypoint?.GetHashCode() ?? 0}";

        // kazuha-player-auto-detection: 删除原"房主侧 kazuha_offline 预检"块。
        // 新机制下房主无法预知谁是 Kazuha（要等客户端声明）；若全员都没声明则 _kazuhaPlayerUid == null
        // 自然走 fallback：IsCurrentPlayerKazuha 返回 false，进入 WaitAsNonKazuhaAsync 等待终态超时后离开。

        try
        {
            if (IsCurrentPlayerKazuha)
            {
                await RunAsKazuhaAsync(syncKey, prepTask, ct);
            }
            else
            {
                // 非万叶玩家分支不消费 prepTask；BeginPreparationAsync 内部第一行 gate 已对
                // !IsCurrentPlayerKazuha 立即返回 PreparationResult.Skipped，无副作用。
                await WaitAsNonKazuhaAsync(syncKey, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消透传，不广播 Finished/Skipped（requirements 5.6）
            throw;
        }
    }

    /// <summary>
    /// 万叶玩家分支：到点立刻执行聚物（不等其他成员）→ 按 Outcome 广播 Finished/Skipped。
    /// 设计变更：
    /// - 删除"先等所有 Peer 到齐"等待，万叶到达战斗点后立即放 E，缩短整组锄地时长。
    /// - BC2: NotifyKazuhaArrivedAtFightPoint / NotifyKazuhaCollectStarted / Finished / Skipped 全部 fire-and-forget，
    ///   主流程不等待 SignalR Round Trip；CoordinatorClient 内部已有 try/catch + LogWarning 静默吞，
    ///   外层 FireAndForget(ContinueWith) 兜底未观察异常。
    /// - BC3+BC4: 消费 prepTask（已与 MoveCloseTo 并行执行）：成功时走 KazuhaCollectExecutor 快速路径
    ///   (assumeAlreadySwitched=true + preselectedKazuha)；任意失败/兜底回退原"串行 GetCombatScenes + 自切人"路径。
    /// 非万叶玩家分支仍然先订阅终态事件再到点，因此万叶在他们订阅后才广播也安全。
    /// </summary>
    private async Task RunAsKazuhaAsync(string syncKey, Task<PreparationResult>? prepTask, CancellationToken ct)
    {
        var maskedUid = AutoPartyTask.MaskUid(_client.PlayerUid);
        _logger.LogInformation("[联机][聚物] 万叶玩家 {Uid} 到达战斗点，立即执行聚物 (syncKey={Key})", maskedUid, syncKey);

        // BC2: 上报到点 + 广播 Started 改 fire-and-forget；schedule 顺序仍是 Arrived → Started，
        // 不引入新事件丢失窗口（非万叶玩家"先订阅 Finished/Skipped 再上报到点"时序保持不变）。
        FireAndForget(_client.NotifyKazuhaArrivedAtFightPointAsync(syncKey, ct), "NotifyKazuhaArrivedAtFightPoint");
        CurrentState = KazuhaCollectState.Collecting;
        FireAndForget(_client.NotifyKazuhaCollectStartedAsync(), "NotifyKazuhaCollectStarted");

        // BC3+BC4: 消费 prepTask。OperationCanceledException 透传；其它异常 LogWarning + Skipped 兜底。
        PreparationResult prep;
        if (prepTask != null)
        {
            try
            {
                prep = await prepTask;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机][聚物] 万叶玩家：消费 prepTask 失败，转兜底");
                prep = PreparationResult.Skipped;
            }
        }
        else
        {
            prep = PreparationResult.Skipped;
        }

        CombatScenes? combatScenes;
        Avatar? preKazuha = null;
        bool useFastPath = false;

        if (prep.SwitchedSuccessfully && prep.CombatScenes != null && prep.Kazuha != null)
        {
            combatScenes = prep.CombatScenes;
            preKazuha = prep.Kazuha;
            useFastPath = true;
        }
        else
        {
            // 兜底：缓存未命中 / 预切人失败 / 异常 → 走原"串行 GetCombatScenes + RunAsync 自切人"路径
            combatScenes = null;
            try
            {
                combatScenes = await RunnerContext.Instance.GetCombatScenes(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机][聚物] 万叶玩家：兜底获取 CombatScenes 失败");
            }
        }

        if (combatScenes == null)
        {
            _logger.LogWarning("[联机][聚物] 万叶玩家：CombatScenes 不可用，广播 team_no_kazuha");
            FireAndForget(_client.NotifyKazuhaCollectSkippedAsync("team_no_kazuha"), "NotifyKazuhaCollectSkipped(team_no_kazuha)");
            CurrentState = KazuhaCollectState.Skipped;
            return;
        }

        KazuhaCollectExecutor.Outcome outcome;
        try
        {
            outcome = await KazuhaCollectExecutor.RunAsync(
                combatScenes,
                waitSkillCdSeconds: _config.KazuhaWaitSkillCdSeconds,
                // 联机版万叶玩家"下落攻击完成后的拾取等待"复用 KazuhaSyncWaitSeconds（与非万叶玩家停留时长一致），
                // 让万叶/非万叶玩家完成后近乎同步离开战斗点，避免"万叶比别人晚 N 秒离开"的奇怪节奏。
                postPickupWaitMs: Math.Max(0, _config.KazuhaSyncWaitSeconds) * 1000,
                onProgress: stage => _logger.LogInformation("[联机][聚物] 万叶阶段: {Stage}", stage),
                assumeAlreadySwitched: useFastPath,
                preselectedKazuha: preKazuha,
                ct: ct);
        }
        catch (RetryException)
        {
            _logger.LogWarning("[联机][聚物] 万叶玩家在聚物中触发 RetryException，广播 kazuha_anomaly 并向上抛");
            FireAndForget(_client.NotifyKazuhaCollectSkippedAsync("kazuha_anomaly"), "NotifyKazuhaCollectSkipped(kazuha_anomaly)");
            CurrentState = KazuhaCollectState.Skipped;
            throw;
        }

        if (outcome.Success)
        {
            FireAndForget(_client.NotifyKazuhaCollectFinishedAsync(true), "NotifyKazuhaCollectFinished(true)");
            _logger.LogInformation("[联机][聚物] 万叶玩家聚物完成，已广播 Finished");
            CurrentState = KazuhaCollectState.Finished;
        }
        else
        {
            var reason = outcome.SkipReason ?? "unknown";
            FireAndForget(_client.NotifyKazuhaCollectSkippedAsync(reason), $"NotifyKazuhaCollectSkipped({reason})");
            _logger.LogWarning("[联机][聚物] 万叶玩家聚物降级，原因={Reason}", reason);
            CurrentState = KazuhaCollectState.Skipped;
        }
    }

    /// <summary>
    /// 普通玩家分支：subscribe-before-action 订阅 Finished/Skipped → 上报 AtFightPoint
    /// → 等待终态事件 → 任意终态后统一停 KazuhaSyncWaitSeconds 秒后离开（不再做 elapsedMs 补足计算）。
    /// </summary>
    private async Task WaitAsNonKazuhaAsync(string syncKey, CancellationToken ct)
    {
        CurrentState = KazuhaCollectState.WaitingForKazuha;
        var maskedUid = AutoPartyTask.MaskUid(_client.PlayerUid);
        _logger.LogInformation("[联机][聚物] 非万叶玩家 {Uid} 到达战斗点，等待万叶完成 (syncKey={Key})", maskedUid, syncKey);

        // subscribe-before-action：先订阅终态事件再上报到达，避免事件丢失
        var terminalTcs = new TaskCompletionSource<TerminalEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<string, bool> onFinished = (uid, success) =>
            terminalTcs.TrySetResult(new TerminalEvent(IsFinished: true, Success: success, Reason: null));
        Action<string> onSkipped = reason =>
            terminalTcs.TrySetResult(new TerminalEvent(IsFinished: false, Success: false, Reason: reason));

        _client.KazuhaCollectFinished += onFinished;
        _client.KazuhaCollectSkipped += onSkipped;

        TerminalEvent? ev = null;
        try
        {
            await _client.NotifyKazuhaArrivedAtFightPointAsync(syncKey, ct);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.KazuhaSyncTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                ev = await terminalTcs.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("[联机][聚物] 非万叶玩家：等待万叶完成超时 {Sec}s，按 KazuhaSyncWaitSeconds 兜底",
                    _config.KazuhaSyncTimeoutSeconds);
            }
        }
        finally
        {
            _client.KazuhaCollectFinished -= onFinished;
            _client.KazuhaCollectSkipped -= onSkipped;
        }

        if (ev != null && ev.IsFinished && ev.Success)
        {
            // 收到 Finished(true)：再停 KazuhaSyncWaitSeconds
            CurrentState = KazuhaCollectState.PostFinishedWait;
            _logger.LogInformation("[联机][聚物] 非万叶玩家：收到聚物完成，再停留 {Sec}s",
                _config.KazuhaSyncWaitSeconds);
            await Task.Delay(KazuhaCollectSyncDecisions.ComputePostTerminalWaitMs(_config, TerminalKind.FinishedSuccess), ct);
            CurrentState = KazuhaCollectState.Finished;
        }
        else
        {
            // Finished(false) / Skipped / 超时：统一停 KazuhaSyncWaitSeconds 秒后离开
            // （删除原 Math.Max(0, ReturnToFightPointStaySeconds*1000 - elapsedMs) 剩余时间补足分支）
            CurrentState = KazuhaCollectState.PostFinishedWait;
            var kind = ev == null
                ? TerminalKind.Timeout
                : (ev.IsFinished
                    ? TerminalKind.FinishedFailure
                    : TerminalKind.Skipped);
            _logger.LogInformation("[联机][聚物] 非万叶玩家：未收到 Finished(true)，kind={Kind}，按统一等待 {Sec}s 后离开",
                kind, _config.KazuhaSyncWaitSeconds);
            await Task.Delay(KazuhaCollectSyncDecisions.ComputePostTerminalWaitMs(_config, kind), ct);
            CurrentState = KazuhaCollectState.Skipped;
        }
    }

    /// <summary>每次进入新一段战后回点前调用，重置状态机。</summary>
    public void ResetForNextCycle()
    {
        CurrentState = KazuhaCollectState.Idle;
    }

    /// <summary>
    /// 把 Notify*Async 改成 fire-and-forget 时使用的兜底 helper：
    /// 转调独立 <see cref="FireAndForgetHelper.ObserveExceptions"/> 实现，便于 PBT-5 直接覆盖 helper 的语义。
    /// CoordinatorClient 内部已有 try/catch + LogWarning 静默吞 SignalR 异常；
    /// 此处兜底 OperationCanceledException / 其他未预期异常，避免 unobserved task exception 累积。
    /// 不允许使用裸 _ = client.InvokeAsync(...)（异常 sink 会丢失日志）。
    /// </summary>
    private void FireAndForget(Task task, string opLabel)
    {
        _ = FireAndForgetHelper.ObserveExceptions(task, _logger, opLabel);
    }

    /// <summary>
    /// 取消事件订阅。kazuha-player-auto-detection: KazuhaPlayerUpdated 订阅在构造函数中建立，
    /// 这里释放避免重复订阅触发或 _client 生命周期残留引用。
    /// </summary>
    public void Dispose()
    {
        _client.KazuhaPlayerUpdated -= _onKazuhaPlayerUpdated;
    }

    /// <summary>非万叶分支等待结果的内部承载类型。</summary>
    private sealed record TerminalEvent(bool IsFinished, bool Success, string? Reason);
}
