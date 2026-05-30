#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 联机协调器门面类：统一管理联机模式下的所有协调逻辑。
/// 整合了路线同步、异常状态管理、等待点状态跟踪等功能，
/// 替代了之前分散在多个文件中的协调机制。
/// </summary>
public class MultiplayerCoordinator : IAsyncDisposable
{
    private readonly ILogger<MultiplayerCoordinator> _logger = App.GetLogger<MultiplayerCoordinator>();
    private readonly CoordinatorClient _client;
    private readonly AutoHoeingConfig _config;
    private readonly SyncBarrier _barrier;
    private readonly SyncPointResolver _resolver;
    private readonly int _minPlayersToSync;
    private readonly int _syncTimeoutSeconds;

    // === 子协调器 ===
    public RouteSyncCoordinator? RouteSyncCoordinator { get; private set; }
    public AbnormalStatusManager? StateManager { get; private set; }
    public WaitPointStateManager? WaitPointStateManager { get; private set; }
    /// <summary>
    /// 万叶聚物同步协调器（multiplayer-kazuha-collect-sync）。
    /// 由 PathExecutor 在战后回点 Delay 处通过 <see cref="KazuhaCollectSyncCoordinator.WaitAtFightPointAsync"/> 调用。
    /// </summary>
    public KazuhaCollectSyncCoordinator? KazuhaCollectSync { get; private set; }

    // === 基础状态 ===
    public bool IsHost { get; private set; }
    public bool IsConnected => _client.IsConnected;
    public bool IsExitTriggered { get; private set; }
    public bool IsAbortRequested { get; private set; }
    public int CurrentRouteIndex => _client.CurrentRouteIndex;
    
    /// <summary>
    /// 当前玩家 UID
    /// </summary>
    public string PlayerUid => _client.PlayerUid;

    /// <summary>
    /// 暴露持有的 AutoHoeingConfig 副本（AutoHoeingTask 在配置组场景下经
    /// JsonSerializer 深拷贝 + line 236-239 FastSync 字段强制重置 + line 243
    /// ApplySettingsOverride() 三步处理后产出的实例）。
    ///
    /// 供 PathExecutor 读取 FastSync 三字段（FastSyncPointEnabled /
    /// FastSyncPathingDistance / FastSyncTeleportLoadingDelayMs）时使用，
    /// 替代 TaskContext.Instance().Config.AutoHoeingConfig（全局未被配置组覆盖）。
    ///
    /// 与 KazuhaCollectSyncCoordinator._config 暴露给 PathExecutor 的模式同构
    /// （详见 .kiro/specs/pathexecutor-fastsync-config-readsource-fix）。
    ///
    /// 本 getter 返回引用，C# 层面调用方可写，但调用纪律保证只读使用
    /// （与 KazuhaCollectSync 现有持有副本不做防护的模式对齐，OQ-5=a 决议）。
    /// </summary>
    public AutoHoeingConfig EffectiveConfig => _config;

    // === 连续超时控制（需求 5）===
    private int _consecutiveSyncTimeoutCount;
    private int _consecutiveSkipCount;

    // === 跳过同步点标志 ===
    private volatile bool _skipNextSyncPoint;

    // === 集体卡死跳段信号位（multiplayer-mutual-wait-collective-skip spec / OQ-6 A）===
    /// <summary>
    /// 服务端 RequestSkipToProgress 事件触发后置 1，客户端 4 处消费点 CAS 1→0 命中后跳段。
    /// 与 PathExecutor._multiplayerRevivalDetected 完全独立（preservation §3.4）。
    /// 多线程可见性由 Interlocked.Exchange 保证。
    /// </summary>
    private int _remoteSkipRequested = 0;

    /// <summary>
    /// 缓存 RequestSkipToProgress 事件携带的 targetProgress；与 _remoteSkipRequested 配对读写。
    /// </summary>
    private long _remoteSkipTargetProgress = -1;

    // === 待处理等待点 ===
    private PendingWaitPoint? _pendingWaitPoint;

    // === 抢报已记录集合（fastsync-redesign-parameter-passing spec 修复）===
    /// <summary>
    /// 已经通过 FastReportAsync（fire-and-forget）上报过到达的 syncId 集合。
    /// 后续严格路径 WaitForAllPlayers 进入前查此集合：命中 → 直接返回（不阻塞）；
    /// 未命中 → 走原有"订阅 + 上报 + 等 AllArrived"流程。
    ///
    /// 服务端 RecordArrival 是 idempotent，但已经广播过 AllArrived 后 ArrivalSet 会清空，
    /// 严格路径再上报会陷入永久等待——本集合用于让抢报方自己跳过严格等待。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _fastReportedSyncIds = new();

    // === 事件 ===
    public event Action<string>? OnDegraded;
    public event Action<bool>? OnConsecutiveSyncTimeoutExceeded;

