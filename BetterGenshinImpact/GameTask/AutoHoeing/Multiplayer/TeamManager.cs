#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 异常玩家信息
/// </summary>
public class AnomalyPlayerInfo
{
    public string PlayerUid { get; set; } = string.Empty;
    public int RouteIndex { get; set; }
    public bool PassedSyncPoint { get; set; }
    public int TargetRouteIndex { get; set; }
    public DateTime ReportTime { get; set; }
}

/// <summary>
/// 联机队伍管理器：负责同步点等待、异常玩家协调、线路对齐
/// </summary>
public class TeamManager : IAsyncDisposable
{
    private readonly ILogger<TeamManager> _logger = App.GetLogger<TeamManager>();
    private readonly CoordinatorClient _coordinatorClient;
    private readonly SyncBarrier _syncBarrier;
    private readonly AutoHoeingConfig _config;

    // === 异常玩家列表 ===
    private readonly ConcurrentDictionary<string, AnomalyPlayerInfo> _abnormalPlayers = new();
    private volatile bool _amIAbnormal;

    // === 超时计数 ===
    private int _consecutiveSyncTimeoutCount;
    private const int MaxConsecutiveTimeouts = 3;

    // === 状态标志 ===
    private volatile bool _isExitTriggered;
    private volatile bool _isDegraded;

    // === 事件 ===
    public event Action<bool>? OnConsecutiveSyncTimeoutExceeded; // isHost
    public event Action<string>? OnDegraded; // reason

    public TeamManager(CoordinatorClient coordinatorClient, SyncBarrier syncBarrier, AutoHoeingConfig config)
    {
        _coordinatorClient = coordinatorClient;
        _syncBarrier = syncBarrier;
        _config = config;

        // 注册 SignalR 事件
        _coordinatorClient.PlayerAnomalyNotifyReceived += OnPlayerAnomalyNotifyReceived;
        _coordinatorClient.PlayerAnomalyRecoveredReceived += OnPlayerAnomalyRecoveredReceived;
    }

    // === 公共属性 ===
    public bool IsActive => _coordinatorClient.IsConnected && !_isDegraded;
    public bool IsHost => _coordinatorClient.IsHost;
    public bool IsExitTriggered => _isExitTriggered;
    public int OnlinePlayerCount => _coordinatorClient.CurrentRoomPlayerCount;
    public bool HasAbnormalPlayers => !_abnormalPlayers.IsEmpty;
    public bool AmIAbnormal => _amIAbnormal;

