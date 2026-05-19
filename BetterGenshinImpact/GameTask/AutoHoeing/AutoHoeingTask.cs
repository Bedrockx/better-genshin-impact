using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using BetterGenshinImpact.GameTask.AutoHoeing.Services;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;


namespace BetterGenshinImpact.GameTask.AutoHoeing;

/// <summary>
/// 锄地一条龙 - BetterGI原生C#独立任务
/// 将JS脚本"锄地一条龙"的全部功能转写为原生任务
/// </summary>
public class AutoHoeingTask : ISoloTask
{
    public string Name => "锄地一条龙";

    private readonly ILogger<AutoHoeingTask> _logger = App.GetLogger<AutoHoeingTask>();
    private AutoHoeingConfig _config = null!;
    private CancellationToken _ct;

    /// <summary>
    /// 配置组传入的地图追踪配置（含队伍、战斗策略等），为null时使用默认配置
    /// </summary>
    private readonly PathingPartyConfig? _partyConfig;

    /// <summary>
    /// 配置组传入的独立任务配置覆盖，为null时使用全局AutoHoeingConfig
    /// </summary>
    private readonly Dictionary<string, object?>? _settingsOverride;

    /// <summary>
    /// 配置组名称
    /// </summary>
    private readonly string? _groupName;

    // 数据目录（路线文件、怪物信息、运行记录等）
    private string _dataDir = "";

    // 服务
    private readonly MonsterInfoRepository _monsterRepo = new();
    private readonly CdManager _cdManager = new();
    private readonly RouteSelector _routeSelector = new();
    private readonly TimeRestrictionChecker _timeChecker = new();
    private readonly TemplatePickupService _pickupService = new();
    private readonly AnomalyDetector _anomalyDetector = new();
    private readonly DumperService _dumperService = new();
    private readonly BlacklistManager _blacklistManager = new();
    private readonly CookingService _cookingService = new();
    private RouteConsistencyChecker? _consistencyChecker;
    private CoordinatorClient? _coordinatorClientRef;
    private RouteExecutionEngine? _executionEngine;
    private MultiplayerCoordinator? _multiplayerCoordinator;
    private WorldStateMonitor? _worldStateMonitor;
    private bool _shouldSwitchFurina;
    
    private volatile bool _sessionTerminated;
    private string? _stopReason;
    private CancellationTokenSource? _linkedStopCts;
    
    private bool _teamAlreadySwitched = false;
    private bool _worldPermissionSet = false;

    /// <summary>
    /// 隐藏服务器地址的前半部分（隐私保护）
    /// </summary>
    private static string MaskServerUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url ?? "";
        try
        {
            // 例如 http://121.4.78.52:8080/hub -> http://***:****/***
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
    /// <summary>
    /// 多世界模式下，保存第一任房主的配置，后续轮次都使用这个配置
    /// </summary>
    private RoomConfig? _firstHostConfig;
    
    public static volatile bool SkipPartyWait = false; // 房主点击"立即开始"时设为 true
    public static volatile bool IsWaitingForParty = false; // 是否正在等待组队（进入F2页面）

    public AutoHoeingTask(PathingPartyConfig? partyConfig = null, Dictionary<string, object?>? settings = null, string? groupName = null)
    {
        _partyConfig = partyConfig;
        _settingsOverride = settings;
        _groupName = groupName;
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        _config = TaskContext.Instance().Config.AutoHoeingConfig;

        // 如果有配置组传入的覆盖配置，在全局配置的深拷贝上应用，避免污染全局状态
        if (_settingsOverride != null && _settingsOverride.Count > 0)
        {
            var json = JsonSerializer.Serialize(_config);
            _config = JsonSerializer.Deserialize<AutoHoeingConfig>(json) ?? _config;

            // 重置所有可能被其他配置组污染的字段为默认值
            // 这些字段若配置组 settings 里有显式配置，ApplySettingsOverride 会覆盖；
            // 若没有配置，则保持干净的默认值，不受上一次执行的残留影响
            _config.MultiplayerEnabled = false;
            _config.DebugMode = false;
            _config.StartRouteIndex = 0;
            _config.SyncTimeoutSeconds = 60;
            _config.MinPlayersToSync = 0;
            _config.SyncPointMinDistance = 30.0;
            _config.KazuhaPlayerIndex = 0;
            _config.ReturnToFightPointAfterBattle = false;
            _config.ReturnToFightPointStaySeconds = 5;
            _config.MultiWorldEnabled = false;
            _config.MultiWorldCount = 2;
            _config.FightExtraWaitSeconds = 60;
            _config.RejoinMaxWaitSeconds = 300;

            ApplySettingsOverride();
        }

        // 数据目录：使用JS脚本原有的pathing目录
        _dataDir = Path.Combine(
            AppContext.BaseDirectory,
            "User", "JsScript", "AutoHoeingOneDragon");

        // 检查JS脚本资源是否存在
        if (!Directory.Exists(_dataDir) || !Directory.Exists(Path.Combine(_dataDir, "pathing")))
        {
            _logger.LogError("锄地一条龙资源目录不存在: {Dir}", _dataDir);
            _logger.LogError("请先在「脚本仓库」中订阅并下载「AutoHoeingOneDragon」JS脚本，独立任务依赖该脚本的路线和资源文件");

            // 在UI线程弹窗提示
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Wpf.Ui.Violeta.Controls.Toast.Warning(
                    "锄地一条龙资源未找到，请先在「脚本仓库」中订阅并下载「AutoHoeingOneDragon」脚本");
            });
            return;
        }

        if (!string.IsNullOrEmpty(_groupName))
        {
            _logger.LogInformation("锄地一条龙任务启动 [配置组: {Group}]，数据目录: {Dir}", _groupName, _dataDir);
        }
        else
        {
            _logger.LogInformation("锄地一条龙任务启动，数据目录: {Dir}", _dataDir);
        }