    // === CancellationTokenSource（需求 2.1）===
    public CancellationTokenSource? StopCts { get; set; }

    public MultiplayerCoordinator(
        CoordinatorClient client,
        SyncBarrier barrier,
        SyncPointResolver resolver,
        int minPlayersToSync,
        int syncTimeoutSeconds,
        AutoHoeingConfig? config = null)
    {
        _client = client;
        // 优先使用调用方传入的 config（持有 AutoHoeingTask 拷贝/覆盖后的实例），
        // 否则 fallback 到全局（保持向后兼容）。
        // 配置组（ScriptGroup）启动时 AutoHoeingTask 会在深拷贝上 ApplySettingsOverride，
        // 此时全局未被覆盖，必须由调用方显式传入覆盖后的 _config，否则 KazuhaCollectSync 的
        // EnableKazuhaSync 永远是全局值（多半为 false）→ PathExecutor 跳过聚物分支。
        _config = config ?? TaskContext.Instance().Config.AutoHoeingConfig;
        _barrier = barrier;
        _resolver = resolver;
        _minPlayersToSync = minPlayersToSync;
        _syncTimeoutSeconds = syncTimeoutSeconds;
        IsHost = client.IsHost;

        // 初始化子协调器
        WaitPointStateManager = new WaitPointStateManager();
        RouteSyncCoordinator = new RouteSyncCoordinator(_client, this, _config);
        StateManager = new AbnormalStatusManager(this, WaitPointStateManager, _config);
        KazuhaCollectSync = new KazuhaCollectSyncCoordinator(_client, _config, this);

        // 集体卡死跳段事件订阅（multiplayer-mutual-wait-collective-skip §8.6）
        _client.RequestSkipToProgressReceived += OnRequestSkipToProgressReceived;
        _client.CollectiveSkipDegradedReceived += OnCollectiveSkipDegradedReceived;

        _logger.LogInformation("[联机] 子协调器初始化完成");
    }

    // === 集体卡死跳段事件处理（multiplayer-mutual-wait-collective-skip §8.6）===

    private void OnRequestSkipToProgressReceived(long targetProgress)
    {
        _remoteSkipTargetProgress = targetProgress;
        RemoteSkipGate.TargetProgress = targetProgress;
        // Interlocked.Exchange 保证写入对所有线程立即可见
        Interlocked.Exchange(ref _remoteSkipRequested, 1);
        _logger.LogWarning("[联机] 大部队请求跳段，target={Target}，等待 4 处消费点命中", targetProgress);

        // 唤醒 SyncBarrier.WaitAsync 等待（消费点 4，design §8.7 备选 B 静态 Gate）
        RemoteSkipGate.Cancel();
    }

    private void OnCollectiveSkipDegradedReceived(string reason)
    {
        _logger.LogError("[联机] 集体跳段连续触发达上限，触发协调停止：{Reason}", reason);
        // 走 OnConsecutiveSyncTimeoutExceeded 等价路径（OQ-5 A）
        _ = TriggerCoordinatedStop(IsHost, $"CollectiveSkip:{reason}");
    }

    /// <summary>
    /// CAS 消费 _remoteSkipRequested 信号位（与 PathExecutor.TryConsumeRevivalSignal 同模式但**独立信号位**）。
    /// 命中：返回 true 并通过 out 返回 targetProgress；未命中：返回 false。
    /// 单机模式下调用方已用 <c>MultiplayerCoordinator != null</c> 守卫，本方法不重复短路。
    /// </summary>
    public bool TryConsumeRemoteSkipSignal(out long targetProgress)
    {
        var prev = Interlocked.Exchange(ref _remoteSkipRequested, 0);
        if (prev == 1)
        {
            targetProgress = _remoteSkipTargetProgress;
            _remoteSkipTargetProgress = -1;
            return true;
        }
        targetProgress = -1;
        return false;
    }

    /// <summary>
    /// 降级为单机模式
    /// </summary>
    public void Degrade(string reason)
    {
        _logger.LogWarning("[联机] 降级为单机模式，原因：{Reason}", reason);
        OnDegraded?.Invoke(reason);
    }

    /// <summary>
    /// 触发协调停止（需求 2.2）
    /// </summary>
    public async Task TriggerCoordinatedStop(bool isHost, string reason)
    {
        IsExitTriggered = true;
        _logger.LogWarning("[联机] 触发协调停止，原因：{Reason}", reason);
        
        if (isHost)
        {
            try { await _client.CloseRoomAsync(); } catch { }
        }
        
        StopCts?.Cancel();
    }

    /// <summary>
    /// 重置连续超时计数（需求 5）
    /// </summary>
    public void ResetSyncTimeoutCount()
    {
        _consecutiveSyncTimeoutCount = 0;
        _consecutiveSkipCount = 0;
    }

