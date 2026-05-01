#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

public class MultiplayerCoordinator : IAsyncDisposable
{
    private readonly ILogger<MultiplayerCoordinator> _logger = App.GetLogger<MultiplayerCoordinator>();
    private readonly CoordinatorClient _client;
    private readonly SyncBarrier _barrier;
    private readonly SyncPointResolver _resolver;
    private readonly int _minPlayersToSync;
    private readonly int _syncTimeoutSeconds;

    private int _kazuhaPlayerIndex;
    private int _myPlayerIndex; // 1-based
    private readonly Action<string, MemberStatus> _onMemberStatusChangedHandler;

    // === 同步点超时容错（需求 5）===
    private int _consecutiveSyncTimeouts;
    private bool _consecutiveSyncTimeoutFired;
    private const int MaxConsecutiveSyncTimeouts = 3;

    // === 原子退出（需求 8）===
    private int _exitTriggered; // 0=未触发, 1=已触发
    private CancellationTokenSource? _stopCts; // 用于取消本地任务

    // === 成员离线感知 ===
    private readonly HashSet<string> _offlineMembers = new();
    private readonly object _offlineLock = new();

    public bool IsActive { get; private set; } = true;
    public bool IsKazuhaPlayer => _kazuhaPlayerIndex > 0 && _myPlayerIndex == _kazuhaPlayerIndex;

    /// <summary>当前是否为房主（动态判断，多世界模式下每轮可能不同）</summary>
    public bool IsHost => !string.IsNullOrEmpty(_client.HostPlayerUid)
        && _client.HostPlayerUid == TaskContext.Instance().Config.AutoHoeingConfig.PlayerUid;

    /// <summary>外部传入的 CancellationTokenSource，协调停止时 Cancel 它来停止任务。</summary>
    public CancellationTokenSource? StopCts
    {
        get => _stopCts;
        set => _stopCts = value;
    }

    /// <summary>退出是否已触发。</summary>
    public bool IsExitTriggered => _exitTriggered == 1;

    public event Action<string>? OnDegraded;

    /// <summary>
    /// 连续超时达到上限时触发。参数 isHost 表示当前是否为房主。
    /// 房主应广播 HostLeft 并停止；成员应上报 Offline 并退出世界。
    /// </summary>
    public event Func<bool, Task>? OnConsecutiveSyncTimeoutExceeded;

    public MultiplayerCoordinator(
        CoordinatorClient client,
        SyncBarrier barrier,
        SyncPointResolver resolver,
        int minPlayersToSync = 0,
        int syncTimeoutSeconds = 60)
    {
        _client = client;
        _barrier = barrier;
        _resolver = resolver;
        _minPlayersToSync = minPlayersToSync;
        _syncTimeoutSeconds = syncTimeoutSeconds;

        _client.OnDegraded += () => Degrade("连接断开且重连失败");
        _client.KazuhaPlayerUpdated += idx =>
        {
            _kazuhaPlayerIndex = idx;
            _logger.LogInformation("[联机] 万叶玩家索引更新为 {Idx}", idx);
        };
        _client.PlayerListUpdated += OnPlayerListUpdated;

        // 成员异常恢复状态变化（需求 7）+ 成员离线感知
        _onMemberStatusChangedHandler = (playerUid, status) =>
        {
            _logger.LogInformation("[联机] 成员状态变化: {Uid} → {Status}", playerUid, status);
            if (status == MemberStatus.Offline)
            {
                _logger.LogWarning("[联机] 成员 {Uid} 已离线", playerUid);
                lock (_offlineLock) { _offlineMembers.Add(playerUid); }

                // 房主检查剩余在线成员数（需求 2.2）
                if (IsHost)
                {
                    int onlineMembers;
                    lock (_offlineLock)
                    {
                        // 房间总人数 - 离线人数 - 房主自己 = 在线成员数
                        onlineMembers = _client.CurrentRoomPlayerCount - _offlineMembers.Count - 1;
                    }

                    if (onlineMembers <= 0)
                    {
                        _logger.LogError("[联机] 所有成员已离线，房主停止任务");
                        _ = TriggerCoordinatedStop(true, "所有成员已离线");
                    }
                    else
                    {
                        _logger.LogInformation("[联机] 成员离线，剩余 {Count} 个在线成员，继续执行", onlineMembers);
                    }
                }
            }
        };
        _client.OnMemberStatusChanged += _onMemberStatusChangedHandler;

        // 成员侧：监听 RoomClosed 事件（需求 2.5）
        _client.RoomClosed += reason =>
        {
            _logger.LogError("[联机] 收到 RoomClosed: {Reason}，触发协调停止", reason);
            _ = TriggerCoordinatedStop(false, $"房间已关闭: {reason}");
        };
    }

