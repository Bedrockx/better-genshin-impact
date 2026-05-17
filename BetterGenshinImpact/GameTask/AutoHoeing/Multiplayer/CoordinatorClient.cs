#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// SignalR 客户端：负责与服务端通信。
/// 简化版：移除旧协调机制（MemberStatus、RouteSkipped、WaitPointReport、AbortAndRealign、RouteEnforceSync），
/// 保留基础连接、心跳、进度查询、异常通知等新机制所需功能。
/// </summary>
public class CoordinatorClient : IAsyncDisposable
{
    private readonly ILogger<CoordinatorClient> _logger = App.GetLogger<CoordinatorClient>();
    private HubConnection? _connection;
    private Timer? _heartbeatTimer;
    
    // 保存房间信息用于重连
    private string? _currentRoomCode;
    private string? _playerName;
    private string? _playerUid;

    // === 路线进度信息（BUG 1：保留用于线路协调检查）===
    private int _currentRouteIndex = -1;
    private DateTime _routeStartTime;
    private double _routeEstimatedSeconds;

    // === SignalR 断线重连 ===
    private volatile bool _isReconnecting;
    private volatile bool _isInRoom;

    private WorldStateMonitor? _worldStateMonitor;

    // === 玩家名称缓存（用于日志显示）===
    private readonly ConcurrentDictionary<string, string> _playerNameCache = new();

    public event Action<List<PlayerInfo>>? PlayerListUpdated;
    public event Action<string>? AllArrived;
    public event Action<string>? AllFightDone;
    public event Action<List<string>>? RouteDiffReceived;
    public event Action? RouteVerificationPassed;
    public event Action? OnDegraded;
    public event Action<string>? RoomClosed;
    public event Action? RouteVerificationAllDone;
    public event Action<int>? KazuhaPlayerUpdated;
    public event Action? AllWorldJoined;
    public event Action<bool>? HostReadyChanged;
    public event Action<List<string>>? HostRouteListReady;
    public event Action<string, int, bool>? PlayerAnomalyNotifyReceived; // playerUid, routeIndex, passedSyncPoint
    public event Action<string>? PlayerAnomalyRecoveredReceived; // playerUid
    public event Action<int>? StartRouteReceived; // targetRouteIndex

    public List<PlayerInfo> CurrentPlayerList { get; set; } = new();
    public int CurrentRoomPlayerCount { get; set; }
    public string HostPlayerUid { get; set; } = string.Empty;
    public bool IsHost => _playerUid == HostPlayerUid;
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;
    public bool IsInRoom => _isInRoom;
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// 当前玩家 UID（公开属性）
    /// </summary>
    public string PlayerUid => _playerUid ?? "";

    /// <summary>
    /// 当前玩家 UID（别名，用于兼容 TeamManager 等调用方）
    /// </summary>
    public string MyPlayerUid => _playerUid ?? "";

    /// <summary>
    /// 当前路线索引
    /// </summary>
    public int CurrentRouteIndex => _currentRouteIndex;
    public WorldStateMonitor? WorldStateMonitor
    {
        get => _worldStateMonitor;
        set => _worldStateMonitor = value;
    }