    /// <summary>
    /// 增加连续超时计数
    /// </summary>
    public void IncrementSyncTimeoutCount()
    {
        _consecutiveSyncTimeoutCount++;
        _logger.LogWarning("[联机] 连续超时次数: {Count}/{Max}", 
            _consecutiveSyncTimeoutCount, _config.MaxConsecutiveTimeouts);
        
        if (_consecutiveSyncTimeoutCount >= _config.MaxConsecutiveTimeouts)
        {
            OnConsecutiveSyncTimeoutExceeded?.Invoke(IsHost);
        }
    }

    /// <summary>
    /// 重置为新轮次
    /// </summary>
    public void ResetForNewRound()
    {
        _consecutiveSyncTimeoutCount = 0;
        _consecutiveSkipCount = 0;
        _skipNextSyncPoint = false;
        IsExitTriggered = false;
        IsAbortRequested = false;

        // 集体卡死跳段信号位重置（multiplayer-mutual-wait-collective-skip §8.6 改动 4）
        Interlocked.Exchange(ref _remoteSkipRequested, 0);
        _remoteSkipTargetProgress = -1;
        RemoteSkipGate.Reset();

        RouteSyncCoordinator?.Reset();
        StateManager?.Reset();
        WaitPointStateManager?.ResetCurrentRound();
        
        _logger.LogInformation("[联机] 已重置为新轮次");
    }

    // === 等待点相关 ===

    public bool HasPendingWaitPoint => _pendingWaitPoint != null && !_pendingWaitPoint.IsProcessed;

    public PendingWaitPoint? GetPendingWaitPoint() => _pendingWaitPoint;

    public void SetPendingWaitPoint(PendingWaitPoint point)
    {
        _pendingWaitPoint = point;
        _logger.LogInformation("[联机] 设置待处理等待点: {SyncId}, 强制={Forced}", point.SyncPointId, point.IsForced);
    }

    public void ClearPendingWaitPoint()
    {
        if (_pendingWaitPoint != null)
        {
            _logger.LogInformation("[联机] 清除待处理等待点: {SyncId}", _pendingWaitPoint.SyncPointId);
            _pendingWaitPoint.IsProcessed = true;
            _pendingWaitPoint = null;
        }
    }

    /// <summary>
    /// 检查指定同步点是否为异常等待点
    /// </summary>
    public bool IsAbnormalWaitingAtPoint(string syncPointId)
    {
        // 简化实现：如果有 _pendingWaitPoint 且匹配则返回 IsForced
        return _pendingWaitPoint != null && !_pendingWaitPoint.IsProcessed && _pendingWaitPoint.SyncPointId == syncPointId && _pendingWaitPoint.IsForced;
    }

    public void SetSkipNextSyncPoint()
    {
        _skipNextSyncPoint = true;
        _consecutiveSkipCount++;
        _logger.LogInformation("[联机] 设置跳过下一个同步点，连续跳过次数: {Count}", _consecutiveSkipCount);
    }

    public bool ShouldSkipNextSyncPoint()
    {
        if (_skipNextSyncPoint)
        {
            _skipNextSyncPoint = false;
            return true;
        }
        return false;
    }