    private void OnPlayerListUpdated(List<PlayerInfo> players)
    {
        // 根据 ConnectionId 或 PlayerName 确定自己的序号（1-based）
        // 由于我们无法直接获取 ConnectionId，用列表顺序作为序号
        // 服务器广播的列表顺序是稳定的（加入顺序）
        _myPlayerIndex = 0; // 未知时为 0
        // PlayerListUpdated 会在 UI 线程处理，这里只记录人数
    }

    /// <summary>
    /// 重置连续超时计数。在每条路线开始时调用，避免跨路线累积误触发。
    /// </summary>
    public void ResetSyncTimeoutCount()
    {
        _consecutiveSyncTimeouts = 0;
        _logger.LogDebug("[联机] 连续超时计数已重置");
    }

    public async Task WaitForAllPlayers(string syncId, CancellationToken ct)
    {
        if (!IsActive || IsExitTriggered)
        {
            _logger.LogDebug("[联机] 已停止/退出，跳过集合等待 syncId={SyncId}", syncId);
            return;
        }

        // 连续超时已触发退出，跳过后续等待
        if (_consecutiveSyncTimeoutFired)
        {
            _logger.LogWarning("[联机] 连续超时退出已触发，跳过集合等待 syncId={SyncId}", syncId);
            return;
        }

        // 计算有效等待人数：排除离线成员（需求 2.3）
        var effectiveMin = _minPlayersToSync;
        if (effectiveMin == 0)
        {
            int offlineCount;
            lock (_offlineLock) { offlineCount = _offlineMembers.Count; }
            effectiveMin = Math.Max(1, _client.CurrentRoomPlayerCount - offlineCount);
        }

        if (effectiveMin <= 1)
        {
            _logger.LogInformation("[联机] 有效最低人数={Min}（房间人数={RoomCount}，配置最低={ConfigMin}），跳过集合等待 syncId={SyncId}",
                effectiveMin, _client.CurrentRoomPlayerCount, _minPlayersToSync, syncId);
            return;
        }

        try
        {
            var allArrived = await _barrier.WaitAsync(syncId, ct);

            if (allArrived)
            {
                // 全员到达，重置连续超时计数
                _consecutiveSyncTimeouts = 0;
                return;
            }

            // 标准超时放行 — 检查是否有异常状态成员需要额外等待
            var hasAbnormalMembers = _client.HasFightingMembers
                                    || _client.HasRejoiningMembers
                                    || _client.HasRevivingMembers;

            if (hasAbnormalMembers)
            {
                // 记录异常成员详情
                var abnormalDetails = _client.MemberStatuses
                    .Where(kv => kv.Value != MemberStatus.Normal)
                    .Select(kv => $"{kv.Key}={kv.Value}")
                    .ToList();
                _logger.LogInformation("[联机] 同步点 {SyncId} 标准超时，检测到异常状态成员: [{Members}]，进入额外等待",
                    syncId, string.Join(", ", abnormalDetails));

                // 额外等待：进度感知动态计算（需求 6）
                // Fighting → 固定 FightExtraWaitSeconds
                // Rejoining/Reviving → 查询进度计算剩余时间，查不到回退 RejoinMaxWaitSeconds
                var config = TaskContext.Instance().Config.AutoHoeingConfig;
                var extraWaitSeconds = await CalculateExtraWaitSecondsAsync(config);

                var extraArrived = await _barrier.WaitExtraAsync(syncId, extraWaitSeconds, ct);

                if (extraArrived)
                {
                    // 额外等待期间全员到达
                    _consecutiveSyncTimeouts = 0;
                    _logger.LogInformation("[联机] 同步点 {SyncId} 额外等待期间全员到达", syncId);
                    return;
                }

                _logger.LogWarning("[联机] 同步点 {SyncId} 额外等待也超时，放行", syncId);
            }
            else
            {
                // 记录未到达成员信息
                _logger.LogWarning("[联机] 同步点 {SyncId} 标准超时放行，无异常状态成员", syncId);
            }

            // 超时放行 — 递增连续超时计数（重连期间不计入，避免网络抖动触发连续超时退出）
            if (_client.IsReconnecting)
            {
                _logger.LogWarning("[联机] 同步点 {SyncId} 超时放行（重连中，不计入连续超时）", syncId);
            }
            else
            {
                _consecutiveSyncTimeouts++;
                _logger.LogWarning("[联机] 同步点 {SyncId} 超时放行，连续超时次数: {Count}/{Max}",
                    syncId, _consecutiveSyncTimeouts, MaxConsecutiveSyncTimeouts);

                // 检查连续超时是否达到上限
                if (_consecutiveSyncTimeouts >= MaxConsecutiveSyncTimeouts && !_consecutiveSyncTimeoutFired)
                {
                    _consecutiveSyncTimeoutFired = true;
                    _logger.LogError("[联机] 连续超时达到上限（{Max}次），触发退出", MaxConsecutiveSyncTimeouts);

                    if (OnConsecutiveSyncTimeoutExceeded != null)
                    {
                        var isHost = !string.IsNullOrEmpty(_client.HostPlayerUid)
                            && _client.HostPlayerUid == TaskContext.Instance().Config.AutoHoeingConfig.PlayerUid;
                        await OnConsecutiveSyncTimeoutExceeded.Invoke(isHost);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] WaitForAllPlayers 异常，syncId={SyncId}，跳过同步继续执行", syncId);
        }
    }

    /// <summary>
    /// 计算额外等待时间（需求 6：进度感知等待）。
    /// Fighting → 固定 FightExtraWaitSeconds。
    /// Rejoining/Reviving → 查询进度计算剩余时间，查不到回退 RejoinMaxWaitSeconds。
    /// </summary>
    private async Task<int> CalculateExtraWaitSecondsAsync(AutoHoeingConfig config)
    {
        // 只有 Fighting 成员 → 固定等待
        if (!_client.HasRejoiningMembers && !_client.HasRevivingMembers)
            return config.FightExtraWaitSeconds;

        // 有 Rejoining/Reviving 成员 → 尝试进度感知
        var maxWait = 0.0;
        foreach (var (uid, status) in _client.MemberStatuses)
        {
            if (status != MemberStatus.Rejoining && status != MemberStatus.Reviving)
                continue;

            var progress = await _client.GetMemberProgressAsync(uid);
            if (progress == null)
            {
                // 查不到进度，回退到固定值
                _logger.LogWarning("[联机] 无法获取成员 {Uid} 的进度信息，回退到固定等待 {Seconds}s", uid, config.RejoinMaxWaitSeconds);
                return config.RejoinMaxWaitSeconds;
            }

            var elapsed = (DateTime.UtcNow - progress.RouteStartTime).TotalSeconds;
            var remaining = progress.RouteEstimatedSeconds - elapsed + 60; // 60s 缓冲
            _logger.LogInformation("[联机] 成员 {Uid} 进度：路线{Index}，已用{Elapsed:F0}s，预估总{Est:F0}s，剩余{Remain:F0}s",
                uid, progress.RouteIndex, elapsed, progress.RouteEstimatedSeconds, remaining);
            maxWait = Math.Max(maxWait, remaining);
        }

        if (maxWait <= 0)
        {
            _logger.LogWarning("[联机] 进度计算结果 <= 0，回退到固定等待 {Seconds}s", config.RejoinMaxWaitSeconds);
            return config.RejoinMaxWaitSeconds;
        }

        var result = (int)Math.Min(maxWait, config.RejoinMaxWaitSeconds);
        _logger.LogInformation("[联机] 进度感知额外等待: {Seconds}s（上限 {Max}s）", result, config.RejoinMaxWaitSeconds);
        return result;
    }

    /// <summary>等待所有玩家完成路线验证。</summary>
    public async Task WaitForRouteVerificationAsync(CancellationToken ct)
    {
        if (!IsActive) return;

        var effectiveMin = _minPlayersToSync == 0 ? _client.CurrentRoomPlayerCount : _minPlayersToSync;
        if (effectiveMin <= 1) return;

        _logger.LogInformation("[联机] 等待所有玩家完成路线验证...");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_syncTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Action? handler = null;
        handler = () => tcs.TrySetResult(true);

        _client.RouteVerificationAllDone += handler;
        try
        {
            // 先上报一次
            await _client.ReportRouteVerificationDoneAsync();

            // 设置重试机制，每10秒重试一次上报
            var retryTimer = new Timer(async _ =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    _logger.LogDebug("[联机] 重试上报路线验证完成状态");
                    await _client.ReportRouteVerificationDoneAsync();
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            using var reg = linkedCts.Token.Register(() =>
            {
                retryTimer?.Dispose();
                if (ct.IsCancellationRequested)
                    tcs.TrySetCanceled(ct);
                else
                {
                    _logger.LogWarning("[联机] 路线验证同步等待超时({Timeout}s)，自动放行", _syncTimeoutSeconds);
                    tcs.TrySetResult(false);
                }
            });

            var result = await tcs.Task;
            retryTimer?.Dispose();
            _logger.LogInformation("[联机] 路线验证同步完成，结果: {Result}", result ? "全员完成" : "超时放行");
        }
        finally
        {
            _client.RouteVerificationAllDone -= handler;
        }
    }

    /// <summary>
    /// 触发协调停止流程（需求 2、8）。
    /// 使用 Interlocked.CompareExchange 保证只执行一次。
    /// RC-02: 先 Cancel _stopCts 停止本地任务，再执行网络操作。
    /// </summary>
    /// <param name="isHost">当前是否为房主</param>
    /// <param name="reason">停止原因</param>
    public async Task TriggerCoordinatedStop(bool isHost, string reason)
    {
        // 原子操作：只有第一个调用者能进入
        if (Interlocked.CompareExchange(ref _exitTriggered, 1, 0) != 0)
        {
            _logger.LogDebug("[联机] 协调停止已触发，忽略重复请求，来源: {Reason}", reason);
            return;
        }

        _logger.LogError("[联机] 触发协调停止，角色: {Role}，原因: {Reason}",
            isHost ? "房主" : "成员", reason);

        // RC-02: 先取消本地任务，确保本地尽快停止
        try { _stopCts?.Cancel(); }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("[联机] StopCts 已被 Dispose，跳过 Cancel（任务已在清理中）");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 取消本地任务失败");
        }

        // 再执行网络操作（所有操作用 try-catch 包裹，确保不抛异常，需求 8.5）
        if (isHost)
        {
            // 房主：发送 CloseRoom（需求 2.4, 2.8）
            try { await _client.CloseRoomAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机] 房主发送 CloseRoom 失败（成员靠心跳超时感知）");
            }
        }
        else
        {
            // 成员：上报 Offline（需求 2.1）
            try { await _client.ReportMemberStatusAsync(MemberStatus.Offline); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机] 成员上报 Offline 失败（房主靠心跳超时感知）");
            }
        }

        // 标记不再活跃
        IsActive = false;
    }