    /// <summary>
    /// 在同步点等待队友
    /// </summary>
    /// <param name="syncId">同步点ID</param>
    /// <param name="currentRouteIndex">当前线路索引</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>线路同步决策</returns>
    public async Task<RouteSyncDecision> WaitAtSyncPoint(string syncId, int currentRouteIndex, CancellationToken ct)
    {
        if (!IsActive)
        {
            _logger.LogDebug("[联机] TeamManager 未激活，跳过同步点等待");
            return RouteSyncDecision.Proceed;
        }

        // 计算有效等待人数（排除在其他线路汇合的异常玩家）
        int effectiveCount = GetEffectiveWaitCount(currentRouteIndex);
        if (effectiveCount <= 0) effectiveCount = 1;

        // 计算超时秒数（有异常玩家→300，无→60）
        int timeoutSeconds = HasAbnormalPlayers ? 300 : 60;

        _logger.LogInformation("[联机] 在同步点 {SyncId} 等待，有效人数={Count}，超时={Timeout}秒，异常玩家数={Abnormal}",
            syncId, effectiveCount, timeoutSeconds, _abnormalPlayers.Count);

        try
        {
            // === 集体卡死跳段消费点 4（multiplayer-mutual-wait-collective-skip §8.7 / OQ-6 A）===
            // 把 RemoteSkipGate.Token 与 ct link，让 RequestSkipToProgress 立即解封 SyncBarrier 等待。
            // SyncBarrier.WaitAsync 收到 cancel 后返回 false（超时放行语义），由 PathExecutor 主循环 / 段切换点二次消费 _remoteSkipRequested 抛 RetryException。
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, RemoteSkipGate.Token);
            var effectiveCt = linkedCts.Token;
            bool allArrived = await _syncBarrier.WaitAsync(syncId, effectiveCount, timeoutSeconds, effectiveCt);

            if (allArrived)
            {
                _logger.LogInformation("[联机] 同步点 {SyncId} 全员到齐", syncId);

                // 如果是异常玩家且在目标同步点汇合 → 恢复
                if (_amIAbnormal)
                {
                    int? minTarget = GetMinAnomalyTargetRoute();
                    if (minTarget.HasValue && currentRouteIndex == minTarget.Value)
                    {
                        _logger.LogInformation("[联机] 异常玩家在目标同步点汇合，发送恢复通知");
                        await ReportRecovered();
                    }
                }

                // 执行线路协调检查
                RouteSyncDecision decision = CheckRouteAlignment(currentRouteIndex);

                if (decision == RouteSyncDecision.Proceed)
                {
                    // 重置连续超时计数
                    _consecutiveSyncTimeoutCount = 0;
                }

                return decision;
            }
            else
            {
                _logger.LogWarning("[联机] 同步点 {SyncId} 等待超时（{Timeout}秒）", syncId, timeoutSeconds);

                // 超时后也执行线路协调检查
                RouteSyncDecision decision = CheckRouteAlignment(currentRouteIndex);

                // 检查连续超时
                _consecutiveSyncTimeoutCount++;
                if (_consecutiveSyncTimeoutCount >= MaxConsecutiveTimeouts)
                {
                    _logger.LogError("[联机] 连续 {Count} 次同步超时，触发退出", MaxConsecutiveTimeouts);
                    OnConsecutiveSyncTimeoutExceeded?.Invoke(IsHost);
                    return RouteSyncDecision.Abort;
                }

                return decision;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[联机] 同步点等待被取消");
            throw;
        }
    }

    /// <summary>
    /// 上报异常（RetryException 导致路线跳过时调用）
    /// </summary>
    public async Task ReportAnomaly(int routeIndex, bool passedSyncPoint)
    {
        if (!IsActive) return;

        string myUid = _coordinatorClient.MyPlayerUid;
        if (string.IsNullOrEmpty(myUid)) return;

        // 本地记录
        _amIAbnormal = true;
        var info = new AnomalyPlayerInfo
        {
            PlayerUid = myUid,
            RouteIndex = routeIndex,
            PassedSyncPoint = passedSyncPoint,
            TargetRouteIndex = passedSyncPoint ? routeIndex + 1 : routeIndex,
            ReportTime = DateTime.UtcNow
        };
        _abnormalPlayers[myUid] = info;

        // 广播
        await _coordinatorClient.ReportAnomalyAsync(myUid, routeIndex, passedSyncPoint);

        _logger.LogWarning("[联机] 上报异常: 玩家={PlayerUid}, 路线={RouteIndex}, 已过同步点={Passed}",
            myUid, routeIndex, passedSyncPoint);
    }

    /// <summary>
    /// 上报异常恢复
    /// </summary>
    public async Task ReportRecovered()
    {
        if (!IsActive) return;

        string myUid = _coordinatorClient.MyPlayerUid;
        if (string.IsNullOrEmpty(myUid)) return;

        // 本地移除
        _amIAbnormal = false;
        _abnormalPlayers.TryRemove(myUid, out _);

        // 广播
        await _coordinatorClient.ReportRecoveredAsync(myUid);

        _logger.LogInformation("[联机] 上报恢复: 玩家={PlayerUid}", myUid);
    }

    /// <summary>
    /// 线路协调检查
    /// </summary>
    public RouteSyncDecision CheckRouteAlignment(int myNextRouteIndex)
    {
        // 没有异常玩家 → 直接通过
        if (!HasAbnormalPlayers)
        {
            return RouteSyncDecision.Proceed;
        }

        // 计算最小目标汇合线路
        int? minTarget = GetMinAnomalyTargetRoute();
        if (minTarget == null)
        {
            return RouteSyncDecision.Abort;
        }

        if (myNextRouteIndex == minTarget.Value)
        {
            // 我的下一条线路就是汇合目标 → 正常继续
            return RouteSyncDecision.Proceed;
        }
        else if (myNextRouteIndex > minTarget.Value)
        {
            // 我超前了，需要跳回目标线路
            _logger.LogWarning("[联机] 线路协调：我超前（我的={My}，目标={Target}），需要跳到线路{Target}",
                myNextRouteIndex, minTarget.Value, minTarget.Value);
            return RouteSyncDecision.SkipToTarget;
        }
        else
        {
            // 我落后（正常情况，异常玩家领先）→ 继续执行
            return RouteSyncDecision.Proceed;
        }
    }