    public async Task NotifyRouteSkippedAsync(int routeIndex)
    {
        try
        {
            await _client.ReportRouteSkippedAsync(routeIndex);
            _logger.LogInformation("[联机] 上报路线跳过: {Index}", routeIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 上报路线跳过失败");
        }
    }

    public async Task<bool> ReportWaitPointAsync(string routeId, string syncPointId, int worldRound)
    {
        try
        {
            await _client.ReportWaitPointAsync(syncPointId);
            WaitPointStateManager?.UpdateState(_client.PlayerUid ?? "", new WaitPointState
            {
                PlayerUid = _client.PlayerUid ?? "",
                RouteId = routeId,
                SyncPointId = syncPointId,
                WorldRound = worldRound,
                LastUpdated = DateTime.Now
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 上报等待点失败");
            return false;
        }
    }

    // === 中断重对齐相关 ===

    public int GetAbortTargetRouteIndex()
    {
        return _client.CurrentRouteIndex + 1;
    }

    public bool IsRouteEnforceSyncRequested => false; // 简化实现

    public int GetEnforceTargetRouteIndex()
    {
        return _client.CurrentRouteIndex;
    }

    // === 等待所有玩家 ===

    public async Task WaitForAllPlayers(string syncId, CancellationToken ct, long syncProgress = -1)
    {
        // 抢报已经成功的 syncId：自己已经上报过，服务端可能也已经广播过 AllArrived 把 ArrivalSet 清掉了，
        // 再走一次严格 WaitForAllPlayersAsync 会订阅一个永远不会触发的事件 → 死等到超时。
        // 直接 short-circuit 返回让调用方继续走（fastsync-redesign-parameter-passing spec 修复）。
        if (_fastReportedSyncIds.ContainsKey(syncId))
        {
            _logger.LogInformation("[联机][FastSync] WaitForAllPlayers short-circuit（本玩家已抢报过）: {SyncId}", syncId);
            return;
        }

        try
        {
            await _client.WaitForAllPlayersAsync(syncId, ct, syncProgress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 等待所有玩家失败: {SyncId}", syncId);
        }
    }

    /// <summary>
    /// 抢报专用 fire-and-forget：仅向服务端记录到达，不阻塞调用方
    /// （fastsync-redesign-parameter-passing spec）。
    /// 命中后将 syncId 加入 _fastReportedSyncIds，让后续严格 WaitForAllPlayers 路径
    /// short-circuit 直接返回，避免服务端 ArrivalSet 清空后再订阅事件死等。
    /// </summary>
    public async Task FastReportAsync(string syncId, long syncProgress = -1)
    {
        try
        {
            await _client.FireAndForgetArrivalAsync(syncId, syncProgress);
            _fastReportedSyncIds.TryAdd(syncId, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 抢报失败（已忽略）: {SyncId}", syncId);
        }
    }

    /// <summary>
    /// 查询某 syncId 是否已经通过 FastReportAsync 抢报过。
    /// 调用方（PathExecutor）用此判断在段内寻找"下一个还没抢报的 syncPoint"作为提前距离判定目标。
    /// </summary>
    public bool IsFastReported(string syncId) => _fastReportedSyncIds.ContainsKey(syncId);

    // === 上报状态 ===

    public async Task ReportFightingStatusAsync(bool isFighting)
    {
        try
        {
            await _client.ReportFightingStatusAsync(isFighting);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 上报战斗状态失败");
        }
    }

    public async Task ReportMemberStatusAsync(MemberStatus status, long targetProgress = -1)
    {
        try
        {
            await _client.ReportMemberStatusAsync(status, targetProgress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 上报成员状态失败");
        }
    }

    // === 路线验证同步等待 ===

    /// <summary>
    /// 等待路线验证完成
    /// </summary>
    public async Task WaitForRouteVerificationAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Action? onPassed = () => tcs.TrySetResult(true);
        Action? onTimeout = () =>
        {
            _logger.LogWarning("[联机] 等待路线验证超时（90秒），继续执行");
            tcs.TrySetResult(false);
        };

        _client.RouteVerificationPassed += onPassed;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var reg = linkedCts.Token.Register(onTimeout);

            await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[联机] 等待路线验证被取消");
        }
        finally
        {
            _client.RouteVerificationPassed -= onPassed;
        }
    }

    // === 开始路线指令等待 ===

    /// <summary>
    /// 等待服务器广播的开始路线指令
    /// </summary>
    public async Task<int> WaitForStartRouteAsync(int timeoutSeconds, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Action<int>? onStartRoute = routeIndex => tcs.TrySetResult(routeIndex);
        Action? onTimeout = () =>
        {
            _logger.LogWarning("[联机] 等待开始路线指令超时（{Timeout}s）", timeoutSeconds);
            tcs.TrySetResult(-1);
        };

        _client.StartRouteReceived += onStartRoute;

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var reg = linkedCts.Token.Register(onTimeout);

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return -1;
        }
        finally
        {
            _client.StartRouteReceived -= onStartRoute;
        }
    }

    // === 中断状态清除 ===

    /// <summary>
    /// 清除中断请求状态
    /// </summary>
    public void ClearAbortState()
    {
        IsAbortRequested = false;
        _logger.LogDebug("[联机] 中断状态已清除");
    }

    // === 强制同步状态清除 ===

    /// <summary>
    /// 清除强制线路同步状态
    /// </summary>
    public void ClearRouteEnforceSync()
    {
        // 简化实现，无强制同步状态需要清除
        _logger.LogDebug("[联机] 强制线路同步状态已清除");
    }

    public async ValueTask DisposeAsync()
    {
        // 集体卡死跳段事件取消订阅 + Gate 重置（multiplayer-mutual-wait-collective-skip §8.6 改动 5）
        try
        {
            _client.RequestSkipToProgressReceived -= OnRequestSkipToProgressReceived;
            _client.CollectiveSkipDegradedReceived -= OnCollectiveSkipDegradedReceived;
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "[联机] DisposeAsync 解绑 RequestSkipToProgress 事件失败（可忽略）");
        }
        RemoteSkipGate.Reset();

        WaitPointStateManager?.Dispose();
        KazuhaCollectSync?.Dispose();
        RouteSyncCoordinator = null;
        StateManager = null;
        WaitPointStateManager = null;
        KazuhaCollectSync = null;
    }
}