    /// <summary>
    /// 降级方法重构：不再简单设置 IsActive=false，而是触发协调停止。
    /// 保留方法签名兼容旧调用点。
    /// </summary>
    public void Degrade(string reason)
    {
        _logger.LogWarning("[联机] Degrade 调用，转为协调停止，原因: {Reason}", reason);
        var isHost = IsHost;
        // 异步触发，不阻塞调用方
        _ = Task.Run(async () =>
        {
            try { await TriggerCoordinatedStop(isHost, reason); }
            catch (Exception ex) { _logger.LogWarning(ex, "[联机] Degrade 触发协调停止异常"); }
        });
        OnDegraded?.Invoke(reason);
    }

    /// <summary>
    /// 上报战斗状态。进入战斗时 isFighting=true，战斗结束时 isFighting=false。
    /// 封装 CoordinatorClient.ReportMemberStatusAsync，PathExecutor 无需直接依赖 CoordinatorClient。
    /// </summary>
    public async Task ReportFightingStatusAsync(bool isFighting)
    {
        if (!IsActive) return;
        try
        {
            var status = isFighting ? MemberStatus.Fighting : MemberStatus.Normal;
            await _client.ReportMemberStatusAsync(status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 上报战斗状态失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报成员状态（需求 1）。封装 CoordinatorClient.ReportMemberStatusAsync。
    /// </summary>
    public async Task ReportMemberStatusAsync(MemberStatus status)
    {
        if (!IsActive || IsExitTriggered) return; // 需求 8.6: 退出后静默跳过
        try
        {
            await _client.ReportMemberStatusAsync(status);
            _logger.LogInformation("[联机] 上报成员状态: {Status}", status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 上报成员状态失败（静默忽略），状态: {Status}", status);
        }
    }

    /// <summary>多世界轮次切换时重置状态。</summary>
    public void ResetForNewRound()
    {
        _consecutiveSyncTimeouts = 0;
        _consecutiveSyncTimeoutFired = false;
        Interlocked.Exchange(ref _exitTriggered, 0); // 重置退出标志
        lock (_offlineLock) { _offlineMembers.Clear(); }
        IsActive = true;
        _logger.LogInformation("[联机] ResetForNewRound: 状态已重置");
    }

    public async ValueTask DisposeAsync()
    {
        _client.PlayerListUpdated -= OnPlayerListUpdated;
        _client.OnMemberStatusChanged -= _onMemberStatusChangedHandler;
        await _client.DisposeAsync();
    }
}