    public async Task<bool> ConnectAsync(string serverUrl, CancellationToken ct)
    {
        var maskedUrl = MaskServerUrl(serverUrl);
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(serverUrl)
                .Build();

            _connection.On<List<PlayerInfo>>("PlayerListUpdated",
                list =>
                {
                    CurrentRoomPlayerCount = list.Count;
                    if (list.Count > 0)
                        HostPlayerUid = list[0].PlayerUid;
                    CurrentPlayerList = new List<PlayerInfo>(list);
                    UpdatePlayerNameCache(list);
                    PlayerListUpdated?.Invoke(list);
                });

            _connection.On<string>("AllArrived",
                syncPointId => AllArrived?.Invoke(syncPointId));

            _connection.On<string>("AllFightDone",
                syncPointId => AllFightDone?.Invoke(syncPointId));

            _connection.On<List<string>>("RouteDiffReceived",
                diff => RouteDiffReceived?.Invoke(diff));

            _connection.On("RouteVerificationPassed",
                () => RouteVerificationPassed?.Invoke());

            _connection.On<string>("RoomClosed",
                reason => RoomClosed?.Invoke(reason));

            _connection.On("RouteVerificationAllDone",
                () => RouteVerificationAllDone?.Invoke());

            _connection.On<int>("KazuhaPlayerUpdated",
                index => KazuhaPlayerUpdated?.Invoke(index));

            _connection.On("AllWorldJoined",
                () => AllWorldJoined?.Invoke());

            _connection.On<bool>("HostReadyChanged",
                ready => HostReadyChanged?.Invoke(ready));

            _connection.On<List<string>>("HostRouteListReady",
                routeNames => HostRouteListReady?.Invoke(routeNames));

            // === 新机制：异常通知 ===
            _connection.On<string, int, bool>("PlayerAnomalyNotify",
                (playerUid, routeIndex, passedSyncPoint) =>
                {
                    if (playerUid == _playerUid) return; // 过滤自己
                    _logger.LogInformation("[联机] 收到异常通知: 玩家={PlayerUid}, 路线={RouteIndex}, 已过同步点={Passed}",
                        playerUid, routeIndex, passedSyncPoint);
                    PlayerAnomalyNotifyReceived?.Invoke(playerUid, routeIndex, passedSyncPoint);
                });

            // === 新机制：异常恢复通知 ===
            _connection.On<string>("PlayerAnomalyRecovered",
                (playerUid) =>
                {
                    if (playerUid == _playerUid) return;
                    _logger.LogInformation("[联机] 收到恢复通知: 玩家={PlayerUid}", playerUid);
                    PlayerAnomalyRecoveredReceived?.Invoke(playerUid);
                });

            // === 新机制：开始路线指令 ===
            _connection.On<int>("StartRoute",
                (targetRouteIndex) =>
                {
                    _logger.LogInformation("[联机] 收到开始路线指令: 目标路线={TargetRoute}", targetRouteIndex);
                    StartRouteReceived?.Invoke(targetRouteIndex);
                });

            _connection.Closed += OnConnectionClosed;

            await _connection.StartAsync(ct);
            _logger.LogInformation("CoordinatorClient 已连接到 {Url}", maskedUrl);

            // 启动心跳定时器，每 5 秒发送一次
            _heartbeatTimer = new Timer(async _ =>
            {
                try { await SendHeartbeatAsync(); }
                catch { /* 忽略心跳异常 */ }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CoordinatorClient 连接失败: {Url}", maskedUrl);
            return false;
        }
    }

    private async Task OnConnectionClosed(Exception? ex)
    {
        if (_isReconnecting)
        {
            _logger.LogWarning("CoordinatorClient 重连期间再次断线，忽略（等当前重连流程完成）");
            return;
        }

        _isReconnecting = true;
        _isInRoom = false;
        _logger.LogWarning(ex, "CoordinatorClient 连接断开，开始指数退避重连...");

        if (_connection == null)
        {
            _isReconnecting = false;
            return;
        }

        var retryDelays = new[] { 0, 2000, 5000, 10000 };
        bool reconnected = false;

        for (int round = 1; round <= 2 && !reconnected; round++)
        {
            if (round > 1)
            {
                _logger.LogInformation("CoordinatorClient 第{Round}轮重连前等待30秒...", round);
                await Task.Delay(30000);
            }

            for (int attempt = 0; attempt < retryDelays.Length; attempt++)
            {
                if (retryDelays[attempt] > 0)
                    await Task.Delay(retryDelays[attempt]);

                try
                {
                    await _connection.StartAsync();
                    reconnected = true;
                    _logger.LogInformation("CoordinatorClient 重连成功（第{Round}轮，第{Attempt}次）", round, attempt + 1);

                    // 重连后重新加入房间
                    if (!string.IsNullOrEmpty(_currentRoomCode) && !string.IsNullOrEmpty(_playerName))
                    {
                        await JoinRoomAsync(_currentRoomCode, _playerName, _playerUid ?? "");
                    }
                    break;
                }
                catch (Exception retryEx)
                {
                    _logger.LogWarning(retryEx, "CoordinatorClient 重连失败（第{Round}轮，第{Attempt}次）", round, attempt + 1);
                }
            }
        }

        _isReconnecting = false;

        if (!reconnected)
        {
            _logger.LogError("CoordinatorClient 重连失败，触发降级");
            OnDegraded?.Invoke();
        }
        else
        {
            _logger.LogInformation("CoordinatorClient 重连完成");
        }
    }

    private string MaskServerUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        try
        {
            var uri = new Uri(url);
            var maskedPath = string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/" ? "" : "/***";
            return $"{uri.Scheme}://***:****{maskedPath}";
        }
        catch
        {
            return url;
        }
    }

    /// <summary>
    /// 更新玩家名称缓存
    /// </summary>
    private void UpdatePlayerNameCache(List<PlayerInfo> players)
    {
        foreach (var player in players)
        {
            if (!string.IsNullOrEmpty(player.PlayerUid) && !string.IsNullOrEmpty(player.PlayerName))
                _playerNameCache[player.PlayerUid] = player.PlayerName;
        }
    }

    /// <summary>
    /// 获取玩家显示名称
    /// </summary>
    public string GetPlayerDisplayName(string playerUid)
    {
        if (string.IsNullOrEmpty(playerUid)) return "未知玩家";
        
        if (_playerNameCache.TryGetValue(playerUid, out var name) && !string.IsNullOrEmpty(name))
            return name;
        
        var player = CurrentPlayerList.FirstOrDefault(p => p.PlayerUid == playerUid);
        if (player != null && !string.IsNullOrEmpty(player.PlayerName))
        {
            _playerNameCache[playerUid] = player.PlayerName;
            return player.PlayerName;
        }
        
        if (playerUid.Length > 6)
            return $"{playerUid[..3]}***{playerUid[^3..]}";
        return playerUid;
    }

    public async Task<List<RoomSummary>> GetOnlineRoomsAsync()
    {
        if (_connection == null) return new List<RoomSummary>();
        try
        {
            var result = await _connection.InvokeAsync<List<RoomSummary>>("GetOnlineRooms");
            return result ?? new List<RoomSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOnlineRoomsAsync 失败");
            return new List<RoomSummary>();
        }
    }

    public async Task<bool> JoinRoomAsync(string roomCode, string playerName, string? playerUid = null)
    {
        if (_connection == null) return false;
        try
        {
            var result = await _connection.InvokeAsync<bool>("JoinRoom", roomCode, playerName, playerUid);
            _currentRoomCode = roomCode;
            _playerName = playerName;
            _playerUid = playerUid;
            _isInRoom = result;
            _logger.LogInformation("JoinRoomAsync 完成: Room={RoomCode}, Player={PlayerName}, Result={Result}",
                roomCode, playerName, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JoinRoomAsync 失败: Room={RoomCode}, Player={PlayerName}", roomCode, playerName);
            return false;
        }
    }

    public async Task LeaveRoomAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("LeaveRoom");
            _isInRoom = false;
            _logger.LogInformation("LeaveRoomAsync 完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LeaveRoomAsync 失败");
        }
    }

    public async Task CloseRoomAsync()
    {
        if (_connection == null) return;
        try
        {
            // 标记本地正在主动关闭房间，使后续到达的 RoomClosed 广播能识别为"自触发"
            // （本节点是房主，多世界轮次切换时主动关房，应避免 RoomClosed 取消主任务流程）
            _selfClosingRoom = true;
            await _connection.InvokeAsync("CloseRoom");
            _isInRoom = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloseRoomAsync 失败");
        }
    }

    /// <summary>本地是否正在主动关闭房间（CloseRoomAsync 已发起，等待广播回环）</summary>
    private volatile bool _selfClosingRoom;

    /// <summary>
    /// 检查并消费"自触发关闭"标志位。供 RoomClosed 订阅方使用：
    /// 若本次 RoomClosed 是本地主动关房导致的回环广播，应跳过取消逻辑。
    /// </summary>
    public bool ConsumeSelfClosingRoomFlag()
    {
        if (!_selfClosingRoom) return false;
        _selfClosingRoom = false;
        return true;
    }

    public async Task ResetWorldJoinedAsync()
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ResetWorldJoined");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResetWorldJoinedAsync 失败");
        }
    }

    /// <summary>
    /// 多轮世界重置：新轮次开始时调用
    /// </summary>
    public async Task ResetForNewWorldRoundAsync(int newRound)
    {
        if (_connection == null) return;
        try
        {
            await _connection.InvokeAsync("ResetForNewWorldRound", newRound);
            _logger.LogInformation("[联机] 发送多轮世界重置请求: Round {Round}", newRound);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResetForNewWorldRoundAsync 失败");
        }
    }

    public async Task SendHeartbeatAsync()
    {
        if (_connection == null) return;
        try
        {
            if (_currentRouteIndex >= 0)
            {
                await _connection.InvokeAsync("HeartbeatWithProgress",
                    _currentRouteIndex, _routeStartTime, _routeEstimatedSeconds);
            }
            else
            {
                await _connection.InvokeAsync("Heartbeat");
            }
            _worldStateMonitor?.NotifyHeartbeatSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SendHeartbeatAsync 失败");
            _worldStateMonitor?.NotifyHeartbeatFailure();
        }
    }

    /// <summary>
    /// 查询指定成员的路线进度（BUG 1：保留用于线路协调检查）
    /// </summary>
    public async Task<MemberProgress?> GetMemberProgressAsync(string playerUid)
    {
        if (_connection == null || !IsConnected) return null;
        try
        {
            return await _connection.InvokeAsync<MemberProgress?>("GetMemberProgress", playerUid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetMemberProgressAsync 失败: {Uid}", playerUid);
            return null;
        }
    }

    /// <summary>
    /// 更新当前路线进度（供 TeamManager 调用）
    /// </summary>
    public void UpdateRouteProgress(int routeIndex, DateTime startTime, double estimatedSeconds)
    {
        _currentRouteIndex = routeIndex;
        _routeStartTime = startTime;
        _routeEstimatedSeconds = estimatedSeconds;
    }

    /// <summary>
    /// 查询所有成员的路线进度（用于线路同步检查）
    /// </summary>
    public async Task<Dictionary<string, int>?> QueryRouteProgressAsync(CancellationToken ct)
    {
        if (_connection == null || !IsConnected) return null;
        try
        {
            // 服务器只有 GetMemberProgress(playerUid)，需要遍历所有玩家查询
            var result = new Dictionary<string, int>();
            foreach (var player in CurrentPlayerList)
            {
                try
                {
                    var progress = await _connection.InvokeAsync<MemberProgress?>("GetMemberProgress", player.PlayerUid, ct);
                    if (progress != null)
                    {
                        result[player.PlayerUid] = progress.RouteIndex;
                    }
                }
                catch
                {
                    // 忽略单个玩家的查询失败
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QueryRouteProgressAsync 失败");
            return null;
        }
    }

    /// <summary>
    /// 上报路线完成进度
    /// 注意：服务端目前未实现 ReportRouteProgress Hub 方法，此调用会失败。
    /// 当前的"进度值同步"机制（WaitForAllPlayers + MemberStatusChanged 重评估）
    /// 已完整覆盖路线同步功能，故此处改为 no-op，避免日志噪声。
    /// </summary>
    public Task ReportRouteProgressAsync(int completedRouteIndex)
    {
        // No-op: 旧机制已被进度值同步替代，保留方法签名兼容现有调用方。
        _logger.LogDebug("[联机] ReportRouteProgressAsync 调用（已 no-op）: completedRouteIndex={Index}", completedRouteIndex);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 创建房间
    /// </summary>
    public async Task<string?> CreateRoomAsync(string playerName, List<string>? whitelist, string playerUid, int expectedPlayerCount)
    {
        if (_connection == null) return null;
        try
        {
            var result = await _connection.InvokeAsync<string?>("CreateRoom",
                playerName, whitelist ?? new List<string>(), playerUid, expectedPlayerCount);
            _playerName = playerName;
            _playerUid = playerUid;
            _isInRoom = result != null;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateRoomAsync 失败");
            return null;
        }
    }

    /// <summary>
    /// 设置万叶玩家序号
    /// </summary>
    public async Task SetKazuhaPlayerAsync(int playerIndex)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("SetKazuhaPlayer", playerIndex);
            _logger.LogInformation("[联机] 设置万叶玩家序号: {Index}", playerIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetKazuhaPlayerAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上传房间配置
    /// </summary>
    public async Task SetRoomConfigAsync(Models.RoomConfig config)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("SetRoomConfig", config);
            _logger.LogInformation("[联机] 上传房间配置成功");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetRoomConfigAsync 失败");
            throw;
        }
    }

    /// <summary>
    /// 获取房间配置
    /// </summary>
    public async Task<Models.RoomConfig?> GetRoomConfigAsync()
    {
        if (_connection == null || !IsConnected) return null;
        try
        {
            var result = await _connection.InvokeAsync<Models.RoomConfig?>("GetRoomConfig");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetRoomConfigAsync 失败");
            return null;
        }
    }

    /// <summary>
    /// 上报房主就绪
    /// </summary>
    public async Task ReportHostReadyAsync()
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("ReportHostReady");
            _logger.LogInformation("[联机] 上报房主就绪");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportHostReadyAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报已加入世界
    /// </summary>
    public async Task ReportWorldJoinedAsync()
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("ReportWorldJoined");
            _logger.LogInformation("[联机] 上报已加入世界");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportWorldJoinedAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 查询房主是否就绪
    /// </summary>
    public async Task<bool> IsHostReadyAsync()
    {
        if (_connection == null || !IsConnected) return false;
        try
        {
            var result = await _connection.InvokeAsync<bool>("IsHostReady");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IsHostReadyAsync 失败");
            return false;
        }
    }

    /// <summary>
    /// 重置成员路线进度缓存
    /// </summary>
    public void ResetMemberProgressCache()
    {
        _currentRouteIndex = -1;
        _logger.LogDebug("[联机] 成员路线进度缓存已重置");
    }

    /// <summary>
    /// 获取指定玩家的路线索引
    /// </summary>
    public int? GetPeerRouteIndex(string playerUid)
    {
        return null; // 简化实现，无缓存
    }

    /// <summary>
    /// 上传房主路线列表
    /// </summary>
    public async Task SetHostRouteListAsync(List<string> routeNames)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("SetHostRouteList", routeNames);
            _logger.LogInformation("[联机] 上传房主路线列表: {Count} 条", routeNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SetHostRouteListAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 获取房主路线列表
    /// </summary>
    public async Task<List<string>> GetHostRouteListAsync()
    {
        if (_connection == null || !IsConnected) return new List<string>();
        try
        {
            var result = await _connection.InvokeAsync<List<string>>("GetHostRouteList");
            return result ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetHostRouteListAsync 失败");
            return new List<string>();
        }
    }

    /// <summary>
    /// 上报成员进度（跳过后广播）
    /// </summary>
    public async Task SendMemberProgressAsync(int routeIndex)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("ReportMemberProgress", PlayerUid ?? "", routeIndex);
            _logger.LogDebug("[联机] 发送成员进度: 路线 {Index}", routeIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SendMemberProgressAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报路线哈希列表（用于路线一致性验证）
    /// </summary>
    public async Task ReportRouteListAsync(List<Models.RouteHash> hashes)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("ReportRouteList", hashes);
            _logger.LogInformation("[联机] 上报路线列表: {Count} 条", hashes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportRouteListAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 重新加入当前房间
    /// </summary>
    public async Task<bool> RejoinCurrentRoomAsync()
    {
        if (_connection == null || string.IsNullOrEmpty(_currentRoomCode) || string.IsNullOrEmpty(_playerName)) return false;
        try
        {
            var result = await _connection.InvokeAsync<bool>("RejoinRoom", _currentRoomCode, _playerName);
            _isInRoom = result;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RejoinCurrentRoomAsync 失败");
            return false;
        }
    }

    /// <summary>
    /// 上报到达同步点
    /// </summary>
    public async Task ReportArrivalAsync(string syncPointId, int expectedCount = 0)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            if (expectedCount > 0)
                await _connection.InvokeAsync("ReportArrivalWithExpectedCount", syncPointId, expectedCount);
            else
                await _connection.InvokeAsync("ReportArrival", syncPointId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportArrivalAsync 失败: {SyncId}", syncPointId);
        }
    }

    /// <summary>
    /// 上报异常通知（新机制）
    /// </summary>
    public async Task ReportAnomalyAsync(string playerUid, int routeIndex, bool passedSyncPoint)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("PlayerAnomalyNotify", playerUid, routeIndex, passedSyncPoint);
            _logger.LogInformation("[联机] 发送异常通知: 玩家={PlayerUid}, 路线={RouteIndex}, 已过同步点={Passed}",
                playerUid, routeIndex, passedSyncPoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportAnomalyAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报异常恢复通知（新机制）
    /// </summary>
    public async Task ReportRecoveredAsync(string playerUid)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("PlayerAnomalyRecovered", playerUid);
            _logger.LogInformation("[联机] 发送恢复通知: 玩家={PlayerUid}", playerUid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportRecoveredAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报成员状态（Normal/Fighting/Rejoining/Reviving/Offline）
    /// targetProgress：异常恢复后将到达的同步点进度值（仅 Reviving/Rejoining 时有意义，其他时候传 -1）
    /// </summary>
    public async Task ReportMemberStatusAsync(MemberStatus status, long targetProgress = -1)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("MemberStatusChanged", PlayerUid ?? "", status.ToString(), targetProgress);
            _logger.LogDebug("[联机] 上报成员状态: {Status}, 目标进度={Target}", status, targetProgress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportMemberStatusAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报路线跳过
    /// </summary>
    public async Task ReportRouteSkippedAsync(int routeIndex)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("RouteSkipped", PlayerUid ?? "", routeIndex);
            _logger.LogInformation("[联机] 上报路线跳过: {Index}", routeIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportRouteSkippedAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报等待点到达
    /// </summary>
    public async Task ReportWaitPointAsync(string syncPointId)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("WaitPointReached", PlayerUid ?? "", syncPointId);
            _logger.LogDebug("[联机] 上报等待点: {SyncPointId}", syncPointId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportWaitPointAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 上报战斗状态
    /// </summary>
    public async Task ReportFightingStatusAsync(bool isFighting)
    {
        if (_connection == null || !IsConnected) return;
        try
        {
            await _connection.InvokeAsync("FightingStatusChanged", PlayerUid ?? "", isFighting);
            _logger.LogDebug("[联机] 上报战斗状态: {IsFighting}", isFighting);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReportFightingStatusAsync 失败（静默忽略）");
        }
    }

    /// <summary>
    /// 等待所有玩家到达指定同步点
    /// syncProgress：当前同步点的全局进度值（用于服务端判定异常玩家是否会经过此点）
    /// </summary>
    public async Task WaitForAllPlayersAsync(string syncId, CancellationToken ct, long syncProgress = -1)
    {
        if (_connection == null || !IsConnected) return;

        // 使用与 SyncBarrier 相同的模式：先订阅事件，再发送动作，本地等待
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnArrived(string id)
        {
            if (id == syncId)
            {
                tcs.TrySetResult(true);
            }
        }
        void OnRoomClosed(string reason)
        {
            tcs.TrySetCanceled();
        }

        AllArrived += OnArrived;
        RoomClosed += OnRoomClosed;

        try
        {
            // 使用 SendAsync（fire-and-forget），传入 syncId 和 syncProgress
            await _connection.SendAsync("WaitForAllPlayers", syncId, syncProgress);
            _logger.LogDebug("[联机] 请求等待所有玩家: {SyncId}, 进度={Progress}", syncId, syncProgress);

            // 本地等待 AllArrived 事件（受 CT 控制，可被用户取消）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[联机] 等待所有玩家超时或被取消: {SyncId}", syncId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WaitForAllPlayersAsync 失败: {SyncId}", syncId);
            throw;
        }
        finally
        {
            AllArrived -= OnArrived;
            RoomClosed -= OnRoomClosed;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection == null) return;
        try
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _isInRoom = false;
            _connection.Closed -= OnConnectionClosed;
            await _connection.StopAsync();
            _logger.LogInformation("CoordinatorClient 已断开连接");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DisconnectAsync 时发生异常");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        if (_connection != null)
        {
            _connection.Closed -= OnConnectionClosed;
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
