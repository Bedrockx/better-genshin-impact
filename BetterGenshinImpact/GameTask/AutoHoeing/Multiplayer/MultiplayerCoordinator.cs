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

    // === 连续超时控制（需求 5）===
    private int _consecutiveSyncTimeoutCount;
    private int _consecutiveSkipCount;

    // === 跳过同步点标志 ===
    private volatile bool _skipNextSyncPoint;

    // === 待处理等待点 ===
    private PendingWaitPoint? _pendingWaitPoint;

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
        int syncTimeoutSeconds)
    {
        _client = client;
        _config = TaskContext.Instance().Config.AutoHoeingConfig;
        _barrier = barrier;
        _resolver = resolver;
        _minPlayersToSync = minPlayersToSync;
        _syncTimeoutSeconds = syncTimeoutSeconds;
        IsHost = client.IsHost;

        // 初始化子协调器
        WaitPointStateManager = new WaitPointStateManager();
        RouteSyncCoordinator = new RouteSyncCoordinator(_client, this, _config);
        StateManager = new AbnormalStatusManager(this, WaitPointStateManager, _config);

        _logger.LogInformation("[联机] 子协调器初始化完成");
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
        try
        {
            await _client.WaitForAllPlayersAsync(syncId, ct, syncProgress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 等待所有玩家失败: {SyncId}", syncId);
        }
    }

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
        WaitPointStateManager?.Dispose();
        RouteSyncCoordinator = null;
        StateManager = null;
        WaitPointStateManager = null;
    }
}