        try
        {
            await RunTask();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("锄地一条龙任务被取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "锄地一条龙任务异常终止");
        }
        finally
        {
            // 清除联机战斗超时覆盖值
            PathingConditionConfig.MultiplayerFightTimeoutOverride = null;
            PathExecutor.CurrentWorldStateMonitor = null;

            // 房主兜底：确保关闭房间通知已发送
            if (_multiplayerCoordinator != null && _multiplayerCoordinator.IsHost)
            {
                try { await _coordinatorClientRef!.CloseRoomAsync(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[联机] finally 中房主发送 CloseRoom 失败（静默忽略）");
                }
            }

            // 停止世界状态监测（需求 2）
            if (_worldStateMonitor != null)
            {
                await _worldStateMonitor.StopAsync();
                _worldStateMonitor = null;
            }

            // 先 dispose coordinator（取消事件订阅），再 dispose linkedStopCts
            // 避免 dispose 后 RoomClosed 事件触发 TriggerCoordinatedStop 时 Cancel 已 dispose 的 CTS
            if (_multiplayerCoordinator != null)
            {
                await _multiplayerCoordinator.DisposeAsync();
                _multiplayerCoordinator = null;
            }

            if (_linkedStopCts != null)
            {
                _linkedStopCts.Dispose();
                _linkedStopCts = null;
            }
            // 多世界模式下 _coordinatorClientRef 由 RunTask 管理，这里只做兜底清理
            if (_coordinatorClientRef != null)
            {
                await _coordinatorClientRef.DisposeAsync();
                _coordinatorClientRef = null;
            }

            // 联机中断时通知用户并退出世界
            if (!string.IsNullOrEmpty(_stopReason) && _config.MultiplayerEnabled)
            {
                // 房主和成员都退出联机世界回到单人世界
                try
                {
                    _logger.LogInformation("[联机] {Role}退出联机世界",
                        _config.MultiplayerRole == "member" ? "成员" : "房主");
                    using var leaveWorldCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var autoParty = new AutoPartyTask();
                    var left = await autoParty.LeaveWorldAsync(leaveWorldCts.Token);
                    if (!left)
                        _logger.LogWarning("[联机] 退出世界未确认成功");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[联机] 退出世界失败（忽略）");
                }

                var reason = _stopReason;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Wpf.Ui.Violeta.Controls.Toast.Warning($"联机中断：{reason}");
                });
            }
        }
    }

    private async Task InitializeMultiplayerAsync()
    {
        try
        {
            var client = new CoordinatorClient();
            // 隐藏服务器地址前半部分
            var maskedUrl = MaskServerUrl(_config.CoordinatorServerUrl);
            _logger.LogInformation("[联机] 开始连接协调服务器: {Url}", maskedUrl);
            var connected = await client.ConnectAsync(_config.CoordinatorServerUrl, _ct);
            if (!connected)
            {
                _logger.LogWarning("[联机] 连接协调服务器失败，降级为单机模式");
                return;
            }
            _logger.LogInformation("[联机] 连接成功");

            // 解析白名单
            var whitelist = string.IsNullOrEmpty(_config.RoomWhitelist)
                ? null
                : new List<string>(_config.RoomWhitelist.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // 根据角色决定创建或加入房间
            var isMember = _config.MultiplayerRole == "member";

            if (isMember)
            {
                // 成员模式：根据加入方式获取房间码
                string? roomCodeToJoin = null;

                // 指定房主名为空时，自动降级为随机加入
                if (_config.MemberJoinMode != "random" && string.IsNullOrEmpty(_config.TargetHostName))
                {
                    _logger.LogWarning("[联机] 成员模式指定房主名为空，自动切换为随机加入模式");
                    _config.MemberJoinMode = "random";
                }

                if (_config.MemberJoinMode == "random")
                {
                    // 随机加入：轮询房间列表，逐个尝试有空位的房间，加入失败则换下一个
                    _logger.LogInformation("[联机] 成员模式，随机加入现有房间（超时 {T}s）...", _config.PartyTimeoutSeconds);
                    var deadline = DateTime.UtcNow.AddSeconds(_config.PartyTimeoutSeconds);
                    int attempt = 0;
                    while (DateTime.UtcNow < deadline)
                    {
                        attempt++;
                        var rooms = await client.GetOnlineRoomsAsync();
                        var candidates = rooms.Where(r => r.PlayerCount < r.MaxPlayers).ToList();
                        if (candidates.Count == 0)
                        {
                            _logger.LogInformation("[联机] 暂无可用房间，等待中...（第{N}次）", attempt);
                            await Task.Delay(5000, _ct);
                            continue;
                        }

                        bool joined = false;
                        foreach (var room in candidates)
                        {
                            _logger.LogInformation("[联机] 尝试加入房间: {Code}（房主: {Host}，{N}/{Max}人）",
                                room.Code, room.HostName, room.PlayerCount, room.MaxPlayers);
                            var ok = await client.JoinRoomAsync(room.Code, _config.PlayerName, _config.PlayerUid);
                            if (ok)
                            {
                                roomCodeToJoin = room.Code;
                                _logger.LogInformation("[联机] 成功加入房间: {Code}", room.Code);
                                joined = true;
                                break;
                            }
                            _logger.LogWarning("[联机] 加入房间 {Code} 失败（可能满员或白名单限制），尝试下一个", room.Code);
                        }

                        if (joined) break;

                        _logger.LogInformation("[联机] 本轮所有候选房间均加入失败，5秒后重试（第{N}次）", attempt);
                        await Task.Delay(5000, _ct);
                    }
                    if (roomCodeToJoin == null)
                    {
                        _logger.LogError("[联机] 等待超时（{T}s），未能加入任何可用房间，停止联机锄地", _config.PartyTimeoutSeconds);
                        await client.DisposeAsync();
                        return;
                    }
                }
                else
                {
                    // 指定房主名：轮询房间列表，找到对应房主的房间
                    var targetHost = _config.TargetHostName;
                    _logger.LogInformation("[联机] 成员模式，等待房主 [{Host}] 的房间（超时 {T}s）...", targetHost, _config.PartyTimeoutSeconds);
                    var deadline = DateTime.UtcNow.AddSeconds(_config.PartyTimeoutSeconds);
                    int attempt = 0;
                    while (DateTime.UtcNow < deadline)
                    {
                        attempt++;
                        var rooms = await client.GetOnlineRoomsAsync();
                        var target = rooms.FirstOrDefault(r =>
                            r.HostName.Equals(targetHost, StringComparison.OrdinalIgnoreCase) &&
                            r.PlayerCount < r.MaxPlayers);
                        if (target != null)
                        {
                            roomCodeToJoin = target.Code;
                            _logger.LogInformation("[联机] 找到房主 [{Host}] 的房间: {Code}", targetHost, target.Code);
                            break;
                        }
                        _logger.LogInformation("[联机] 未找到房主 [{Host}] 的房间，等待中...（第{N}次）", targetHost, attempt);
                        await Task.Delay(5000, _ct);
                    }
                    if (roomCodeToJoin == null)
                    {
                        _logger.LogError("[联机] 等待超时（{T}s），未找到房主 [{Host}] 的房间，停止联机锄地", _config.PartyTimeoutSeconds, targetHost);
                        await client.DisposeAsync();
                        return;
                    }
                }

                // 加入找到的房间（带重试）
                // 注意：随机加入模式下，JoinRoomAsync 已在轮询循环中成功调用，无需重复加入
                _config.CurrentRoomCode = roomCodeToJoin;
                bool joinedRoom = _config.MemberJoinMode == "random"; // 随机模式已在轮询中加入成功
                bool roomClosed = false;
                Action<string> roomClosedHandler = _ => roomClosed = true;
                client.RoomClosed += roomClosedHandler;
                try
                {
                    if (!joinedRoom)
                    {
                        for (int retry = 1; retry <= 10; retry++)
                        {
                            if (roomClosed) { _logger.LogWarning("[联机] 房间已被关闭，停止加入"); break; }
                            _logger.LogInformation("[联机] 尝试加入房间: {Code}（第{N}次）", roomCodeToJoin, retry);
                            joinedRoom = await client.JoinRoomAsync(roomCodeToJoin, _config.PlayerName, _config.PlayerUid);
                            if (joinedRoom) { _logger.LogInformation("[联机] 成功加入房间: {Code}", roomCodeToJoin); break; }
                            _logger.LogWarning("[联机] 加入房间 {Code} 失败，3秒后重试", roomCodeToJoin);
                            await Task.Delay(3000, _ct);
                        }
                    }
                }
                finally
                {
                    client.RoomClosed -= roomClosedHandler;
                }
                if (!joinedRoom)
                {
                    _logger.LogError("[联机] 无法加入房间 {Code}（{Reason}），停止联机锄地",
                        roomCodeToJoin, roomClosed ? "房间已关闭" : "重试耗尽");
                    await client.DisposeAsync();
                    return;
                }
                // 加入房间后立刻发心跳，确保服务器知道成员在线
                await client.SendHeartbeatAsync();
            }
            else
            {
                // 房主模式：始终创建新房间（避免重启后尝试加入已不存在的旧房间）
                if (!string.IsNullOrEmpty(_config.CurrentRoomCode))
                {
                    _logger.LogInformation("[联机] 清除旧房间码 {Code}，创建新房间", _config.CurrentRoomCode);
                    _config.CurrentRoomCode = "";
                }
                _logger.LogInformation("[联机] 无房间码，创建新房间");
                var newCode = await client.CreateRoomAsync(_config.PlayerName, whitelist, _config.PlayerUid, _config.ExpectedPlayerCount);
                if (newCode != null)
                {
                    _config.CurrentRoomCode = newCode;
                    _logger.LogInformation("[联机] 已创建房间: {Code}", newCode);
                }
                else
                {
                    _logger.LogWarning("[联机] 创建房间失败，降级为单机模式");
                    await client.DisposeAsync();
                    return;
                }
            }

            // 房主/成员标识（用于后续配置同步）
            var isHost = !isMember;
            if (_config.KazuhaPlayerIndex > 0)
            {
                await client.SetKazuhaPlayerAsync(_config.KazuhaPlayerIndex);
                _logger.LogInformation("[联机] 已设置万叶玩家序号: {Idx}", _config.KazuhaPlayerIndex);
            }

            // 配置同步：房主上传，成员拉取
            if (isHost)
            {
                var roomConfig = new Multiplayer.Models.RoomConfig
                {
                    SyncTimeoutSeconds = _config.SyncTimeoutSeconds,
                    MinPlayersToSync = _config.MinPlayersToSync,
                    SyncPointMinDistance = _config.SyncPointMinDistance,
                    StartRouteIndex = _config.StartRouteIndex,
                    UseFixedDebugRoutes = _config.UseFixedDebugRoutes,
                    FixedDebugRoutePath = _config.FixedDebugRoutePath,
                    DebugMode = _config.DebugMode,
                    ReturnToFightPointAfterBattle = _config.ReturnToFightPointAfterBattle,
                    ReturnToFightPointStaySeconds = _config.ReturnToFightPointStaySeconds,
                    KazuhaPlayerIndex = _config.KazuhaPlayerIndex,
                    PartyTimeoutSeconds = _config.PartyTimeoutSeconds,
                    MultiWorldEnabled = _config.MultiWorldEnabled,
                    MultiWorldCount = _config.MultiWorldCount,
                    SelectedBuiltinRoute = _config.SelectedBuiltinRoute,
                    FightTimeoutSeconds = _config.FightTimeoutSeconds,
                    FightExtraWaitSeconds = _config.FightExtraWaitSeconds,
                    RejoinMaxWaitSeconds = _config.RejoinMaxWaitSeconds,
                };
                
                // 房主上传配置，带重试机制（最多3次）
                bool uploadSuccess = false;
                for (int retry = 1; retry <= 3; retry++)
                {
                    try
                    {
                        await client.SetRoomConfigAsync(roomConfig);
                        uploadSuccess = true;
                        _logger.LogInformation("[联机] 房主配置已上传到服务器（第{N}次尝试）", retry);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (retry < 3)
                        {
                            _logger.LogWarning(ex, "[联机] 上传房主配置失败（第{N}次尝试），2秒后重试", retry);
                            await Task.Delay(2000, _ct);
                        }
                        else
                        {
                            _logger.LogError(ex, "[联机] 上传房主配置失败（重试3次后仍失败）");
                        }
                    }
                }
                
                // 多世界模式下配置上传失败是严重错误，必须终止
                if (!uploadSuccess && _config.MultiWorldEnabled)
                {
                    _logger.LogError("[联机] 多世界模式：房主配置上传失败，终止多世界模式");
                    await client.DisposeAsync();
                    _multiplayerCoordinator = null;
                    return;
                }
                
                // 多世界模式：保存第一任房主的配置
                if (_config.MultiWorldEnabled && uploadSuccess)
                {
                    _firstHostConfig = roomConfig;
                    _logger.LogInformation("[联机] 多世界模式：已锁定第一任房主配置");
                }
            }

            var barrier = new SyncBarrier(client, _config.SyncTimeoutSeconds);
            var resolver = new SyncPointResolver();
            _multiplayerCoordinator = new MultiplayerCoordinator(client, barrier, resolver, _config.MinPlayersToSync, _config.SyncTimeoutSeconds);

            _logger.LogInformation("[联机] MultiplayerCoordinator 初始化完成，超时={Timeout}s，最低人数={Min}", _config.SyncTimeoutSeconds, _config.MinPlayersToSync);

            // 联机模式：设置战斗超时覆盖值（不修改原始配置，通过 PathingConditionConfig 传递给 AutoFightHandler）
            PathingConditionConfig.MultiplayerFightTimeoutOverride = _config.FightTimeoutSeconds;

            // 打印所有房主同步的参数
            _logger.LogInformation("[联机] ===== 当前联机参数（房主同步）=====");
            _logger.LogInformation("[联机] 集合点超时={SyncTimeout}s，最低同步人数={MinPlayers}，集合点最小距离={MinDist}",
                _config.SyncTimeoutSeconds, _config.MinPlayersToSync, _config.SyncPointMinDistance);
            _logger.LogInformation("[联机] 战斗超时={FightTimeout}s，万叶玩家序号={Kazuha}，从第{Start}条路线开始",
                _config.FightTimeoutSeconds, _config.KazuhaPlayerIndex, _config.StartRouteIndex);
            _logger.LogInformation("[联机] 战斗后走回={ReturnFight}（停留{Stay}s），调试模式={Debug}",
                _config.ReturnToFightPointAfterBattle, _config.ReturnToFightPointStaySeconds, _config.DebugMode);
            _logger.LogInformation("[联机] 组队超时={PartyTimeout}s，多世界={MultiWorld}（{Rounds}轮），内置线路={Route}",
                _config.PartyTimeoutSeconds, _config.MultiWorldEnabled, _config.MultiWorldCount, _config.SelectedBuiltinRoute);
            _logger.LogInformation("[联机] 战斗额外等待={FightExtra}s，重新加入最大等待={RejoinMax}s，传送必同步（默认）",
                _config.FightExtraWaitSeconds, _config.RejoinMaxWaitSeconds);
            _logger.LogInformation("[联机] =====================================");

            _multiplayerCoordinator.OnDegraded += reason =>
            {
                _logger.LogWarning("[联机] 已降级为单机模式，原因：{Reason}", reason);
            };

            // 连续超时退出处理（需求 5）
            _multiplayerCoordinator.OnConsecutiveSyncTimeoutExceeded += async (isHost) =>
            {
                if (isHost)
                {
                    _logger.LogError("[联机] 房主连续超时达到上限，广播关闭房间并停止");
                    try { await _coordinatorClientRef!.CloseRoomAsync(); } catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[联机] 广播关闭房间失败（静默忽略，成员靠心跳超时感知）");
                    }
                }
                else
                {
                    _logger.LogError("[联机] 成员连续超时达到上限，上报 Offline 并退出");
                    try { await _coordinatorClientRef!.ReportMemberStatusAsync(MemberStatus.Offline); } catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[联机] 上报 Offline 失败（静默忽略）");
                    }
                }
                // 降级停止后续同步等待
                _multiplayerCoordinator?.Degrade("连续超时达到上限");
            };

            // 路线一致性验证（延迟到路线筛选后执行，只验证本次要跑的路线）
            _consistencyChecker = new RouteConsistencyChecker();
            _coordinatorClientRef = client;

            // 启动世界状态监测（需求 2）
            _worldStateMonitor = new WorldStateMonitor(client, _config.PlayerUid, _ct);
            client.WorldStateMonitor = _worldStateMonitor;
            _worldStateMonitor.OnExitConfirmed += async (isHost, reason) =>
            {
                _stopReason = reason;
                if (!isHost) _sessionTerminated = true;
                // 直接 cancel linkedStopCts，确保 _ct 被取消（不依赖 TriggerCoordinatedStop 的 _stopCts）
                try { _linkedStopCts?.Cancel(); }
                catch (ObjectDisposedException) { }
                catch { }
                await _multiplayerCoordinator!.TriggerCoordinatedStop(isHost, reason);
            };
            _worldStateMonitor.OnDroppedFromRoom += async () =>
            {
                _stopReason = "掉出房间且重试失败";
                if (!_multiplayerCoordinator!.IsHost) _sessionTerminated = true;
                try { _linkedStopCts?.Cancel(); }
                catch (ObjectDisposedException) { }
                catch { }
                var isHost = _multiplayerCoordinator!.IsHost;
                await _multiplayerCoordinator.TriggerCoordinatedStop(isHost, "掉出房间且重试失败");
            };
            // 创建 linked CTS，协调停止时通过 Cancel 传播停止信号（需求 2.1）
            _linkedStopCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
            _multiplayerCoordinator.StopCts = _linkedStopCts;
            // 关键：将 _ct 替换为 linked token，这样 _linkedStopCts.Cancel() 能取消所有使用 _ct 的操作
            _ct = _linkedStopCts.Token;

            // 成员侧：监听 RoomClosed 事件，设置 _sessionTerminated 确保多世界模式不继续下一轮
            // 房主多世界轮次切换会主动调用 CloseRoomAsync，会触发 SignalR 回环 RoomClosed 广播；
            // 通过 ConsumeSelfClosingRoomFlag 识别为自触发并跳过取消，避免误终止后续轮次。
            // 多世界轮次切换期间（成员尚未离开旧 Group），旧房主关旧房间也会广播 RoomClosed，
            // 必须用 WorldStateMonitor.IsRoundSwitching 抑制，否则成员会被误终止整个任务。
            client.RoomClosed += reason =>
            {
                if (client.ConsumeSelfClosingRoomFlag())
                {
                    _logger.LogInformation("[联机] 收到 RoomClosed（自触发关房）: {Reason}，不取消任务（多世界轮次切换）", reason);
                    return;
                }
                if (_worldStateMonitor != null && _worldStateMonitor.IsRoundSwitching)
                {
                    _logger.LogInformation("[联机] 收到 RoomClosed（轮次切换中）: {Reason}，不取消任务（旧房间关闭广播）", reason);
                    return;
                }
                _stopReason = $"房间已关闭: {reason}";
                _sessionTerminated = true;
                try { _linkedStopCts?.Cancel(); }
                catch (ObjectDisposedException) { }
                catch { }
            };

            _worldStateMonitor.Start();
            _logger.LogInformation("[联机] 路线一致性验证将在路线筛选后执行");

            // 自动组队：游戏内加入/等待
            _worldStateMonitor.IsPartyPhase = true;
            var autoParty = new AutoPartyTask();
            if (isHost)
            {
                // 上报房主就绪，通知成员可以开始申请
                await client.ReportHostReadyAsync();
                var hotkeyHint = string.IsNullOrEmpty(TaskContext.Instance().Config.HotKeyConfig.SkipPartyWaitHotkey)
                    ? "（可在快捷键设置中配置快捷键）"
                    : $"（可按快捷键 {TaskContext.Instance().Config.HotKeyConfig.SkipPartyWaitHotkey} 立即开始）";
                _logger.LogInformation("[联机] 当前为房主，已上报就绪，等待成员加入世界 {Hint}", hotkeyHint);
                var partyWhitelist = string.IsNullOrEmpty(_config.RoomWhitelist)
                    ? null
                    : _config.RoomWhitelist.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var actualCount = await autoParty.WaitForMembersAsync(
                    _config.ExpectedPlayerCount, partyWhitelist, client, _config.PartyTimeoutSeconds, _ct);

                if (actualCount < 0)
                {
                    // 初始化失败（未检测到主界面等）
                    _logger.LogError("[联机] 自动组队初始化失败，停止联机锄地");
                    _worldStateMonitor.IsPartyPhase = false;
                    await client.DisposeAsync();
                    _multiplayerCoordinator = null;
                    return;
                }
                else if (actualCount == 0)
                {
                    // 超时
                    if (_config.PartyTimeoutAction == 1)
                    {
                        // 现有人数锄地
                        var currentCount = client.CurrentRoomPlayerCount;
                        _logger.LogWarning("[联机] 组队超时，以当前 {N} 人开始锄地", currentCount);
                        _config.ExpectedPlayerCount = currentCount > 0 ? currentCount : 1;
                        _config.MinPlayersToSync = _config.ExpectedPlayerCount;
                    }
                    else
                    {
                        _logger.LogError("[联机] 组队超时，停止联机锄地");
                        _worldStateMonitor.IsPartyPhase = false;
                        await client.DisposeAsync();
                        _multiplayerCoordinator = null;
                        return;
                    }
                }
                else if (actualCount < _config.ExpectedPlayerCount)
                {
                    // 房主手动跳过（ESC），以实际人数开始
                    _logger.LogInformation("[联机] 房主跳过等待，以实际 {N} 人开始锄地", actualCount);
                    _config.ExpectedPlayerCount = actualCount;
                    _config.MinPlayersToSync = actualCount;
                }
                else
                {
                    _logger.LogInformation("[联机] 人齐，共 {N} 人，开始锄地", actualCount);
                }

                // 房主也上报 WorldJoined，触发 AllWorldJoined 广播给成员
                // 先注册监听再上报，避免信号在上报和等待之间丢失
                var hostAllJoinedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Action hostAllJoinedHandler = () => hostAllJoinedTcs.TrySetResult(true);
                client.AllWorldJoined += hostAllJoinedHandler;
                try
                {
                    await client.ReportWorldJoinedAsync();
                    _logger.LogInformation("[联机] 房主已上报就绪，等待所有成员就绪...");

                    using var allJoinedCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.PartyTimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_ct, allJoinedCts.Token);
                    using var reg = linkedCts.Token.Register(() => hostAllJoinedTcs.TrySetResult(false));
                    var allJoined = await hostAllJoinedTcs.Task;
                    if (allJoined)
                        _logger.LogInformation("[联机] 所有成员已就绪，开始锄地");
                    else
                        _logger.LogWarning("[联机] 等待所有成员就绪超时，继续执行");
                }
                finally
                {
                    client.AllWorldJoined -= hostAllJoinedHandler;
                }
                _worldStateMonitor.IsPartyPhase = false;
            }
            else
            {
                // 从服务器获取房主 UID（PlayerList 第一个玩家）
                // 等待 PlayerListUpdated 到达（最多 10 秒超时）
                if (string.IsNullOrEmpty(client.HostPlayerUid))
                {
                    _logger.LogInformation("[联机] 等待 PlayerListUpdated 事件到达...");
                    var playerListTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action<List<PlayerInfo>> playerListHandler = _ => playerListTcs.TrySetResult(true);
                    client.PlayerListUpdated += playerListHandler;
                    try
                    {
                        using var playerListCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_ct, playerListCts.Token);
                        using var reg = linkedCts.Token.Register(() => playerListTcs.TrySetResult(false));
                        
                        var received = await playerListTcs.Task;
                        if (!received)
                        {
                            _logger.LogWarning("[联机] 等待 PlayerListUpdated 超时，HostPlayerUid 可能为空");
                        }
                        else
                        {
                            _logger.LogInformation("[联机] PlayerListUpdated 已到达");
                        }
                    }
                    finally
                    {
                        client.PlayerListUpdated -= playerListHandler;
                    }
                }
                
                var hostUid = client.HostPlayerUid;
                if (string.IsNullOrEmpty(hostUid))
                {
                    _logger.LogWarning("[联机] 无法获取房主 UID，跳过自动组队");
                }
                else
                {
                    // 等待房主就绪后再申请加入
                    _logger.LogInformation("[联机] 等待房主进入就绪状态...");
                    var hostReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action<bool> readyHandler = ready => { if (ready) hostReadyTcs.TrySetResult(true); };
                    client.HostReadyChanged += readyHandler;
                    try
                    {
                        // 先查一次当前状态
                        if (await client.IsHostReadyAsync())
                        {
                            _logger.LogInformation("[联机] 房主已就绪");
                        }
                        else
                        {
                            using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.PartyTimeoutSeconds));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_ct, readyCts.Token);
                            using var reg = linkedCts.Token.Register(() => hostReadyTcs.TrySetResult(false));

                            // 同时启动轮询，防止事件推送丢失（服务器可能不持久化就绪状态）
                            _ = Task.Run(async () =>
                            {
                                while (!hostReadyTcs.Task.IsCompleted)
                                {
                                    try
                                    {
                                        await Task.Delay(3000, linkedCts.Token);
                                        if (await client.IsHostReadyAsync())
                                            hostReadyTcs.TrySetResult(true);
                                    }
                                    catch { break; }
                                }
                            }, linkedCts.Token);

                            var ready = await hostReadyTcs.Task;
                            if (!ready)
                            {
                                _logger.LogError("[联机] 等待房主就绪超时，停止联机锄地");
                                _worldStateMonitor.IsPartyPhase = false;
                                await client.DisposeAsync();
                                _multiplayerCoordinator = null;
                                return;
                            }
                            _logger.LogInformation("[联机] 房主已就绪");
                        }
                    }
                    finally
                    {
                        client.HostReadyChanged -= readyHandler;
                    }

                    // 房主就绪后拉取配置（确保房主已上传配置）
                    RoomConfig? hostConfig = null;
                    for (int retry = 1; retry <= 3; retry++)
                    {
                        hostConfig = await client.GetRoomConfigAsync();
                        if (hostConfig != null)
                        {
                            _logger.LogInformation("[联机] 成功拉取房主配置（第{N}次尝试）", retry);
                            break;
                        }
                        if (retry < 3)
                        {
                            _logger.LogWarning("[联机] 拉取房主配置失败（第{N}次尝试），2秒后重试", retry);
                            await Task.Delay(2000, _ct);
                        }
                    }

                    if (hostConfig != null)
                    {
                        _config.SyncTimeoutSeconds = hostConfig.SyncTimeoutSeconds;
                        _config.MinPlayersToSync = hostConfig.MinPlayersToSync;
                        _config.SyncPointMinDistance = hostConfig.SyncPointMinDistance;
                        _config.StartRouteIndex = hostConfig.StartRouteIndex;
                        _config.UseFixedDebugRoutes = hostConfig.UseFixedDebugRoutes;
                        _config.FixedDebugRoutePath = hostConfig.FixedDebugRoutePath;
                        _config.DebugMode = hostConfig.DebugMode;
                        _config.ReturnToFightPointAfterBattle = hostConfig.ReturnToFightPointAfterBattle;
                        _config.ReturnToFightPointStaySeconds = hostConfig.ReturnToFightPointStaySeconds;
                        _config.KazuhaPlayerIndex = hostConfig.KazuhaPlayerIndex;
                        _config.PartyTimeoutSeconds = hostConfig.PartyTimeoutSeconds;
                        _config.MultiWorldEnabled = hostConfig.MultiWorldEnabled;
                        _config.MultiWorldCount = hostConfig.MultiWorldCount;
                        _config.SelectedBuiltinRoute = hostConfig.SelectedBuiltinRoute;
                        _config.FightExtraWaitSeconds = hostConfig.FightExtraWaitSeconds;
                        _config.RejoinMaxWaitSeconds = hostConfig.RejoinMaxWaitSeconds;

                        // 联机模式：设置战斗超时覆盖值（不修改原始配置）
                        PathingConditionConfig.MultiplayerFightTimeoutOverride = hostConfig.FightTimeoutSeconds;

                        // 多世界模式：保存第一任房主的配置
                        if (_config.MultiWorldEnabled)
                        {
                            _firstHostConfig = hostConfig;
                            _logger.LogInformation("[联机] 多世界模式：已保存第一任房主配置");
                        }

                        _logger.LogInformation("[联机] 已同步房主配置：超时={Timeout}s，最低人数={Min}，最小距离={Dist}",
                            hostConfig.SyncTimeoutSeconds, hostConfig.MinPlayersToSync, hostConfig.SyncPointMinDistance);
                    }
                    else
                    {
                        // 多世界模式下配置同步失败是严重错误，必须终止
                        if (_config.MultiWorldEnabled)
                        {
                            _logger.LogError("[联机] 多世界模式：无法获取房主配置（重试3次后仍失败），终止多世界模式");
                            _worldStateMonitor.IsPartyPhase = false;
                            await client.DisposeAsync();
                            _multiplayerCoordinator = null;
                            return;
                        }
                        _logger.LogWarning("[联机] 无法获取房主配置，使用本地配置");
                    }

                    _logger.LogInformation("[联机] 当前为成员，尝试加入房主世界，房主 UID: {Uid}", AutoPartyTask.MaskUid(hostUid));
                    var joinOk = await autoParty.JoinHostWorldAsync(hostUid, _ct);
                    if (joinOk)
                    {
                        _logger.LogInformation("[联机] 已进入房主世界，等待所有人就绪...");

                        // 先注册 AllWorldJoined 监听，再上报，避免信号在上报和等待之间丢失
                        var waitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        Action handler = () => waitTcs.TrySetResult(true);
                        client.AllWorldJoined += handler;
                        try
                        {
                            await client.ReportWorldJoinedAsync();

                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.PartyTimeoutSeconds));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_ct, timeoutCts.Token);
                            using var reg = linkedCts.Token.Register(() =>
                            {
                                if (_ct.IsCancellationRequested)
                                    waitTcs.TrySetCanceled(_ct);
                                else
                                    waitTcs.TrySetResult(false);
                            });

                            var allReady = await waitTcs.Task;
                            if (allReady)
                            {
                                _logger.LogInformation("[联机] 所有人已就绪，开始锄地");
                            }
                            else
                            {
                                _logger.LogWarning("[联机] 等待组队超时，退出房间，结束任务");
                                _worldStateMonitor.IsPartyPhase = false;
                                _multiplayerCoordinator?.Degrade("组队超时");
                                return;
                            }
                        }
                        finally
                        {
                            client.AllWorldJoined -= handler;
                        }
                        _worldStateMonitor.IsPartyPhase = false;
                    }
                    else
                    {
                        _logger.LogError("[联机] 加入房主世界失败，停止联机锄地");
                        _worldStateMonitor.IsPartyPhase = false;
                        _multiplayerCoordinator?.Degrade("加入房主世界失败");
                        return;
                    }
                }
            }

            // 注入到执行引擎
            _executionEngine?.SetCoordinator(_multiplayerCoordinator);
            _executionEngine?.SetWorldStateMonitor(_worldStateMonitor);
            _logger.LogInformation("[联机] 初始化完成，房间码：{Code}", _config.CurrentRoomCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[联机] 初始化异常，降级为单机模式");
            if (_worldStateMonitor != null) _worldStateMonitor.IsPartyPhase = false;
            _multiplayerCoordinator = null;
        }
    }

    /// <summary>
    /// 联机前准备：切换队伍和角色
    /// </summary>
    private async Task PrepareMultiplayerPartyAndAvatar()
    {
        if (!_config.MultiplayerEnabled)
        {
            return;
        }
        
        _logger.LogInformation("[联机] 开始准备联机队伍和角色");

        // 0a. 检测当前是否在多人联机世界，如是则先退出
        await EnsureInOwnWorldBeforeMultiplayerAsync();

        // 0b. 设置世界权限为确认后才能加入
        await SetWorldPermissionToConfirmJoin();
        
        // 1. 切换队伍
        if (!string.IsNullOrEmpty(_config.MultiplayerPartyName))
        {
            await SwitchMultiplayerParty();
        }
        
        // 2. 切换角色
        if (!string.IsNullOrEmpty(_config.MultiplayerStartAvatarName))
        {
            await SwitchMultiplayerAvatar();
        }
        
        _logger.LogInformation("[联机] 队伍和角色准备完成");
    }

    /// <summary>
    /// 切换联机队伍（带重试）
    /// </summary>
    private async Task SwitchMultiplayerParty()
    {
        // 避免重复切换队伍
        if (_teamAlreadySwitched)
        {
            _logger.LogInformation("[联机] 队伍已切换过，跳过重复切换");
            return;
        }
        
        bool switchSuccess = false;
        
        for (int retry = 1; retry <= 5; retry++)
        {
            try
            {
                switchSuccess = await new SwitchPartyTask().Start(_config.MultiplayerPartyName, _ct);
                if (switchSuccess)
                {
                    _logger.LogInformation("[联机] 切换队伍成功（第{N}次尝试）", retry);
                    _teamAlreadySwitched = true; // 标记已切换
                    break;
                }
                
                if (retry < 5)
                {
                    _logger.LogWarning("[联机] 切换队伍失败（第{N}次尝试），2秒后重试", retry);
                    await Task.Delay(2000, _ct);
                    
                    // 第3次失败后尝试去七天神像
                    if (retry == 3)
                    {
                        _logger.LogInformation("[联机] 尝试传送到七天神像后重试");
                        try
                        {
                            await new TpTask(_ct).TpToStatueOfTheSeven();
                            await Task.Delay(1000, _ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[联机] 传送到七天神像失败，继续重试");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机] 切换队伍异常（第{N}次尝试）", retry);
                if (retry < 5)
                {
                    await Task.Delay(2000, _ct);
                }
            }
        }
        
        if (!switchSuccess)
        {
            _logger.LogWarning("[联机] 切换队伍失败（重试5次后仍失败），将使用当前队伍继续联机");
        }
        else
        {
            await Task.Delay(500, _ct); // 等待队伍切换完成
        }
    }

    /// <summary>
    /// 切换联机角色（带重试）
    /// </summary>
    private async Task SwitchMultiplayerAvatar()
    {
        bool avatarSwitchSuccess = false;
        
        for (int retry = 1; retry <= 3; retry++)
        {
            try
            {
                // 获取当前战斗场景
                using var ra = CaptureToRectArea();
                var combatScenes = new CombatScenes().InitializeTeam(ra);
                
                if (combatScenes.CheckTeamInitialized())
                {
                    // 通过角色名称查找角色
                    var targetAvatar = combatScenes.SelectAvatar(_config.MultiplayerStartAvatarName);
                    
                    if (targetAvatar != null)
                    {
                        // 尝试切换到该角色
                        if (targetAvatar.TrySwitch(10))
                        {
                            avatarSwitchSuccess = true;
                            _logger.LogInformation("[联机] 切换到角色[{Name}]成功（第{R}次尝试）", 
                                _config.MultiplayerStartAvatarName, retry);
                            break;
                        }
                    }
                    else
                    {
                        // 当前队伍没有该角色，切换到1号位
                        _logger.LogWarning("[联机] 当前队伍没有角色[{Name}]，切换到1号位角色", 
                            _config.MultiplayerStartAvatarName);
                        var firstAvatar = combatScenes.SelectAvatar(1);
                        if (firstAvatar.TrySwitch(10))
                        {
                            avatarSwitchSuccess = true;
                            _logger.LogInformation("[联机] 切换到1号位角色[{Name}]成功", firstAvatar.Name);
                            break;
                        }
                    }
                }
                
                if (retry < 3)
                {
                    _logger.LogWarning("[联机] 切换角色失败（第{N}次尝试），1秒后重试", retry);
                    await Task.Delay(1000, _ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机] 切换角色异常（第{N}次尝试）", retry);
                if (retry < 3)
                {
                    await Task.Delay(1000, _ct);
                }
            }
        }
        
        if (!avatarSwitchSuccess)
        {
            _logger.LogWarning("[联机] 切换角色失败（重试3次后仍失败），将使用当前角色继续联机");
        }
    }

    private async Task RunTask()
    {
        // 1. 加载配置
        var accountName = string.IsNullOrEmpty(_config.AccountName) ? "默认账户" : _config.AccountName;
        LoadGroupSettings(accountName);

        // 2. 解析时间限制
        _timeChecker.ParseRestrictions(_config.NoRunPeriod);

        // 3. 加载怪物信息
        var monsterInfoPath = Path.Combine(_dataDir, "assets", "monsterInfo.json");
        _monsterRepo.Load(monsterInfoPath);

        // 3.5 初始化并发服务
        var assetsDir = Path.Combine(_dataDir, "assets");
        _pickupService.LoadTemplates(assetsDir, _config.PickupMode);
        _anomalyDetector.LoadTemplates(assetsDir);
        _blacklistManager.Load(_dataDir, accountName);
        _blacklistManager.LoadItemFullTemplate(assetsDir);
        _cookingService.LoadTemplates(assetsDir);
        _executionEngine = new RouteExecutionEngine(
            _pickupService, _anomalyDetector, _dumperService, _blacklistManager, _config, _partyConfig);

        // 联机模式初始化
        if (_config.MultiplayerEnabled)
        {
            // 联机模式下禁用自动领取派遣，避免打断锄地流程
            // （_partyConfig 为 null 时由 RouteExecutionEngine 在每条路线执行前处理）
            if (_partyConfig != null)
                _partyConfig.DisableAutoFetchDispatch = true;

            // 联机前准备：切换队伍和角色
            await PrepareMultiplayerPartyAndAvatar();
            
            await InitializeMultiplayerAsync();

            // 多世界模式：循环执行多轮
            if (_config.MultiWorldEnabled && _coordinatorClientRef != null)
            {
                // 在进入多世界循环前先加载 CD 记录（单世界路径在下面加载，多世界需要在这里加载）
                _cdManager.Load(_dataDir, accountName);
                await RunMultiWorldAsync(accountName);
                return;
            }
        }

        // 4. 加载CD记录
        _cdManager.Load(_dataDir, accountName);

        // 5. 构建分组标签
        var groupTags = BuildGroupTags();

        await RunSingleWorldAsync(accountName, groupTags);

        // 联机模式任务结束：成员退回自己的世界
        if (_config.MultiplayerEnabled && _coordinatorClientRef != null)
        {
            var isMember = _config.MultiplayerRole == "member";
            if (isMember)
            {
                _logger.LogInformation("[联机] 任务结束，成员退回自己的世界");
                try
                {
                    var autoParty = new AutoPartyTask();
                    var left = await autoParty.LeaveWorldAsync(_ct);
                    if (!left)
                        _logger.LogWarning("[联机] 退出世界未确认成功");
                }
                catch (Exception ex) { _logger.LogWarning(ex, "[联机] 退出世界失败，忽略"); }
            }
        }
    }

    /// <summary>
    /// 多世界连续锄地：按玩家列表顺序，每轮换一个房主，共 MultiWorldCount 轮
    /// </summary>
    private async Task RunMultiWorldAsync(string accountName)
    {
        var client = _coordinatorClientRef!;

        // 直接使用 client 缓存的玩家列表（在组队完成时已由服务器推送并缓存）
        // 立即快照并锁定顺序，后续掉线不影响轮换序列
        var playerOrder = new List<PlayerInfo>(client.CurrentPlayerList);

        if (playerOrder.Count == 0)
        {
            _logger.LogWarning("[多世界] 玩家列表为空，仅执行第1轮");
            await RunSingleWorldCoreAsync(accountName);
            return;
        }

        // 轮数 = min(配置轮数, 实际玩家数)，超出玩家数的轮数没有意义
        var totalRounds = Math.Min(_config.MultiWorldCount, playerOrder.Count);
        if (totalRounds < _config.MultiWorldCount)
            _logger.LogWarning("[多世界] 配置轮数({Cfg})超过实际玩家数({N})，实际执行 {Total} 轮",
                _config.MultiWorldCount, playerOrder.Count, totalRounds);

        _logger.LogInformation("[多世界] 共 {Total} 轮，轮换顺序: {Players}",
            totalRounds, string.Join(" → ", playerOrder.Select(p => p.PlayerName)));

        bool lastRoundAmIHost = false;
        for (int round = 0; round < totalRounds; round++)
        {
            if (_sessionTerminated || _multiplayerCoordinator?.IsExitTriggered == true)
            {
                _logger.LogWarning("[多世界] 会话已终止（sessionTerminated={ST}, exitTriggered={ET}），跳过剩余轮次",
                    _sessionTerminated, _multiplayerCoordinator?.IsExitTriggered);
                break;
            }

            _ct.ThrowIfCancellationRequested();

            var roundHostPlayer = playerOrder[round];
            // 身份判断优先级：UID > 名称 > 列表位置（都为空时用位置，第0个是第1轮房主）
            bool amIHost;
            if (!string.IsNullOrEmpty(_config.PlayerUid))
                amIHost = roundHostPlayer.PlayerUid == _config.PlayerUid;
            else if (!string.IsNullOrEmpty(_config.PlayerName))
                amIHost = roundHostPlayer.PlayerName == _config.PlayerName;
            else
            {
                // UID 和名称都未填，无法可靠判断，降级：只有第1轮的第1个玩家是房主
                amIHost = round == 0 && _config.MultiplayerRole == "host";
                _logger.LogWarning("[多世界] 玩家 UID 和名称均未填写，身份判断可能不准确，建议填写 UID");
            }
            lastRoundAmIHost = amIHost;

            _logger.LogInformation("[多世界] 第 {Round}/{Total} 轮，房主: {Host}，我是{Role}",
                round + 1, totalRounds, roundHostPlayer.PlayerName, amIHost ? "房主" : "成员");

            if (round > 0)
            {
                // 注意：第 1 轮结束后房主和成员都已通过 LeaveCurrentWorldAsync 主动回到自己世界，
                // 因此本轮开始时 IsInMultiGame 必然为 false，不能在此处用"是否在联机世界"做兜底检测。
                // 成员被踢/掉线的异常场景由 WorldStateMonitor.OnDroppedFromRoom 和 RoomClosed 事件兜底。

                // 非第一轮：需要重新组队进入新房主的世界，并重新加载 CD
                _worldStateMonitor?.BeginRoundSwitch();
                await SetupNextRoundAsync(round, roundHostPlayer, amIHost, client, playerOrder.Count);
                _worldStateMonitor?.EndRoundSwitch();
                _multiplayerCoordinator?.ResetForNewRound();
                if (_multiplayerCoordinator == null)
                {
                    _logger.LogError("[多世界] 第 {Round} 轮初始化失败，停止后续轮次", round + 1);
                    // 尝试回到自己世界，避免卡在别人世界
                    try { await LeaveCurrentWorldAsync(amIHost, client); }
                    catch (Exception ex) { _logger.LogWarning(ex, "[多世界] 离开世界失败，忽略"); }
                    break;
                }
                _cdManager.Load(_dataDir, accountName);
            }

            // 每轮开始前重新准备队伍和角色
            await PrepareMultiplayerPartyAndAvatar();
            
            // 执行本轮锄地
            var groupTags = BuildGroupTags();
            await RunSingleWorldCoreAsync(accountName, groupTags);

            // 本轮结束：全员同步后离开世界（最后一轮不需要离开）
            if (round < totalRounds - 1)
            {
                _logger.LogInformation("[多世界] 第 {Round} 轮锄地完成，等待全员同步", round + 1);
                // 轮次切换期间忽略 WorldStateMonitor 的 IsInMultiGame 检测（需求 2）
                _worldStateMonitor?.BeginRoundSwitch();
                await SyncRoundEndAsync(round, client);

                // 离开世界：成员先主动离开，房主等待后再关闭房间
                // 这样避免房主关闭房间时成员还在执行 LeaveWorldAsync 的竞态
                await LeaveCurrentWorldAsync(amIHost, client);

                // 清理本轮的 coordinator，下一轮重新建
                if (_multiplayerCoordinator != null)
                {
                    _multiplayerCoordinator.OnDegraded -= _ => { };
                    _multiplayerCoordinator = null;
                }
                _executionEngine?.SetCoordinator(null);
                _executionEngine?.SetWorldStateMonitor(null);
            }
        }

        _logger.LogInformation("[多世界] 全部 {Total} 轮锄地完成", totalRounds);

        // 任务结束：所有人退回自己的世界
        _logger.LogInformation("[多世界] 任务结束，退回自己的世界");
        try { await LeaveCurrentWorldAsync(lastRoundAmIHost, client); }
        catch (Exception ex) { _logger.LogWarning(ex, "[多世界] 任务结束退出世界失败，忽略"); }
    }

    /// <summary>
    /// 设置下一轮：新房主创建房间，成员加入
    /// </summary>
    private async Task SetupNextRoundAsync(int round, PlayerInfo roundHostPlayer, bool amIHost,
        CoordinatorClient client, int playerCount)
    {
        try
        {
            var autoParty = new AutoPartyTask();
            var whitelist = string.IsNullOrEmpty(_config.RoomWhitelist)
                ? null
                : _config.RoomWhitelist.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // 多世界模式下，确保所有参与玩家都在白名单中（避免轮换后原房主无法加入新房间）
            List<string>? multiWorldWhitelist = null;
            if (whitelist != null)
            {
                var allPlayerNames = client.CurrentPlayerList.Select(p => p.PlayerName).Where(n => !string.IsNullOrEmpty(n));
                multiWorldWhitelist = whitelist.Union(allPlayerNames).ToList();
                _logger.LogInformation("[多世界] 第 {Round} 轮白名单（含所有参与玩家）: {List}", round + 1, string.Join(", ", multiWorldWhitelist));
            }

            if (amIHost)
            {
                // 我是本轮房主：创建新房间，带重试机制（最多3次）
                _logger.LogInformation("[多世界] 第 {Round} 轮我是房主，创建新房间", round + 1);
                string? newCode = null;
                for (int retry = 1; retry <= 3; retry++)
                {
                    newCode = await client.CreateRoomAsync(_config.PlayerName, multiWorldWhitelist?.ToList() ?? whitelist?.ToList(), _config.PlayerUid, playerCount);
                    if (newCode != null)
                    {
                        _logger.LogInformation("[多世界] 第 {Round} 轮创建房间成功（第{N}次尝试）: {Code}", round + 1, retry, newCode);
                        break;
                    }
                    if (retry < 3)
                    {
                        _logger.LogWarning("[多世界] 第 {Round} 轮创建房间失败（第{N}次尝试），2秒后重试", round + 1, retry);
                        await Task.Delay(2000, _ct);
                    }
                }
                
                if (newCode == null)
                {
                    _logger.LogError("[多世界] 第 {Round} 轮创建房间失败（重试3次后仍失败），终止多世界模式", round + 1);
                    return;
                }
                _config.CurrentRoomCode = newCode;
                _logger.LogInformation("[多世界] 新房间码: {Code}", newCode);

                // 重置 WorldJoinedSet，确保新轮次独立计数
                await client.ResetWorldJoinedAsync();
                _logger.LogInformation("[多世界] 第 {Round} 轮 WorldJoinedSet 已重置", round + 1);

                // 重置多轮世界等待点状态（skip-route-wait-point-report 修复）
                await client.ResetForNewWorldRoundAsync(round + 1);
                _logger.LogInformation("[多世界] 第 {Round} 轮等待点状态已重置", round + 1);

                // 上传配置：使用第一任房主的配置（已在第1轮保存）
                if (_firstHostConfig != null)
                {
                    // 房主上传配置，带重试机制（最多3次）
                    bool uploadSuccess = false;
                    for (int retry = 1; retry <= 3; retry++)
                    {
                        try
                        {
                            await client.SetRoomConfigAsync(_firstHostConfig);
                            uploadSuccess = true;
                            _logger.LogInformation("[多世界] 第 {Round} 轮已上传第一任房主的配置（第{N}次尝试）", round + 1, retry);
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (retry < 3)
                            {
                                _logger.LogWarning(ex, "[多世界] 第 {Round} 轮上传配置失败（第{N}次尝试），2秒后重试", round + 1, retry);
                                await Task.Delay(2000, _ct);
                            }
                            else
                            {
                                _logger.LogError(ex, "[多世界] 第 {Round} 轮上传配置失败（重试3次后仍失败），终止多世界模式", round + 1);
                            }
                        }
                    }
                    
                    if (!uploadSuccess)
                    {
                        _multiplayerCoordinator = null;
                        return;
                    }
                }
                else
                {
                    // 多世界模式下_firstHostConfig为null是严重错误，必须终止
                    _logger.LogError("[多世界] 第 {Round} 轮房主配置丢失（_firstHostConfig为null），这表明第1轮配置保存失败，终止多世界模式", round + 1);
                    _multiplayerCoordinator = null;
                    return;
                }

                await client.ReportHostReadyAsync();
                var actualCount = await autoParty.WaitForMembersAsync(
                    playerCount, whitelist, client, _config.PartyTimeoutSeconds, _ct);

                if (actualCount <= 0)
                {
                    _logger.LogError("[多世界] 第 {Round} 轮等待成员超时", round + 1);
                    return;
                }

                // 先注册监听再上报，避免信号在上报和等待之间丢失
                var hostAllJoinedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Action hostAllJoinedHandler = () => hostAllJoinedTcs.TrySetResult(true);
                client.AllWorldJoined += hostAllJoinedHandler;
                try
                {
                    await client.ReportWorldJoinedAsync();
                    _logger.LogInformation("[多世界] 第 {Round} 轮房主已上报就绪，等待所有成员就绪...", round + 1);

                    using var allJoinedCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.PartyTimeoutSeconds));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_ct, allJoinedCts.Token);
                    using var reg = linkedCts.Token.Register(() => hostAllJoinedTcs.TrySetResult(false));
                    var allJoined = await hostAllJoinedTcs.Task;
                    if (allJoined)
                        _logger.LogInformation("[多世界] 第 {Round} 轮所有成员已就绪", round + 1);
                    else
                        _logger.LogWarning("[多世界] 第 {Round} 轮等待所有成员就绪超时，继续执行", round + 1);
                }
                finally
                {
                    client.AllWorldJoined -= hostAllJoinedHandler;
                }
            }
            else
            {
                // 我是成员：先找到新房主的房间并加入，再等待就绪
                _logger.LogInformation("[多世界] 第 {Round} 轮我是成员，等待房主 [{Host}] 创建房间",
                    round + 1, roundHostPlayer.PlayerName);

                // 轮询找到新房主的房间并加入
                string? newRoomCode = null;
                var deadline = DateTime.UtcNow.AddSeconds(_config.PartyTimeoutSeconds);
                int attempt = 0;
                while (DateTime.UtcNow < deadline)
                {
                    attempt++;
                    var rooms = await client.GetOnlineRoomsAsync();
                    var target = rooms.FirstOrDefault(r =>
                        (!string.IsNullOrEmpty(roundHostPlayer.PlayerUid) && r.HostUid == roundHostPlayer.PlayerUid) ||
                        r.HostName.Equals(roundHostPlayer.PlayerName, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        _logger.LogInformation("[多世界] 找到新房主 [{Host}] 的房间: {Code}", roundHostPlayer.PlayerName, target.Code);
                        var joined = await client.JoinRoomAsync(target.Code, _config.PlayerName, _config.PlayerUid);
                        if (joined)
                        {
                            newRoomCode = target.Code;
                            _logger.LogInformation("[多世界] 成功加入新房间: {Code}", target.Code);
                            break;
                        }
                        _logger.LogWarning("[多世界] 加入房间 {Code} 失败，重试", target.Code);
                    }
                    else
                    {
                        _logger.LogInformation("[多世界] 未找到新房主 [{Host}] 的房间，等待中...（第{N}次）", roundHostPlayer.PlayerName, attempt);
                    }
                    await Task.Delay(3000, _ct);
                }

                if (newRoomCode == null)
                {
                    _logger.LogError("[多世界] 等待超时，未找到新房主 [{Host}] 的房间，终止多世界模式", roundHostPlayer.PlayerName);
                    _multiplayerCoordinator = null;
                    return;
                }

                // 加入房间后立刻发心跳，确保服务器知道成员在线（避免 AllOnlineMembersReported 提前触发）
                await client.SendHeartbeatAsync();
                _logger.LogInformation("[多世界] 第 {Round} 轮成员已发送心跳", round + 1);

                // 等待房主就绪
                var hostReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Action<bool> readyHandler = ready => { if (ready) hostReadyTcs.TrySetResult(true); };
                client.HostReadyChanged += readyHandler;
                try
                {
                    if (!await client.IsHostReadyAsync())
                    {
                        using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.PartyTimeoutSeconds));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_ct, readyCts.Token);
                        using var reg = linked.Token.Register(() => hostReadyTcs.TrySetResult(false));
                        if (!await hostReadyTcs.Task)
                        {
                            _logger.LogError("[多世界] 等待房主就绪超时");
                            return;
                        }
                    }
                }
                finally { client.HostReadyChanged -= readyHandler; }

                // 拉取新配置，带重试机制（最多3次）
                RoomConfig? hostConfig = null;
                for (int retry = 1; retry <= 3; retry++)
                {
                    hostConfig = await client.GetRoomConfigAsync();
                    if (hostConfig != null)
                    {
                        _logger.LogInformation("[多世界] 第 {Round} 轮成功拉取房主配置（第{N}次尝试）", round + 1, retry);
                        break;
                    }
                    if (retry < 3)
                    {
                        _logger.LogWarning("[多世界] 第 {Round} 轮拉取房主配置失败（第{N}次尝试），2秒后重试", round + 1, retry);
                        await Task.Delay(2000, _ct);
                    }
                }
                
                if (hostConfig != null)
                {
                    _config.SyncTimeoutSeconds = hostConfig.SyncTimeoutSeconds;
                    _config.MinPlayersToSync = hostConfig.MinPlayersToSync;
                    _config.SyncPointMinDistance = hostConfig.SyncPointMinDistance;
                    _config.StartRouteIndex = hostConfig.StartRouteIndex;
                    _config.UseFixedDebugRoutes = hostConfig.UseFixedDebugRoutes;
                    _config.FixedDebugRoutePath = hostConfig.FixedDebugRoutePath;
                    _config.DebugMode = hostConfig.DebugMode;
                    _config.ReturnToFightPointAfterBattle = hostConfig.ReturnToFightPointAfterBattle;
                    _config.ReturnToFightPointStaySeconds = hostConfig.ReturnToFightPointStaySeconds;
                    _config.KazuhaPlayerIndex = hostConfig.KazuhaPlayerIndex;
                    _config.PartyTimeoutSeconds = hostConfig.PartyTimeoutSeconds;
                    _config.MultiWorldEnabled = hostConfig.MultiWorldEnabled;
                    _config.MultiWorldCount = hostConfig.MultiWorldCount;
                    _config.SelectedBuiltinRoute = hostConfig.SelectedBuiltinRoute;
                    _config.FightExtraWaitSeconds = hostConfig.FightExtraWaitSeconds;
                    _config.RejoinMaxWaitSeconds = hostConfig.RejoinMaxWaitSeconds;

                    // 联机模式：设置战斗超时覆盖值（不修改原始配置）
                    PathingConditionConfig.MultiplayerFightTimeoutOverride = hostConfig.FightTimeoutSeconds;
                }
                else
                {
                    // 多世界模式下配置同步失败是严重错误，必须终止
                    _logger.LogError("[多世界] 第 {Round} 轮成员无法获取房主配置（重试3次后仍失败），终止多世界模式", round + 1);
                    _multiplayerCoordinator = null;
                    return;
                }

                // 加入新房主世界
                var joinOk = await autoParty.JoinHostWorldAsync(roundHostPlayer.PlayerUid, _ct);
                if (!joinOk)
                {
                    _logger.LogError("[多世界] 加入第 {Round} 轮房主世界失败", round + 1);
                    return;
                }

                // 先注册 AllWorldJoined 监听，再上报，避免信号在上报和等待之间丢失
                var allJoinedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Action allJoinedHandler = () => allJoinedTcs.TrySetResult(true);
                client.AllWorldJoined += allJoinedHandler;
                try
                {
                    await client.ReportWorldJoinedAsync();

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.PartyTimeoutSeconds));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(_ct, timeoutCts.Token);
                    using var reg = linked.Token.Register(() => allJoinedTcs.TrySetResult(false));
                    if (!await allJoinedTcs.Task)
                        _logger.LogWarning("[多世界] 等待全员就绪超时，继续执行");
                }
                finally { client.AllWorldJoined -= allJoinedHandler; }
            }

            // 重建 coordinator
            var barrier = new SyncBarrier(client, _config.SyncTimeoutSeconds);
            var resolver = new SyncPointResolver();
            _multiplayerCoordinator = new MultiplayerCoordinator(client, barrier, resolver,
                _config.MinPlayersToSync, _config.SyncTimeoutSeconds);

            _multiplayerCoordinator.OnDegraded += reason =>
                _logger.LogWarning("[联机] 已降级为单机模式，原因：{Reason}", reason);
            // 连续超时退出处理（需求 5）— 多世界模式每轮重建时也需要注册
            _multiplayerCoordinator.OnConsecutiveSyncTimeoutExceeded += async (isHost) =>
            {
                if (isHost)
                {
                    _logger.LogError("[联机] 房主连续超时达到上限，广播关闭房间并停止");
                    try { await _coordinatorClientRef!.CloseRoomAsync(); } catch (Exception ex2)
                    {
                        _logger.LogWarning(ex2, "[联机] 广播关闭房间失败（静默忽略，成员靠心跳超时感知）");
                    }
                }
                else
                {
                    _logger.LogError("[联机] 成员连续超时达到上限，上报 Offline 并退出");
                    try { await _coordinatorClientRef!.ReportMemberStatusAsync(MemberStatus.Offline); } catch (Exception ex2)
                    {
                        _logger.LogWarning(ex2, "[联机] 上报 Offline 失败（静默忽略）");
                    }
                }
                _multiplayerCoordinator?.Degrade("连续超时达到上限");
            };
            _consistencyChecker = new RouteConsistencyChecker();
            _executionEngine?.SetCoordinator(_multiplayerCoordinator);
            _executionEngine?.SetWorldStateMonitor(_worldStateMonitor);
            // 重置路线进度信息（需求 6），避免上一轮的旧进度在新轮次心跳中上报
            _coordinatorClientRef?.UpdateRouteProgress(-1, DateTime.UtcNow, 0);
            // 轮次切换完成，恢复 WorldStateMonitor 检测（需求 2）
            _worldStateMonitor?.EndRoundSwitch();
            _logger.LogInformation("[多世界] 第 {Round} 轮初始化完成", round + 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[多世界] 第 {Round} 轮初始化异常", round + 1);
            _multiplayerCoordinator = null;
            // 初始化失败也要恢复 WorldStateMonitor 检测（需求 2）
            _worldStateMonitor?.EndRoundSwitch();
        }
    }

    /// <summary>全员同步本轮结束</summary>
    private async Task SyncRoundEndAsync(int round, CoordinatorClient client)
    {
        // 协调停止已触发，跳过轮次同步等待（避免等 120s 超时）
        if (_sessionTerminated || _multiplayerCoordinator?.IsExitTriggered == true)
        {
            _logger.LogWarning("[多世界] 协调停止已触发，跳过轮次结束同步");
            return;
        }

        try
        {
            var syncId = $"round_end_{round}";
            _logger.LogInformation("[多世界] 同步轮次结束: {SyncId}", syncId);
            // 用 PartyTimeoutSeconds 作为超时，因为需要等所有人跑完本轮全部路线
            var barrier = new SyncBarrier(client, _config.PartyTimeoutSeconds);
            await barrier.WaitAsync(syncId, _ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[多世界] 轮次结束同步异常，继续");
        }
    }

    /// <summary>离开当前世界（成员退出，房主关闭房间）</summary>
    private async Task LeaveCurrentWorldAsync(bool amIHost, CoordinatorClient client)
    {
        var autoParty = new AutoPartyTask();
        if (amIHost)
        {
            // 房主：等待成员有足够时间主动离开（LeaveWorldAsync 约需 5-20 秒）
            // 然后关闭房间，被动踢出还未离开的成员
            // 等待时间 = min(PartyTimeoutSeconds/10, 15) 秒，默认 15 秒
            var waitMs = Math.Min(_config.PartyTimeoutSeconds / 10 * 1000, 15000);
            _logger.LogInformation("[多世界] 房主等待成员离开（{T}s）后关闭房间", waitMs / 1000);
            await Task.Delay(waitMs, _ct);
            await client.CloseRoomAsync();
            _logger.LogInformation("[多世界] 房主已关闭房间");
            await Task.Delay(2000, _ct);
            // 房主也需要退出多人世界，回到自己的世界
            _logger.LogInformation("[多世界] 房主退出多人世界");
            var left = await autoParty.LeaveWorldAsync(_ct);
            if (!left)
                _logger.LogWarning("[多世界] 房主退出多人世界未确认成功，继续下一轮");
            await Task.Delay(1000, _ct);
        }
        else
        {
            // 成员：主动离开，回到自己的世界
            _logger.LogInformation("[多世界] 成员离开当前世界");
            var left = await autoParty.LeaveWorldAsync(_ct);
            if (!left)
                _logger.LogWarning("[多世界] 离开世界操作未确认成功，继续下一轮");
            await Task.Delay(1000, _ct);
        }
    }

    /// <summary>单世界锄地核心（从 RunTask 提取，供多世界循环复用）</summary>
    private async Task RunSingleWorldCoreAsync(string accountName, List<List<string>>? groupTags = null)
    {
        groupTags ??= BuildGroupTags();
        await RunSingleWorldAsync(accountName, groupTags);
    }

    private async Task RunSingleWorldAsync(string accountName, List<List<string>> groupTags)
    {
        // 根据操作模式执行
        var operationMode = _config.OperationMode;

        if (operationMode == "启用仅指定怪物模式")
        {
            await RunTargetMonsterMode(accountName, groupTags);
        }
        else if (_config.UseFixedDebugRoutes)
        {
            // 固定调试线路模式：使用三级优先级逻辑加载路线
            _logger.LogInformation("[固定调试线路] 开始加载路线");
            var fixedRoutes = LoadRoutesBasedOnConfig();
            if (fixedRoutes.Count == 0)
            {
                _logger.LogWarning("[固定调试线路] 没有找到可用的路线文件");
                return;
            }
            _logger.LogInformation("[固定调试线路] 共加载 {Count} 条路线，按文件名顺序执行", fixedRoutes.Count);

            // 队伍校验
            ValidateTeam();

            await ProcessRoutesByGroup(fixedRoutes, accountName);
        }
        else
        {
            // 预处理路线
            var pathingDir = Path.Combine(_dataDir, "pathing");
            var routes = RouteInfoLoader.LoadRoutes(
                pathingDir, _monsterRepo, _config.IgnoreRate, groupTags[0]);

            // 初始化CD和运行记录
            foreach (var route in routes)
                _cdManager.InitializeRoute(route);

            // 自我优化
            SelfOptimizer.Apply(routes, _config.DisableSelfOptimization, _config.CuriosityFactor);

            // 标记过滤
            var priorityTags = ParseChineseTags(_config.PriorityTags);
            var excludeTags = ParseChineseTags(_config.ExcludeTags);
            if (!_config.PickupMode.Contains("模板匹配") && !excludeTags.Contains("沙暴"))
                excludeTags.Add("沙暴");

            RouteMarker.MarkRoutes(routes, groupTags, priorityTags, excludeTags);

            // 路线选择优化
            var targetElite = Math.Max(0, _config.TargetEliteNum) + 5;
            var targetMonster = Math.Max(0, _config.TargetMonsterNum) + 25;
            _routeSelector.SelectOptimalRoutes(
                routes, _config.EfficiencyIndex, targetElite, targetMonster, _config.SortMode);

            // 分组分配
            RouteGroupAssigner.AssignGroups(routes, groupTags);

            if (operationMode == "调试路线分配")
            {
                RouteGroupAssigner.PrintGroupSummary(routes, _config, _dataDir);
                _cdManager.UpdateAllRecords(routes);
            }
            else if (operationMode == "运行锄地路线")
            {
                // 队伍校验
                ValidateTeam();

                _logger.LogInformation("开始运行锄地路线");
                _cdManager.UpdateAllRecords(routes);
                await ProcessRoutesByGroup(routes, accountName);
            }
            else // 强制刷新所有运行记录
            {
                _logger.LogInformation("强制刷新所有运行记录");
                _cdManager.ClearAll();
                // 同时清除内存中路线对象上的CD和运行记录
                foreach (var route in routes)
                {
                    route.CdTime = DateTime.MinValue;
                    route.Records.Clear();
                }
                _cdManager.UpdateAllRecords(routes);
            }
        }
    }

    private async Task RunTargetMonsterMode(string accountName, List<List<string>> groupTags)
    {
        var targetMonsters = ParseChineseTags(_config.TargetMonsters);
        if (targetMonsters.Count == 0)
        {
            _logger.LogError("目标怪物为空，请检查配置");
            return;
        }

        _logger.LogInformation("目标怪物模式：{Monsters}", string.Join("、", targetMonsters));

        var pathingDir = Path.Combine(_dataDir, "pathing");
        var fakeGroupTags = Enumerable.Range(0, 10).Select(_ => new List<string>()).ToList();
        var routes = RouteInfoLoader.LoadRoutes(pathingDir, _monsterRepo, _config.IgnoreRate, fakeGroupTags[0]);

        foreach (var route in routes)
            _cdManager.InitializeRoute(route);

        // 逐路线匹配
        foreach (var route in routes)
        {
            var textToSearch = (route.FullPath ?? "") + " " +
                string.Join(" ", route.MonsterInfo.Keys);
            route.Selected = targetMonsters.Any(m => textToSearch.Contains(m));
            route.Group = route.Selected ? 1 : 0;
        }

        var selectedCount = routes.Count(p => p.Selected);
        _logger.LogInformation("目标怪物模式：共找到 {Count} 条相关路线", selectedCount);

        _cdManager.UpdateAllRecords(routes);
        await ProcessRoutesByGroup(routes, accountName);
    }

    private async Task ProcessRoutesByGroup(List<RouteInfo> routes, string accountName)
    {
        var targetGroup = _config.GroupIndex;
        var groupRoutes = routes.Where(r => r.Group == targetGroup && r.Selected).ToList();

        // === 路线跳过对齐修复（sync-point-route-skip-alignment）===
        // 每轮开始时重置成员路线进度缓存，防止跨轮误判
        if (_coordinatorClientRef != null)
        {
            _coordinatorClientRef.ResetMemberProgressCache();
            _logger.LogDebug("[联机] 新一轮开始：成员路线进度缓存已重置");
        }
        
        // 连续不跳过重试计数（防止无限循环）
        int consecutiveNoSkipRetryCount = 0;
        const int MaxNoSkipRetries = 3;
        
        // 智能跳过决策辅助方法
        bool ShouldSkipRoute(int currentRouteIndex)
        {
            if (_multiplayerCoordinator == null || _coordinatorClientRef == null)
                return true; // 单机模式或无协调器时无条件跳过
            
            // 获取所有对方玩家中最小路线索引
            int? minPeerRouteIndex = GetMinPeerRouteIndex();
            
            // 查询失败兜底：返回 true（无条件跳过）
            if (minPeerRouteIndex == null)
            {
                _logger.LogWarning("[联机] 智能跳过决策：查询对方进度失败，兜底为无条件跳过");
                return true;
            }
            
            // 目标路线索引 = 当前路线索引 + 1（跳过后将进入的路线）
            int targetRouteIndex = currentRouteIndex + 1;
            
            // 对方路线索引 <= 目标路线索引 → 不跳过（对方还在目标路线或更早）
            if (minPeerRouteIndex <= targetRouteIndex)
            {
                _logger.LogInformation("[联机] 智能跳过决策：不跳过路线 {Current}（对方进度 {Peer} <= 目标路线 {Target}）",
                    currentRouteIndex, minPeerRouteIndex, targetRouteIndex);
                return false;
            }
            
            // 对方已超前 → 跳过
            _logger.LogInformation("[联机] 智能跳过决策：跳过路线 {Current}（对方进度 {Peer} > 目标路线 {Target}）",
                currentRouteIndex, minPeerRouteIndex, targetRouteIndex);
            return true;
        }
        
        // 获取所有对方玩家中最小路线索引
        int? GetMinPeerRouteIndex()
        {
            if (_coordinatorClientRef == null || _coordinatorClientRef.CurrentPlayerList == null)
                return null;
            
            // 过滤掉自己
            var myUid = _config.PlayerUid;
            var peerPlayers = _coordinatorClientRef.CurrentPlayerList
                .Where(p => p.PlayerUid != myUid)
                .ToList();
            
            if (peerPlayers.Count == 0)
            {
                _logger.LogDebug("[联机] 无对方玩家，返回 null");
                return null;
            }
            
            // 获取所有对方玩家的路线索引，取最小值（最落后玩家）
            int? minIndex = null;
            foreach (var player in peerPlayers)
            {
                var peerIndex = _coordinatorClientRef.GetPeerRouteIndex(player.PlayerUid);
                if (peerIndex.HasValue)
                {
                    minIndex = minIndex.HasValue ? Math.Min(minIndex.Value, peerIndex.Value) : peerIndex.Value;
                    _logger.LogDebug("[联机] 玩家 {Uid} 路线索引: {Index}", player.PlayerUid, peerIndex.Value);
                }
                else
                {
                    _logger.LogDebug("[联机] 玩家 {Uid} 路线索引: 未缓存", player.PlayerUid);
                }
            }
            
            _logger.LogDebug("[联机] 最小对方路线索引: {Index}", minIndex?.ToString() ?? "null");
            return minIndex;
        }
        // === 路线跳过对齐修复结束 ===

        // 步骤1：联机模式路线列表同步（必须在验证之前，确保两端用相同路线集合验证）
        if (_config.MultiplayerEnabled && _coordinatorClientRef != null)
        {
            // 房主身份判断：优先使用 UID 匹配，仅在 PlayerUid 为空时使用 MultiplayerRole 兜底
            bool isHost = !string.IsNullOrEmpty(_config.PlayerUid)
                ? _coordinatorClientRef.HostPlayerUid == _config.PlayerUid
                : _config.MultiplayerRole == "host";

            if (isHost || string.IsNullOrEmpty(_coordinatorClientRef.HostPlayerUid))
            {
                // 房主：CD 过滤后上传最终路线文件名列表
                var hostRouteNames = groupRoutes
                    .Where(r => _config.StartRouteIndex > 0 || !_cdManager.IsOnCooldown(r))
                    .Select(r => r.FileName)
                    .ToList();
                await _coordinatorClientRef.SetHostRouteListAsync(hostRouteNames);
                _logger.LogInformation("[联机] 房主已上传路线列表，共 {Count} 条（CD过滤后）", hostRouteNames.Count);

                // 用过滤后的列表替换 groupRoutes
                var hostRouteSet = new HashSet<string>(hostRouteNames);
                groupRoutes = groupRoutes.Where(r => hostRouteSet.Contains(r.FileName)).ToList();
            }
            else
            {
                // 成员：等待房主路线列表就绪事件，或轮询兜底
                var routeListTcs = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                Action<List<string>> routeListHandler = names => routeListTcs.TrySetResult(names);
                _coordinatorClientRef.HostRouteListReady += routeListHandler;
                List<string> hostRouteNames;
                try
                {
                    // 先尝试直接拉取（房主可能已经上传了）
                    var existing = await _coordinatorClientRef.GetHostRouteListAsync();
                    if (existing.Count > 0)
                    {
                        hostRouteNames = existing;
                        _logger.LogInformation("[联机] 直接获取到房主路线列表，共 {Count} 条", hostRouteNames.Count);
                    }
                    else
                    {
                        // 等待推送事件，最多90秒
                        _logger.LogInformation("[联机] 等待房主上传路线列表...");
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_ct, timeoutCts.Token);
                        using var reg = linked.Token.Register(() => routeListTcs.TrySetResult([]));
                        hostRouteNames = await routeListTcs.Task;
                        if (hostRouteNames.Count > 0)
                            _logger.LogInformation("[联机] 收到房主路线列表推送，共 {Count} 条", hostRouteNames.Count);
                        else
                        {
                            _logger.LogError("[联机] 等待房主上传路线列表超时（90秒），停止锄地");
                            return;
                        }
                    }
                }
                finally
                {
                    _coordinatorClientRef.HostRouteListReady -= routeListHandler;
                }

                // 先从完整路线列表查找（不限于当前 group 过滤后的结果）
                var allRoutesDict = routes.ToDictionary(r => r.FileName);
                var found = hostRouteNames
                    .Where(name => allRoutesDict.ContainsKey(name))
                    .Select(name => allRoutesDict[name])
                    .ToList();

                // 如果有路线在本地找不到，尝试从 pathing 目录重新加载全量路线
                if (found.Count < hostRouteNames.Count)
                {
                    var pathingDir = Path.Combine(_dataDir, "pathing");
                    var allRoutes = RouteInfoLoader.LoadRoutes(pathingDir, _monsterRepo, _config.IgnoreRate, new List<string>());
                    var fullDict = allRoutes.ToDictionary(r => r.FileName);
                    found = hostRouteNames
                        .Where(name => fullDict.ContainsKey(name))
                        .Select(name => fullDict[name])
                        .ToList();

                    var missing = hostRouteNames.Where(name => !fullDict.ContainsKey(name)).ToList();
                    if (missing.Count > 0)
                        _logger.LogWarning("[联机] 成员本地缺少以下路线文件（路线一致性验证将检测到差异）: {Files}", string.Join(", ", missing));
                }
                groupRoutes = found;
                _logger.LogInformation("[联机] 成员已同步房主路线列表，共 {Count} 条", groupRoutes.Count);
            }
        }

        // 步骤1.5：路线同步完成后的同步点，确保房主和成员同时进入验证阶段
        if (_multiplayerCoordinator != null && _config.MultiplayerEnabled)
        {
            // 路线为0时直接跳过后续流程
            if (groupRoutes.Count == 0)
            {
                _logger.LogWarning("[联机] 路线列表为空（可能全部在CD中），跳过本轮执行");
                return;
            }
            try
            {
                _logger.LogInformation("[联机] 等待所有玩家完成路线同步...");
                await _multiplayerCoordinator.WaitForAllPlayers("route_sync_done", _ct);
                _logger.LogInformation("[联机] 所有玩家路线同步完成，开始验证");
            }
            catch (OperationCanceledException)
            {
                if (_ct.IsCancellationRequested) throw;
                _logger.LogWarning("[联机] 路线同步等待超时，继续执行");
            }
        }

        // 步骤2：路线一致性验证（在同步后的路线集合上验证）
        if (_multiplayerCoordinator != null && _consistencyChecker != null && _coordinatorClientRef != null)
        {
            if (_config.DebugMode)
            {
                _logger.LogInformation("[联机] 调试模式：跳过路线一致性验证，共 {Count} 条路线", groupRoutes.Count);
            }
            else
            {
                _logger.LogInformation("[联机] 开始路线一致性验证，本次路线数量: {Count}", groupRoutes.Count);
                using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
                verifyCts.CancelAfter(TimeSpan.FromSeconds(120));
                try
                {
                    var verified = await _consistencyChecker.VerifyRoutesAsync(
                        _coordinatorClientRef,
                        groupRoutes.Select(r => r.FullPath),
                        verifyCts.Token);
                    if (verified == false)
                    {
                        _logger.LogError("[联机] 路线一致性验证失败，两端路线不一致，停止锄地");
                        return;
                    }
                    else if (verified == null)
                        _logger.LogWarning("[联机] 路线一致性验证超时，继续执行");
                    else
                        _logger.LogInformation("[联机] 路线一致性验证通过");
                }
                catch (OperationCanceledException)
                {
                    if (_ct.IsCancellationRequested) throw;
                    _logger.LogWarning("[联机] 路线一致性验证超时，继续执行");
                }
            }
        }

        // 步骤3：路线验证同步等待（等待所有玩家完成验证后再开始执行）
        // 注意：RouteVerificationPassed 事件已在步骤2的 VerifyRoutesAsync 中被消费，
        // 当 VerifyRoutesAsync 返回时，说明服务器已收到所有玩家的路线上报并完成了对比，
        // 因此无需再次等待。跳过此步骤避免竞态条件（事件在订阅前已触发）。
        if (_multiplayerCoordinator != null && _config.MultiplayerEnabled)
        {
            // 验证已在步骤2完成（服务器收到所有玩家上报后才广播 RouteVerificationPassed）
            // 无需额外等待
            _logger.LogDebug("[联机] 路线验证已在步骤2完成，跳过额外同步等待");
        }

        // 路线为0时直接返回，避免卡住
        if (groupRoutes.Count == 0)
        {
            _logger.LogWarning("路径组{G} 没有可执行的路线（可能全部在CD中或未选中），跳过", targetGroup);
            return;
        }

        // 计算组内总计信息
        var totalElites = groupRoutes.Sum(r => r.EliteMonsterCount);
        var totalMonsters = groupRoutes.Sum(r => r.NormalMonsterCount);
        var totalGain = groupRoutes.Sum(r => r.EliteMoraGain + r.NormalMoraGain);
        var totalEstimatedTime = groupRoutes.Sum(r => r.AdjustedTime);

        var tsTotal = TimeSpan.FromSeconds(totalEstimatedTime);
        _logger.LogInformation("当前组 路径组{G} 共 {Count} 条路线，精英{E}，小怪{M}，预计用时 {H}时{Min}分{S}秒",
            targetGroup, groupRoutes.Count, totalElites, totalMonsters,
            (int)tsTotal.TotalHours, tsTotal.Minutes, tsTotal.Seconds);

        // 切换队伍
        if (!string.IsNullOrEmpty(_config.PartyName))
        {
            if (_config.MultiplayerEnabled)
            {
                _logger.LogInformation("[联机] 联机模式下队伍已在准备阶段切换，跳过重复切换");
            }
            else
            {
                _logger.LogInformation("切换至配置队伍: {Name}", _config.PartyName);
                var switchSuccess = await new SwitchPartyTask().Start(_config.PartyName, _ct);
                if (!switchSuccess)
                {
                    _logger.LogWarning("切换队伍失败: {Name}，继续执行", _config.PartyName);
                }
                await Delay(500, _ct);
            }
        }
        else
        {
            _logger.LogInformation("[DEBUG] 未配置队伍名称，跳过切换队伍");
        }

        int count = 0;
        int consecutiveSkipCount = 0; // 联机模式：连续跳过路线计数（需求 1）
        var groupStartTime = DateTime.Now;
        double remainingEstimatedTime = totalEstimatedTime;
        double skippedTime = 0;

        // 调试用：从指定索引开始（1-based，0表示从头）
        var startIndex = Math.Max(0, _config.StartRouteIndex - 1);
        if (startIndex > 0 && startIndex < groupRoutes.Count)
        {
            _logger.LogInformation("[调试] 从第 {Start} 条路线开始执行（跳过前 {Skip} 条）",
                startIndex + 1, startIndex);
        }

        _logger.LogInformation("[DEBUG] 开始遍历路线，共 {Count} 条，startIndex={Start}", groupRoutes.Count, startIndex);

        foreach (var route in groupRoutes.Skip(startIndex))
        {
            // === 中断重对齐检查（multiplayer-abort-and-realign spec）===
            if (_multiplayerCoordinator?.IsAbortRequested == true)
            {
                var targetRoute = _multiplayerCoordinator.GetAbortTargetRouteIndex();
                _logger.LogWarning("[联机] 收到中断指令，跳转到目标路线 {TargetRoute}", targetRoute);
                
                // 等待服务器广播的 StartRoute 指令（带超时）
                var startRoute = await _multiplayerCoordinator.WaitForStartRouteAsync(30, _ct);
                if (startRoute >= 0)
                {
                    // 清除中断状态
                    _multiplayerCoordinator.ClearAbortState();
                    
                    // 检查目标路线是否有效
                    if (startRoute >= 0 && startRoute < groupRoutes.Count)
                    {
                        // 跳转到目标路线
                        count = startRoute - startIndex;
                        if (count < 0) count = 0; // 目标路线已经过了，从头开始
                        
                        _logger.LogInformation("[联机] 跳转到路线 {TargetRoute}", startRoute);
                        continue; // 重新开始循环，跳转到目标路线
                    }
                }
                else
                {
                    _logger.LogWarning("[联机] 等待开始路线指令超时，继续执行当前路线");
                    _multiplayerCoordinator.ClearAbortState();
                }
            }
            
            // 每条路线开始时重置连续超时计数（需求 5），避免跨路线累积误触发
            _multiplayerCoordinator?.ResetSyncTimeoutCount();

            // 当前路线索引
            int currentRouteIndex = startIndex + count;
            
            // === 强制线路同步检查（multiplayer-route-enforcement spec）===
            if (_multiplayerCoordinator?.IsRouteEnforceSyncRequested == true)
            {
                var enforceTarget = _multiplayerCoordinator.GetEnforceTargetRouteIndex();
                _logger.LogWarning("[联机] 收到强制线路同步指令，需要跳转到目标线路 {TargetRoute}，当前线路 {CurrentRoute}",
                    enforceTarget, currentRouteIndex);
                
                // 清除强制同步状态
                _multiplayerCoordinator.ClearRouteEnforceSync();
                
                // 检查目标线路是否有效且在当前线路之前
                if (enforceTarget >= 0 && enforceTarget < currentRouteIndex && enforceTarget < groupRoutes.Count)
                {
                    // 计算跳转后的 count 值
                    count = enforceTarget - startIndex;
                    if (count < 0) count = 0; // 目标线路已经过了，从头开始
                    
                    _logger.LogInformation("[联机] 强制线路同步：跳转到线路 {TargetRoute}", enforceTarget);
                    continue; // 重新开始循环，跳转到目标线路
                }
                else
                {
                    _logger.LogInformation("[联机] 目标线路 {TargetRoute} 不在当前线路 {CurrentRoute} 之前，无需跳转",
                        enforceTarget, currentRouteIndex);
                }
            }
            
            // === 线路同步检查点（需求 10）：路线开始时检查同步 ===
            if (_multiplayerCoordinator?.RouteSyncCoordinator != null && _config.MultiplayerEnabled)
            {
                try
                {
                    var syncDecision = await _multiplayerCoordinator.RouteSyncCoordinator.CheckSyncAtRouteStart(currentRouteIndex, _ct);
                    
                    if (syncDecision == RouteSyncDecision.SkipToTarget)
                    {
                        // 正常玩家落后，需要追赶异常玩家
                        _logger.LogInformation("[联机] 线路同步检查：需要跳到目标线路追赶");
                        
                        // 刷新进度缓存
                        _multiplayerCoordinator.RouteSyncCoordinator.RefreshCache();
                        
                        // 上报 Rejoining 状态，触发路线跳过流程追赶
                        _logger.LogInformation("[联机] 上报 Rejoining 状态，触发路线跳过流程追赶");
                        await _coordinatorClientRef.ReportMemberStatusAsync(MemberStatus.Rejoining);
                        
                        // 继续执行当前路线，在路线执行时会检测到 Rejoining 状态并触发跳过
                    }
                    else if (syncDecision == RouteSyncDecision.Abort)
                    {
                        // 协调失败，需要结束锄地
                        _logger.LogWarning("[联机] 线路同步检查：协调失败，需要结束锄地");
                        return;
                    }
                    // RouteSyncDecision.Proceed: 正常执行
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[联机] 线路同步检查异常，继续执行当前路线");
                }
            }

            // 上报当前路线进度信息（需求 6），下次心跳时自动携带
            _coordinatorClientRef?.UpdateRouteProgress(currentRouteIndex, DateTime.UtcNow, route.AdjustedTime);

            _logger.LogInformation("[DEBUG] 进入路线循环，count={Count}，route={Name}", count + 1, route.FileName);
            _ct.ThrowIfCancellationRequested();
            // 联机协调停止检查（需求 2.2）
            if (_multiplayerCoordinator?.IsExitTriggered == true)
            {
                _logger.LogWarning("[联机] 协调停止已触发，退出路线循环");
                break;
            }
            count++;

            // 时间限制检查
            if (_timeChecker.IsInRestrictedPeriod() || _timeChecker.IsApproachingRestriction())
            {
                _logger.LogWarning("接近或处于限制时间，停止执行");
                break;
            }

            // CD检查（StartRouteIndex > 0 时跳过CD检查，强制执行；填0则正常检测CD）
            // 联机模式下成员不检测CD，由房主路线列表控制
            if (!_config.MultiplayerEnabled && _config.StartRouteIndex <= 0 && _cdManager.IsOnCooldown(route))
            {
                _logger.LogInformation("路线 {Name} 未刷新，跳过", route.FileName);
                skippedTime += route.AdjustedTime;
                remainingEstimatedTime -= route.AdjustedTime;
                continue;
            }

            _logger.LogInformation("开始第 {N}/{T} 条线路: {Name}",
                startIndex + count, groupRoutes.Count, route.FileName);

            // 白芙切换
            if (_shouldSwitchFurina)
            {
                _logger.LogInformation("上条路线检测到白芙，执行强制黑芙切换");
                _shouldSwitchFurina = false;
                var switchPath = Path.Combine(_dataDir, "assets", "强制黑芙.json");
                if (File.Exists(switchPath))
                {
                    var switchTask = PathingTask.BuildFromFilePath(switchPath);
                    if (switchTask != null)
                    {
                        var executor = new PathExecutor(_ct);
                        executor.PartyConfig = _partyConfig;
                        await executor.Pathing(switchTask);
                    }
                }
            }

            // 料理buff
            _logger.LogInformation("[DEBUG] 开始料理buff检查");
            await _cookingService.TryUseCooking(_config.CookingNames, _ct);
            _logger.LogInformation("[DEBUG] 料理buff检查完成，准备执行路线");

            var sw = Stopwatch.StartNew();

            try
            {
                if (_executionEngine != null)
                {
                    var execResult = await _executionEngine.ExecuteRoute(route, _ct, currentRouteIndex);
                    _shouldSwitchFurina = execResult.ShouldSwitchFurina;
                    sw.Stop();
                    var duration = execResult.ActualDuration;

                    // 更新剩余时间
                    remainingEstimatedTime -= route.AdjustedTime;

                    // 联机模式：检测路线跳过（需求 1）
                    if (execResult.SkipRouteRequested && _multiplayerCoordinator != null && _coordinatorClientRef != null)
                    {
                        consecutiveSkipCount++;
                        _logger.LogWarning("[联机] 路线 {Name} 被跳过（原因: {Reason}），连续跳过: {Count}/{Max}",
                            route.FileName, execResult.SkipRouteReason, consecutiveSkipCount, _config.MaxConsecutiveSkips);

                        // 不在这里上报 Normal — 保持 IsAbnormal 状态，
                        // 让 MemberStatusChanged 重评估能放行正在等的正常玩家。
                        // Normal 会在下一个同步点等待前上报（PathExecutor 中 _needReportNormalBeforeSync）。

                        // === 路线跳过对齐修复（sync-point-route-skip-alignment）===
                        // 智能跳过决策与进度广播
                        if (_multiplayerCoordinator != null)
                        {
                            // 获取当前路线索引（使用外部作用域的 currentRouteIndex）
                            // count 已递增，需要减1
                            int routeIdx = startIndex + count - 1;
                            
                            // 智能跳过决策
                            bool shouldSkip = ShouldSkipRoute(routeIdx);
                            
                            if (!shouldSkip)
                            {
                                // 不跳过路线：递增连续不跳过重试计数
                                consecutiveNoSkipRetryCount++;
                                _logger.LogInformation("[联机] 智能跳过决策：不跳过路线 {Index}（对方进度 <= 目标路线），重试计数: {Retry}/{Max}",
                                    currentRouteIndex, consecutiveNoSkipRetryCount, MaxNoSkipRetries);
                                
                                if (consecutiveNoSkipRetryCount >= MaxNoSkipRetries)
                                {
                                    // 达到上限，强制跳过
                                    _logger.LogWarning("[联机] 连续不跳过重试达到上限（{Max}次），强制跳过路线 {Index}",
                                        MaxNoSkipRetries, currentRouteIndex);
                                    shouldSkip = true;
                                    consecutiveNoSkipRetryCount = 0;
                                }
                                else
                                {
                                    // 继续重试当前路线
                                    _logger.LogInformation("[联机] 继续重试当前路线 {Index}（新 PathExecutor 实例）", currentRouteIndex);
                                    continue; // 重新执行当前路线
                                }
                            }
                            
                            if (shouldSkip)
                            {
                                // 确认跳过：重置重试计数，广播进度，通知对方
                                consecutiveNoSkipRetryCount = 0;
                                
                                // 立即广播新进度（解决进度广播时机窗口期）
                                await _coordinatorClientRef.SendMemberProgressAsync(currentRouteIndex + 1);
                                _logger.LogDebug("[联机] 已广播跳过后进度: 路线 {Index}", currentRouteIndex + 1);
                                
                                // === 等待点上报修复（skip-route-wait-point-report）===
                                // 先发送 RouteSkipped（立即放行）
                                await _multiplayerCoordinator.NotifyRouteSkippedAsync(currentRouteIndex);
                                
                                // 再发送 WaitPointReport（等待点对齐）
                                // 获取当前世界轮次
                                int worldRound = GetCurrentWorldRound();
                                // 获取路线ID
                                string routeId = currentRouteIndex.ToString();
                                // 获取下一个同步点（优先选择"传送必同步"的同步点）
                                string nextSyncPointId = await GetNextSyncPointForWaitPointAsync(currentRouteIndex, groupRoutes);
                                
                                if (!string.IsNullOrEmpty(nextSyncPointId))
                                {
                                    try
                                    {
                                        // 上报等待点（带容错处理）
                                        bool reportSuccess = await _multiplayerCoordinator.ReportWaitPointAsync(routeId, nextSyncPointId, worldRound);
                                        
                                        if (reportSuccess)
                                        {
                                            _logger.LogInformation("[联机] 等待点上报成功: Route={RouteId}, SyncPoint={SyncPointId}, Round={WorldRound}", 
                                                routeId, nextSyncPointId, worldRound);
                                        }
                                        else
                                        {
                                            // 上报失败，记录日志但不阻塞执行
                                            _logger.LogWarning("[联机] 等待点上报失败，已回退到RouteSkipped机制");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        // 异常处理：记录日志但不阻塞执行
                                        _logger.LogError(ex, "[联机] 等待点上报时发生异常，已回退到RouteSkipped机制");
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("[联机] 无法获取下一个同步点，跳过等待点上报");
                                }
                                // === 等待点上报修复结束 ===
                                
                                // 设置跳过下一个同步点标志
                                _multiplayerCoordinator.SetSkipNextSyncPoint();
                            }
                        }
                        // === 路线跳过对齐修复结束 ===

                        // 检查连续跳过是否达到上限
                        if (consecutiveSkipCount >= _config.MaxConsecutiveSkips)
                        {
                            _logger.LogError("[联机] 连续跳过达到上限（{Max}次），触发退出", _config.MaxConsecutiveSkips);
                            if (_multiplayerCoordinator.IsHost)
                            {
                                try { await _coordinatorClientRef.CloseRoomAsync(); } catch (Exception ex2)
                                {
                                    _logger.LogWarning(ex2, "[联机] 广播关闭房间失败（静默忽略）");
                                }
                            }
                            else
                            {
                                try { await _coordinatorClientRef.ReportMemberStatusAsync(MemberStatus.Offline); } catch (Exception ex2)
                                {
                                    _logger.LogWarning(ex2, "[联机] 上报 Offline 失败（静默忽略）");
                                }
                            }
                            _multiplayerCoordinator.Degrade("连续跳过路线达到上限");
                            break; // 退出路线循环
                        }

                        continue; // 跳到下一条路线，不记录 CD
                    }

                    if (execResult.FullyCompleted)
                    {
                        consecutiveSkipCount = 0; // 正常完成，归零连续跳过计数
                        consecutiveNoSkipRetryCount = 0; // 路线跳过对齐修复：正常完成时重置不跳过重试计数
                        // 联机模式：路线正常完成，确保状态为 Normal（可能之前是 Rejoining/Reviving）
                        if (_coordinatorClientRef != null && _multiplayerCoordinator != null)
                        {
                            try { await _coordinatorClientRef.ReportMemberStatusAsync(MemberStatus.Normal); } catch { }
                        }
                        
                        // === 线路同步检查点（需求 10）：路线完成时上报进度 ===
                        if (_multiplayerCoordinator?.RouteSyncCoordinator != null && _config.MultiplayerEnabled)
                        {
                            try
                            {
                                await _multiplayerCoordinator.RouteSyncCoordinator.ReportRouteCompletion(currentRouteIndex);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[联机] 路线完成进度上报异常");
                            }
                        }

                        // 联机模式下只有房主记录 CD（刷的是房主家的怪，成员只是协助）。
                        // 成员记 CD 会导致：第二轮自己当房主时上传给服务器的"非 CD 路线"少一大块，
                        // 自己家的怪没被刷却被记成已完成（测试反馈：4p 做地主只刷第 8 条，2/3p 做地主一条都不刷）。
                        bool shouldRecordCd = !_config.MultiplayerEnabled
                            || (_multiplayerCoordinator?.IsHost ?? false);
                        if (shouldRecordCd)
                        {
                            _cdManager.RecordCompletion(route, duration);
                            _cdManager.UpdateAllRecords(routes);
                        }
                        else
                        {
                            _logger.LogDebug("[联机] 成员模式：路线 {Name} 正常完成，但刷的是房主世界，不记本地 CD", route.FileName);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("路线 {Name} 未完整执行（中断/异常），不记录CD", route.FileName);
                    }

                    // 计算预计剩余时间
                    var actualUsedTime = (DateTime.Now - groupStartTime).TotalSeconds;
                    var consumedEstimated = totalEstimatedTime - remainingEstimatedTime - skippedTime;
                    var predictRemaining = consumedEstimated > 0
                        ? remainingEstimatedTime * actualUsedTime / consumedEstimated
                        : remainingEstimatedTime;
                    var tsRemain = TimeSpan.FromSeconds(Math.Max(0, predictRemaining));

                    _logger.LogInformation(
                        "完成第 {N}/{T} 条线路: {Name}，该组预计剩余: {H}时{Min}分{S}秒",
                        startIndex + count, groupRoutes.Count, route.FileName,
                        (int)tsRemain.TotalHours, tsRemain.Minutes, tsRemain.Seconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("执行路线 {Name} 出错: {Msg}", route.FileName, ex.Message);
            }
        }
    }

    /// <summary>
    /// 根据配置加载路线，实现三级优先级逻辑
    /// 优先级 1: 手动输入的 FixedDebugRoutePath
    /// 优先级 2: 选中的 SelectedBuiltinRoute
    /// 优先级 3: 默认行为（DebugRoutes 或正常路线选择）
    /// </summary>
    private List<RouteInfo> LoadRoutesBasedOnConfig()
    {
        // 优先级 1: 手动输入的调试目录
        if (!string.IsNullOrWhiteSpace(_config.FixedDebugRoutePath))
        {
            _logger.LogInformation("[线路加载] 使用手动指定路径: {Path}", _config.FixedDebugRoutePath);
            return LoadFixedDebugRoutes(_config.FixedDebugRoutePath);
        }

        // 优先级 2: 选中的内置线路
        if (!string.IsNullOrWhiteSpace(_config.SelectedBuiltinRoute))
        {
            var assetsPath = Path.Combine(AppContext.BaseDirectory, "GameTask", "AutoHoeing", "Assets");
            var selectedPath = Path.Combine(assetsPath, _config.SelectedBuiltinRoute);

            if (Directory.Exists(selectedPath))
            {
                _logger.LogInformation("[线路加载] 使用内置线路: {Route}", _config.SelectedBuiltinRoute);
                return LoadFixedDebugRoutes(selectedPath);
            }
            else
            {
                _logger.LogWarning("[线路加载] 选中的内置线路不存在: {Route}，回退到默认行为", _config.SelectedBuiltinRoute);
            }
        }

        // 优先级 3: 默认行为（DebugRoutes 目录）
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "GameTask", "AutoHoeing", "Assets", "DebugRoutes");
        _logger.LogInformation("[线路加载] 使用默认 DebugRoutes 目录");
        return LoadFixedDebugRoutes(defaultPath);
    }

    /// <summary>
    /// 从固定目录加载调试线路，按文件名排序，全部标记为 Selected 且 Group=目标组
    /// </summary>
    private List<RouteInfo> LoadFixedDebugRoutes(string dirPath)
    {
        var routes = new List<RouteInfo>();
        if (!Directory.Exists(dirPath))
        {
            _logger.LogError("[固定调试线路] 目录不存在: {Dir}", dirPath);
            return routes;
        }

        var files = Directory.GetFiles(dirPath, "*.json")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (int i = 0; i < files.Length; i++)
        {
            routes.Add(new RouteInfo
            {
                FileName = Path.GetFileName(files[i]),
                FullPath = files[i],
                Index = i + 1,
                Selected = true,
                Available = true,
                Group = _config.GroupIndex,
                EstimatedTime = 60,
                AdjustedTime = 60,
            });
        }

        return routes;
    }

    private void ValidateTeam()
    {
        if (_config.SkipValidation)
        {
            _logger.LogWarning("已跳过校验阶段");
            return;
        }

        // 基本校验（窗口分辨率等）
        var gameInfo = TaskContext.Instance().SystemInfo;
        if (gameInfo.CaptureAreaRect.Width != 1920 || gameInfo.CaptureAreaRect.Height != 1080)
        {
            _logger.LogWarning("游戏窗口非 1920×1080，可能导致图像识别失败");
        }
    }

    private void LoadGroupSettings(string accountName)
    {
        if (_config.GroupIndex == 1)
        {
            // 路径组一：保存配置
            var settings = new AccountGroupSettings
            {
                TagsForGroup1 = _config.TagsForGroup1,
                TagsForGroup2 = _config.TagsForGroup2,
                TagsForGroup3 = _config.TagsForGroup3,
                TagsForGroup4 = _config.TagsForGroup4,
                TagsForGroup5 = _config.TagsForGroup5,
                TagsForGroup6 = _config.TagsForGroup6,
                TagsForGroup7 = _config.TagsForGroup7,
                TagsForGroup8 = _config.TagsForGroup8,
                TagsForGroup9 = _config.TagsForGroup9,
                TagsForGroup10 = _config.TagsForGroup10,
                DisableSelfOptimization = _config.DisableSelfOptimization,
                EfficiencyIndex = _config.EfficiencyIndex,
                CuriosityFactor = _config.CuriosityFactor.ToString(),
                IgnoreRate = _config.IgnoreRate,
                TargetEliteNum = _config.TargetEliteNum,
                TargetMonsterNum = _config.TargetMonsterNum,
                PriorityTags = _config.PriorityTags,
                ExcludeTags = _config.ExcludeTags
            };
            var filePath = Path.Combine(_dataDir, "settings", $"{accountName}.json");
            settings.SaveToFile(filePath);
        }
        else
        {
            // 非路径组一：加载配置
            var filePath = Path.Combine(_dataDir, "settings", $"{accountName}.json");
            var settings = AccountGroupSettings.LoadFromFile(filePath);
            if (settings != null)
            {
                _config.TagsForGroup1 = settings.TagsForGroup1;
                _config.TagsForGroup2 = settings.TagsForGroup2;
                _config.TagsForGroup3 = settings.TagsForGroup3;
                _config.TagsForGroup4 = settings.TagsForGroup4;
                _config.TagsForGroup5 = settings.TagsForGroup5;
                _config.TagsForGroup6 = settings.TagsForGroup6;
                _config.TagsForGroup7 = settings.TagsForGroup7;
                _config.TagsForGroup8 = settings.TagsForGroup8;
                _config.TagsForGroup9 = settings.TagsForGroup9;
                _config.TagsForGroup10 = settings.TagsForGroup10;
                _config.EfficiencyIndex = settings.EfficiencyIndex;
                _config.IgnoreRate = settings.IgnoreRate;
                _config.TargetEliteNum = settings.TargetEliteNum;
                _config.TargetMonsterNum = settings.TargetMonsterNum;
                _config.PriorityTags = settings.PriorityTags;
                _config.ExcludeTags = settings.ExcludeTags;
            }
            else
            {
                _logger.LogError("配置文件不存在，请先在路径组一运行一次");
            }
        }
    }

    private List<List<string>> BuildGroupTags()
    {
        var groupSettings = new[]
        {
            _config.TagsForGroup1, _config.TagsForGroup2, _config.TagsForGroup3,
            _config.TagsForGroup4, _config.TagsForGroup5, _config.TagsForGroup6,
            _config.TagsForGroup7, _config.TagsForGroup8, _config.TagsForGroup9,
            _config.TagsForGroup10
        };

        var groupTags = groupSettings
            .Select(s => ParseChineseTags(s))
            .ToList();

        // 第0组 = 所有组标签的并集去重
        groupTags[0] = groupTags.SelectMany(t => t).Distinct().ToList();

        return groupTags;
    }

    private static List<string> ParseChineseTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new();
        return input.Split('，')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>
    /// 将配置组传入的覆盖值应用到当前配置
    /// </summary>
    private void ApplySettingsOverride()
    {
        if (_settingsOverride == null) return;

        T Get<T>(string key, T fallback)
        {
            if (_settingsOverride.TryGetValue(key, out var val) && val != null)
            {
                try
                {
                    // 处理 JsonElement 类型（System.Text.Json 反序列化 Dictionary<string, object?> 时的默认类型）
                    if (val is JsonElement jsonElement)
                    {
                        val = ConvertJsonElement(jsonElement);
                        if (val == null) return fallback;
                    }

                    // 处理 double -> int 转换时的 NaN/Infinity 和精度问题
                    if (typeof(T) == typeof(int) && val is double d)
                    {
                        // 处理 NaN/Infinity：直接返回 fallback，不做类型转换
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            return fallback;
                        // 处理浮点数精度问题：如果值接近整数，进行取整
                        if (Math.Abs(d - Math.Round(d)) < 1e-9)
                            return (T)(object)Convert.ToInt32(Math.Round(d));
                        return (T)(object)Convert.ToInt32(d);
                    }
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch { return fallback; }
            }
            return fallback;
        }

        // 将 JsonElement 转换为实际的值类型
        static object? ConvertJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        // groupIndex: 下拉框值是"路径组一"~"路径组十"，需要转换为数字1-10
        var groupIndexStr = Get("groupIndex", "");
        if (!string.IsNullOrEmpty(groupIndexStr))
        {
            var groupMap = new Dictionary<string, int>
            {
                ["路径组一"] = 1, ["路径组二"] = 2, ["路径组三"] = 3, ["路径组四"] = 4, ["路径组五"] = 5,
                ["路径组六"] = 6, ["路径组七"] = 7, ["路径组八"] = 8, ["路径组九"] = 9, ["路径组十"] = 10
            };
            if (groupMap.TryGetValue(groupIndexStr, out var idx))
                _config.GroupIndex = idx;
        }

        _config.OperationMode = Get("operationMode", _config.OperationMode);
        _config.PartyName = Get("partyName", _config.PartyName);
        _config.SortMode = Get("sortMode", _config.SortMode);
        _config.PickupMode = Get("pickupMode", _config.PickupMode);
        _config.UseRouteRelatedMaterialsOnly = Get("useRouteRelatedMaterialsOnly", _config.UseRouteRelatedMaterialsOnly);
        _config.DisableSecondaryValidation = Get("disableSecondaryValidation", _config.DisableSecondaryValidation);
        _config.DumperCharacters = Get("dumperCharacters", _config.DumperCharacters);
        _config.CookingNames = Get("cookingNames", _config.CookingNames);
        _config.NoRunPeriod = Get("noRunPeriod", _config.NoRunPeriod);
        _config.FindFInterval = Get("findFInterval", _config.FindFInterval);
        _config.PickupDelay = Get("pickupDelay", _config.PickupDelay);
        _config.RollingDelay = Get("rollingDelay", _config.RollingDelay);
        _config.ScrollCycle = Get("scrollCycle", _config.ScrollCycle);
        _config.LogMonsterCount = Get("logMonsterCount", _config.LogMonsterCount);
        _config.DisableAsync = Get("disableAsync", _config.DisableAsync);
        _config.EnableCoordinateCheck = Get("enableCoordinateCheck", _config.EnableCoordinateCheck);
        _config.SkipValidation = Get("skipValidation", _config.SkipValidation);
        _config.AccountName = Get("accountName", _config.AccountName);
        _config.TagsForGroup1 = Get("tagsForGroup1", _config.TagsForGroup1);
        _config.TagsForGroup2 = Get("tagsForGroup2", _config.TagsForGroup2);
        _config.TagsForGroup3 = Get("tagsForGroup3", _config.TagsForGroup3);
        _config.TagsForGroup4 = Get("tagsForGroup4", _config.TagsForGroup4);
        _config.TagsForGroup5 = Get("tagsForGroup5", _config.TagsForGroup5);
        _config.TagsForGroup6 = Get("tagsForGroup6", _config.TagsForGroup6);
        _config.TagsForGroup7 = Get("tagsForGroup7", _config.TagsForGroup7);
        _config.TagsForGroup8 = Get("tagsForGroup8", _config.TagsForGroup8);
        _config.TagsForGroup9 = Get("tagsForGroup9", _config.TagsForGroup9);
        _config.TagsForGroup10 = Get("tagsForGroup10", _config.TagsForGroup10);
        _config.DisableSelfOptimization = Get("disableSelfOptimization", _config.DisableSelfOptimization);
        _config.EfficiencyIndex = Get("efficiencyIndex", _config.EfficiencyIndex);
        _config.CuriosityFactor = Get("curiosityFactor", _config.CuriosityFactor);
        _config.IgnoreRate = Get("ignoreRate", _config.IgnoreRate);
        _config.TargetEliteNum = Get("targetEliteNum", _config.TargetEliteNum);
        _config.TargetMonsterNum = Get("targetMonsterNum", _config.TargetMonsterNum);
        _config.PriorityTags = Get("priorityTags", _config.PriorityTags);
        _config.ExcludeTags = Get("excludeTags", _config.ExcludeTags);
        _config.TargetMonsters = Get("targetMonsters", _config.TargetMonsters);

        // 联机配置
        _config.MultiplayerEnabled = Get("multiplayerEnabled", _config.MultiplayerEnabled);
        _config.MultiplayerPartyName = Get("multiplayerPartyName", _config.MultiplayerPartyName);
        _config.MultiplayerStartAvatarName = Get("multiplayerStartAvatarName", _config.MultiplayerStartAvatarName);
        _config.MultiplayerRole = _settingsOverride.ContainsKey("multiplayerRole")
            ? Get("multiplayerRole", _config.MultiplayerRole)
            : _config.MultiplayerRole;
        _config.MemberJoinMode = _settingsOverride.ContainsKey("memberJoinMode")
            ? Get("memberJoinMode", _config.MemberJoinMode)
            : _config.MemberJoinMode;
        _config.TargetHostName = Get("targetHostName", _config.TargetHostName);
        _config.CoordinatorServerUrl = Get("coordinatorServerUrl", _config.CoordinatorServerUrl);
        _config.PlayerName = Get("playerName", _config.PlayerName);
        _config.PlayerUid = Get("playerUid", _config.PlayerUid);
        _config.ExpectedPlayerCount = Get("expectedPlayerCount", _config.ExpectedPlayerCount);
        _config.RoomWhitelist = Get("roomWhitelist", _config.RoomWhitelist);
        _config.PartyTimeoutSeconds = Get("partyTimeoutSeconds", _config.PartyTimeoutSeconds);
        _config.PartyTimeoutAction = Get("partyTimeoutAction", _config.PartyTimeoutAction);

        if (_config.MultiplayerEnabled)
        {
            // 联机模式：应用联机专属字段
            _config.SyncTimeoutSeconds = Get("syncTimeoutSeconds", _config.SyncTimeoutSeconds);
            _config.MinPlayersToSync = Get("minPlayersToSync", _config.MinPlayersToSync);
            _config.SyncPointMinDistance = Get("syncPointMinDistance", _config.SyncPointMinDistance);
            _config.KazuhaPlayerIndex = Get("kazuhaPlayerIndex", _config.KazuhaPlayerIndex);
            _config.ReturnToFightPointAfterBattle = Get("returnToFightPointAfterBattle", _config.ReturnToFightPointAfterBattle);
            _config.ReturnToFightPointStaySeconds = Get("returnToFightPointStaySeconds", _config.ReturnToFightPointStaySeconds);
            _config.FightTimeoutSeconds = Get("fightTimeoutSeconds", _config.FightTimeoutSeconds);
        }
        else
        {
            // 单机模式：重置真正的联机专属字段为安全默认值，避免全局配置残留影响
            _config.SyncTimeoutSeconds = 60;
            _config.MinPlayersToSync = 0;
            _config.SyncPointMinDistance = 30.0;
            _config.KazuhaPlayerIndex = 0;
            _config.ReturnToFightPointAfterBattle = false;
            _config.ReturnToFightPointStaySeconds = 5;
            _config.FightTimeoutSeconds = 120;

            // 单机模式：重置固定调试线路字段，避免联机全局配置残留影响
            // 如果 settings 显式包含这些键，后续 ContainsKey 逻辑会覆盖回来
            if (!_settingsOverride!.ContainsKey("useFixedDebugRoutes"))
                _config.UseFixedDebugRoutes = false;
            if (!_settingsOverride.ContainsKey("fixedDebugRoutePath"))
                _config.FixedDebugRoutePath = "";
            if (!_settingsOverride.ContainsKey("selectedBuiltinRoute"))
                _config.SelectedBuiltinRoute = "";
        }

        // 单机和联机均支持的字段
        _config.StartRouteIndex = Get("startRouteIndex", _config.StartRouteIndex);
        _config.DebugMode = Get("debugMode", _config.DebugMode);
        if (_settingsOverride.ContainsKey("useFixedDebugRoutes"))
            _config.UseFixedDebugRoutes = Get("useFixedDebugRoutes", _config.UseFixedDebugRoutes);
        if (_settingsOverride.ContainsKey("fixedDebugRoutePath"))
            _config.FixedDebugRoutePath = Get("fixedDebugRoutePath", _config.FixedDebugRoutePath);
        if (_settingsOverride.ContainsKey("selectedBuiltinRoute"))
            _config.SelectedBuiltinRoute = Get("selectedBuiltinRoute", _config.SelectedBuiltinRoute);

        _config.MultiWorldEnabled = Get("multiWorldEnabled", _config.MultiWorldEnabled);
        _config.MultiWorldCount = Get("multiWorldCount", _config.MultiWorldCount);
    }

    /// <summary>
    /// 获取可配置参数定义（供UI编辑使用），顺序和说明与JS settings.json一致
    /// </summary>
    public static List<SoloTaskSettingItem> GetSettingDefinitions()
    {
        var config = TaskContext.Instance().Config.AutoHoeingConfig;
        // groupIndex: 数字转为下拉框选项
        var groupNames = new[] { "路径组一","路径组二","路径组三","路径组四","路径组五","路径组六","路径组七","路径组八","路径组九","路径组十" };
        var currentGroup = config.GroupIndex >= 1 && config.GroupIndex <= 10 ? groupNames[config.GroupIndex - 1] : "路径组一";

        return new List<SoloTaskSettingItem>
        {
            // ===== 第一部分：路径组执行配置 =====
            new() { Name = "operationMode", Label = "执行模式", Type = "select", DefaultValue = config.OperationMode,
                Options = new() { "运行锄地路线", "调试路线分配", "强制刷新所有运行记录", "启用仅指定怪物模式" } },
            new() { Name = "groupIndex", Label = "选择执行第几个路径组", Type = "select", DefaultValue = currentGroup,
                Options = new(groupNames) },
            new() { Name = "partyName", Label = "本路径组使用配队名称\n【注意】请只在这里填写要使用的配队，配置组中配队项留空", Type = "text", DefaultValue = config.PartyName },
            new() { Name = "sortMode", Label = "组内路线排序模式", Type = "select", DefaultValue = config.SortMode,
                Options = new() { "原文件顺序", "效率降序", "高收益优先" } },
            new() { Name = "pickupMode", Label = "拾取模式\n【注意】bgi原版拾取性能开销大，准确低，尽量不要使用", Type = "select", DefaultValue = config.PickupMode,
                Options = new() { "模板匹配拾取狗粮和怪物材料", "模板匹配仅拾取狗粮", "BGI原版拾取", "不拾取" } },
            new() { Name = "useRouteRelatedMaterialsOnly", Label = "只使用路线相关怪物材料进行识别，提高性能\n仅在选择模板匹配拾取狗粮和怪物材料时生效\n推荐先不勾选运行一段时间获取历史数据后勾选", Type = "bool", DefaultValue = config.UseRouteRelatedMaterialsOnly },
            new() { Name = "disableSecondaryValidation", Label = "禁用识别到物品后的二次校验，可能增加误捡概率", Type = "bool", DefaultValue = config.DisableSecondaryValidation },
            new() { Name = "dumperCharacters", Label = "泥头车模式，将在接近战斗点前提前释放部分角色E技能\n需要启用时填写角色在队伍中的编号，多个用中文逗号分隔\n【注意】精英路线启用泥头车将有可能导致狗粮损失", Type = "text", DefaultValue = config.DumperCharacters },
            new() { Name = "cookingNames", Label = "使用料理名称，将在路线之间尝试使用对应名称的料理\n多个料理名称之间使用中文逗号分隔，使用间隔为300秒", Type = "text", DefaultValue = config.CookingNames },
            new() { Name = "noRunPeriod", Label = "不运行时段\n示例：单个小时：8  连续区间：8-11 或 23:11-23:55\n多项用中文逗号分隔，留空=全天可运行", Type = "text", DefaultValue = config.NoRunPeriod },
            new() { Name = "findFInterval", Label = "识别间隔(ms)，两次检测F图标之间等待时间，建议10-200", Type = "number", DefaultValue = config.FindFInterval },
            new() { Name = "pickupDelay", Label = "拾取后延时(ms)，连续拾取相同物品时建议调大，建议32-200", Type = "number", DefaultValue = config.PickupDelay },
            new() { Name = "rollingDelay", Label = "滚动后延时(ms)，拾取错误时建议调大，建议16-100", Type = "number", DefaultValue = config.RollingDelay },
            new() { Name = "scrollCycle", Label = "单次滚动周期(ms)，上下滚动不全时建议调大，建议800-2000", Type = "number", DefaultValue = config.ScrollCycle },
            new() { Name = "logMonsterCount", Label = "运行路线时输出交互或拾取精英和小怪数量，便于在日志分析中比对", Type = "bool", DefaultValue = config.LogMonsterCount },
            new() { Name = "disableAsync", Label = "禁用异步操作，设备性能过低换队受影响时选择性勾选", Type = "bool", DefaultValue = config.DisableAsync },
            new() { Name = "enableCoordinateCheck", Label = "路线结尾时进行坐标检查\n用于在路线出现卡死等放弃时不记录CD信息", Type = "bool", DefaultValue = config.EnableCoordinateCheck },
            new() { Name = "skipValidation", Label = "跳过校验阶段\n确认跳过校验阶段，任何包括但不限于漏怪、卡死、不拾取等问题均由自己配置引起", Type = "bool", DefaultValue = config.SkipValidation },

            // ===== 第二部分：路线选择与分组配置 =====
            new() { Name = "accountName", Label = "账户名称\n用于多用户运行时区分不同账户的记录，单用户请勿修改", Type = "text", DefaultValue = config.AccountName },
            new() { Name = "tagsForGroup1", Label = "路径组一要【排除】的标签\n允许使用的标签：水免，次数盾，高危，传奇，蕈兽，小怪，沙暴，狭窄地形，环境伤害\n多个标签使用中文逗号分隔", Type = "text", DefaultValue = config.TagsForGroup1 },
            new() { Name = "tagsForGroup2", Label = "路径组二要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup2 },
            new() { Name = "tagsForGroup3", Label = "路径组三要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup3 },
            new() { Name = "tagsForGroup4", Label = "路径组四要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup4 },
            new() { Name = "disableSelfOptimization", Label = "禁用根据运行记录优化路线选择的功能\n完全使用路线原有信息", Type = "bool", DefaultValue = config.DisableSelfOptimization },
            new() { Name = "efficiencyIndex", Label = "摩拉/耗时权衡因数，填0及以上的数字\n越大越倾向于花费较多时间提高总收益\n含义为愿意为1摩拉多花多少秒", Type = "number", DefaultValue = config.EfficiencyIndex },
            new() { Name = "curiosityFactor", Label = "好奇系数，缺少记录的路线预期用时将被削减对应比例\n填0-1之间的数", Type = "number", DefaultValue = config.CuriosityFactor },
            new() { Name = "ignoreRate", Label = "小怪数量/精英数量大于该值的路线将被视为纯小怪路线\n忽略其中包含的精英", Type = "number", DefaultValue = config.IgnoreRate },
            new() { Name = "targetEliteNum", Label = "目标精英数量", Type = "number", DefaultValue = config.TargetEliteNum },
            new() { Name = "targetMonsterNum", Label = "目标小怪数量", Type = "number", DefaultValue = config.TargetMonsterNum },
            new() { Name = "priorityTags", Label = "优先关键词，含关键词的路线会被视为最高效率\n不同关键词使用中文逗号分隔\n仅优先选择，不影响路线排序", Type = "text", DefaultValue = config.PriorityTags },
            new() { Name = "excludeTags", Label = "排除关键词，含关键词的路线会被完全排除\n不同关键词使用中文逗号分隔", Type = "text", DefaultValue = config.ExcludeTags },
            new() { Name = "tagsForGroup5", Label = "路径组五要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup5 },
            new() { Name = "tagsForGroup6", Label = "路径组六要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup6 },
            new() { Name = "tagsForGroup7", Label = "路径组七要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup7 },
            new() { Name = "tagsForGroup8", Label = "路径组八要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup8 },
            new() { Name = "tagsForGroup9", Label = "路径组九要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup9 },
            new() { Name = "tagsForGroup10", Label = "路径组十要【选择】的标签", Type = "text", DefaultValue = config.TagsForGroup10 },

            // ===== 第三部分：仅指定怪物模式 =====
            new() { Name = "targetMonsters", Label = "目标怪物\n建议按照怪物图鉴中的名字填写，有多个目标时使用中文逗号分隔", Type = "text", DefaultValue = config.TargetMonsters },

            // ===== 第四部分：联机队伍和角色准备 =====
            new() { Name = "multiplayerPartyName", Label = "联机队伍名称\n留空则使用当前队伍", Type = "text", DefaultValue = config.MultiplayerPartyName },
            new() { Name = "multiplayerStartAvatarName", Label = "联机起始角色名称\n留空则使用当前角色，如：钟离、纳西妲", Type = "text", DefaultValue = config.MultiplayerStartAvatarName },

            // ===== 第五部分：联机角色配置 =====
            new() { Name = "multiplayerRole", Label = "联机角色", Type = "select", DefaultValue = config.MultiplayerRole,
                Options = new() { "host", "member" } },
            new() { Name = "memberJoinMode", Label = "成员加入方式", Type = "select", DefaultValue = config.MemberJoinMode,
                Options = new() { "byHostName", "random" } },
            new() { Name = "targetHostName", Label = "目标房主玩家名称\n仅在成员模式且加入方式为byHostName时生效", Type = "text", DefaultValue = config.TargetHostName },

            // ===== 第六部分：联机服务器和同步配置 =====
            new() { Name = "coordinatorServerUrl", Label = "协调服务器地址", Type = "text", DefaultValue = config.CoordinatorServerUrl },
            new() { Name = "playerName", Label = "玩家名称\n联机时显示给其他玩家", Type = "text", DefaultValue = config.PlayerName },
            new() { Name = "playerUid", Label = "玩家UID\n用于进入世界和多世界切换", Type = "text", DefaultValue = config.PlayerUid },
            new() { Name = "expectedPlayerCount", Label = "房间期望人数（2-4）\n用于判断人齐条件", Type = "number", DefaultValue = config.ExpectedPlayerCount },
            new() { Name = "roomWhitelist", Label = "房间白名单\n逗号分隔的玩家名称", Type = "text", DefaultValue = config.RoomWhitelist },
            new() { Name = "partyTimeoutSeconds", Label = "组队等待超时（秒）\n超时后根据超时动作处理", Type = "number", DefaultValue = config.PartyTimeoutSeconds },
            new() { Name = "partyTimeoutAction", Label = "组队超时动作", Type = "select", DefaultValue = config.PartyTimeoutAction.ToString(),
                Options = new() { "0", "1" } },
            new() { Name = "syncTimeoutSeconds", Label = "集合点等待超时（秒）", Type = "number", DefaultValue = config.SyncTimeoutSeconds },
            new() { Name = "minPlayersToSync", Label = "最低开始人数\n低于此人数时集合点直接放行，0=自动等齐所有人", Type = "number", DefaultValue = config.MinPlayersToSync },
            new() { Name = "kazuhaPlayerIndex", Label = "万叶玩家序号\n0=不指定，1-4=对应玩家序号", Type = "number", DefaultValue = config.KazuhaPlayerIndex },
            new() { Name = "returnToFightPointAfterBattle", Label = "战斗完成后是否走回战斗点集合", Type = "bool", DefaultValue = config.ReturnToFightPointAfterBattle },
            new() { Name = "returnToFightPointStaySeconds", Label = "走回战斗点后停留时间（秒）\n等待其他玩家拾取", Type = "number", DefaultValue = config.ReturnToFightPointStaySeconds },
            new() { Name = "syncPointMinDistance", Label = "集合点与战斗点的最小距离阈值\n小于此距离的点不作为集合点", Type = "number", DefaultValue = config.SyncPointMinDistance },

            // ===== 联机战斗配置 =====
            new() { Name = "fightTimeoutSeconds", Label = "联机战斗超时时间（秒）\n由房主设定并同步给所有成员，覆盖各自的自动战斗超时", Type = "number", DefaultValue = config.FightTimeoutSeconds },

            // ===== 第七部分：多世界连续锄地配置 =====
            new() { Name = "multiWorldEnabled", Label = "启用多世界连续锄地\n房主设定，完成一个世界后轮换到下一个玩家的世界", Type = "bool", DefaultValue = config.MultiWorldEnabled },
            new() { Name = "multiWorldCount", Label = "多世界锄地轮数（1-4）\n由房主设定，按加入顺序依次成为房主", Type = "number", DefaultValue = config.MultiWorldCount },
        };
    }

    /// <summary>
    /// 联机前检查：若当前已在多人联机世界，则先退出回到自己世界。
    /// 流程：返回主界面 → 截图判断 IsInMultiGame（带 1 次重试）→ 若是则调 LeaveWorldAsync 退出。
    /// 任何异常都不阻断后续流程，仅记日志，由后续步骤继续兜底。
    /// </summary>
    private async Task EnsureInOwnWorldBeforeMultiplayerAsync()
    {
        try
        {
            // 1. 回到主界面（DetectedMultiGameStatus 依赖右侧 HUD / 左上角图标可见）
            try
            {
                await new ReturnMainUiTask().Start(_ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[联机] 检测多人世界前回到主界面失败，继续尝试检测");
            }
            await Task.Delay(500, _ct);

            // 2. 检测多人状态（最多 2 次，避免单次模板匹配漏识造成误判）
            bool isInMultiGame = false;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var ra = CaptureToRectArea();
                    var status = PartyAvatarSideIndexHelper.DetectedMultiGameStatus(ra);
                    if (status.IsInMultiGame)
                    {
                        _logger.LogInformation("[联机] 检测到当前处于多人联机世界（第{N}次检测，房主={IsHost}，人数={Count}），先退出多人世界",
                            attempt, status.IsHost, status.PlayerCount);
                        isInMultiGame = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[联机] 多人世界检测异常（第{N}次），重试", attempt);
                }
                if (attempt < 2)
                    await Task.Delay(500, _ct);
            }

            if (!isInMultiGame)
            {
                _logger.LogDebug("[联机] 未检测到多人联机世界，跳过退出步骤");
                return;
            }

            // 3. 退出多人世界（带 60s 兜底超时，避免极端情况下卡死整个任务）
            using var leaveCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
            leaveCts.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                var autoParty = new AutoPartyTask();
                var left = await autoParty.LeaveWorldAsync(leaveCts.Token);
                if (left)
                    _logger.LogInformation("[联机] 已退出多人世界，回到自己的世界");
                else
                    _logger.LogWarning("[联机] 退出多人世界未确认成功，后续步骤将继续尝试");
            }
            catch (OperationCanceledException) when (!_ct.IsCancellationRequested)
            {
                _logger.LogWarning("[联机] 退出多人世界超时（60s），后续步骤将继续尝试");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 联机前多人世界检查异常，忽略并继续");
        }
    }

    /// <summary>
    /// 设置世界权限为确认后才能加入
    /// 参考 AutoPermission JS 脚本的点击坐标（基于1920x1080）
    /// </summary>
    private async Task SetWorldPermissionToConfirmJoin()
    {
        if (_worldPermissionSet)
        {
            _logger.LogInformation("[联机] 世界权限已设置过，跳过");
            return;
        }
        try
        {
            // 返回主界面再操作
            var returnMainUiTask = new ReturnMainUiTask();
            await returnMainUiTask.Start(_ct);
            
            _logger.LogInformation("[联机] 开始设置世界权限为确认后才能加入");
            // 按F2打开联机界面
            Simulation.SendInput.SimulateAction(GIActions.OpenCoOpScreen);
            await Task.Delay(1500, _ct);
            
            // 点击"世界权限"选项（坐标来自 AutoPermission JS 脚本）
            GameCaptureRegion.GameRegion1080PPosClick(330, 1010);
            await Task.Delay(800, _ct);
            
            // 点击"确认后可加入"选项
            GameCaptureRegion.GameRegion1080PPosClick(330, 960);
            await Task.Delay(500, _ct);
            
            // 按ESC关闭联机界面
            Simulation.SendInput.SimulateAction(GIActions.OpenCoOpScreen);
            await Task.Delay(800, _ct);
            
            _logger.LogInformation("[联机] 世界权限已设置为确认后才能加入");
            _worldPermissionSet = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机] 设置世界权限时发生异常，将继续执行");
        }
    }

    /// <summary>
    /// 获取当前世界轮次（skip-route-wait-point-report 修复）
    /// </summary>
    private int GetCurrentWorldRound()
    {
        try
        {
            // 从WaitPointStateManager获取当前轮次
            if (_multiplayerCoordinator != null)
            {
                // 通过状态统计获取当前轮次
                var stats = _multiplayerCoordinator.StateManager.GetStatistics();
                int currentRound = stats.CurrentWorldRound;
                _logger.LogDebug("[联机] 获取当前世界轮次: {Round}", currentRound);
                return currentRound;
            }
            
            // 默认返回1（第一轮）
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[联机] 获取当前世界轮次时发生异常");
            return 1; // 异常时返回默认值
        }
    }

    /// <summary>
    /// 获取下一个同步点用于等待点上报（skip-route-wait-point-report 修复）
    /// 优先选择"传送必同步"的同步点
    /// 使用实际执行的路线列表（groupRoutes）而非配置的路线列表
    /// </summary>
    private async Task<string> GetNextSyncPointForWaitPointAsync(int currentRouteIndex, List<RouteInfo> actualRoutes)
    {
        try
        {
            // 获取下一个路线的索引
            int nextRouteIndex = currentRouteIndex + 1;
            
            // 使用实际执行的路线列表
            var routes = actualRoutes;
            if (nextRouteIndex >= routes.Count)
            {
                _logger.LogWarning("[联机] 下一个路线索引 {NextIndex} 超出范围（总路线数: {Total}）", 
                    nextRouteIndex, routes.Count);
                return string.Empty;
            }
            
            var nextRoute = routes[nextRouteIndex];
            _logger.LogDebug("[联机] 获取下一个同步点：路线索引={Index}, 文件名={FileName}", 
                nextRouteIndex, nextRoute.FileName);
            
            // 传送必同步：始终选择第一个传送点作为同步点
            // 同步点ID格式：{FileName}_tp_{listIdx}_{wpIdx}
            _logger.LogInformation("[联机] 传送必同步（默认启用），选择传送点作为等待点");
            return $"{nextRoute.FileName}_tp_0_0"; // 第一个路线的第一个传送点
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[联机] 获取下一个同步点时发生异常");
            return string.Empty;
        }
    }
}