    /// <summary>
    /// 计算有效等待人数
    /// </summary>
    public int GetEffectiveWaitCount(int currentRouteIndex)
    {
        int onlineCount = OnlinePlayerCount;
        if (onlineCount <= 0) return 1;

        // 排除目标汇合线路≠当前线路的异常玩家
        int? minTarget = GetMinAnomalyTargetRoute();
        if (!minTarget.HasValue) return onlineCount;

        int excludedCount = _abnormalPlayers.Values.Count(a => a.TargetRouteIndex != currentRouteIndex);
        int effective = onlineCount - excludedCount;
        return Math.Max(1, effective);
    }

    /// <summary>
    /// 获取异常玩家的最小目标线路索引
    /// </summary>
    public int? GetMinAnomalyTargetRoute()
    {
        var targets = _abnormalPlayers.Values.Select(a => a.TargetRouteIndex).ToList();
        if (targets.Count == 0) return null;
        return targets.Min();
    }

    /// <summary>
    /// 重置状态（新轮次开始时调用）
    /// </summary>
    public void ResetForNewRound()
    {
        _abnormalPlayers.Clear();
        _amIAbnormal = false;
        _consecutiveSyncTimeoutCount = 0;
        _logger.LogInformation("[联机] TeamManager 状态已重置（新轮次）");
    }

    /// <summary>
    /// 重置连续超时计数
    /// </summary>
    public void ResetSyncTimeoutCount()
    {
        _consecutiveSyncTimeoutCount = 0;
    }

    /// <summary>
    /// 触发协调停止
    /// </summary>
    public void TriggerCoordinatedStop(bool isHost, string reason)
    {
        _isExitTriggered = true;
        _logger.LogWarning("[联机] 协调停止: {Reason}", reason);
    }

    /// <summary>
    /// 降级模式
    /// </summary>
    public void Degrade(string reason)
    {
        if (_isDegraded) return;
        _isDegraded = true;
        _logger.LogWarning("[联机] 降级模式: {Reason}", reason);
        OnDegraded?.Invoke(reason);
    }

    /// <summary>
    /// 检查服务端是否可用
    /// </summary>
    public bool IsServerAvailable()
    {
        return _coordinatorClient.IsConnected && !_isDegraded;
    }

    // === SignalR 事件处理 ===
    private void OnPlayerAnomalyNotifyReceived(string playerUid, int routeIndex, bool passedSyncPoint)
    {
        var info = new AnomalyPlayerInfo
        {
            PlayerUid = playerUid,
            RouteIndex = routeIndex,
            PassedSyncPoint = passedSyncPoint,
            TargetRouteIndex = passedSyncPoint ? routeIndex + 1 : routeIndex,
            ReportTime = DateTime.UtcNow
        };
        _abnormalPlayers[playerUid] = info;

        _logger.LogInformation("[联机] 收到异常通知: 玩家={PlayerUid}, 路线={RouteIndex}, 已过同步点={Passed}",
            playerUid, routeIndex, passedSyncPoint);
    }

    private void OnPlayerAnomalyRecoveredReceived(string playerUid)
    {
        _abnormalPlayers.TryRemove(playerUid, out _);
        _logger.LogInformation("[联机] 收到恢复通知: 玩家={PlayerUid}", playerUid);
    }

    public async ValueTask DisposeAsync()
    {
        _coordinatorClient.PlayerAnomalyNotifyReceived -= OnPlayerAnomalyNotifyReceived;
        _coordinatorClient.PlayerAnomalyRecoveredReceived -= OnPlayerAnomalyRecoveredReceived;
        await _syncBarrier.DisposeAsync();
    }
}
