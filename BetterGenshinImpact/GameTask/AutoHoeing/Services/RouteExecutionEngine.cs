using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 路线执行引擎：调用PathExecutor执行地图追踪，并发运行拾取/异常检测/泥头车子任务
/// </summary>
public class RouteExecutionEngine
{
    private static readonly ILogger Logger = App.GetLogger<RouteExecutionEngine>();

    private readonly TemplatePickupService _pickupService;
    private readonly AnomalyDetector _anomalyDetector;
    private readonly DumperService _dumperService;
    private readonly BlacklistManager _blacklistManager;
    private readonly AutoHoeingConfig _config;
    private readonly PathingPartyConfig? _partyConfig;

    private volatile bool _running;
    private MultiplayerCoordinator? _coordinator;
    private WorldStateMonitor? _worldStateMonitor;

    // 当前正在执行的 PathExecutor 引用（联机模式下供 AnomalyDetector 信号传递使用）
    private PathExecutor? _activeExecutor;

    public void SetCoordinator(MultiplayerCoordinator? coordinator)
    {
        _coordinator = coordinator;
        
        // 设置异常检测器的复苏回调
        if (coordinator != null)
        {
            _anomalyDetector.OnRevivalDetected = async () =>
            {
                // 不在这里上报 Reviving，避免覆盖 targetProgress 为 -1。
                // 复苏后会触发 RetryException，由 PathExecutor 的 catch 块上报带正确 targetProgress 的 Reviving。
                Logger.LogInformation("[联机] 检测到复苏，等待 RetryException 路径上报 Reviving");
                await Task.CompletedTask;
            };

            // 联机模式专用：色块检测到"已倒下"时，向当前 PathExecutor 发信号，
            // 让其在主循环抛 RetryException，进入"同步点前/后"统一异常处理流程。
            // 注意：这个回调只对联机的色块检测生效（IsMultiplayerDefeated），
            // 单机的模板匹配复苏走的是另一条 OnRevivalDetected 回调，不受影响。
            _anomalyDetector.OnMultiplayerDefeatedDetected = () =>
            {
                _activeExecutor?.SignalMultiplayerRevival();
            };
        }
        else
        {
            _anomalyDetector.OnRevivalDetected = null;
            _anomalyDetector.OnMultiplayerDefeatedDetected = null;
        }
    }
    public void SetWorldStateMonitor(WorldStateMonitor? monitor) => _worldStateMonitor = monitor;

    public RouteExecutionEngine(
        TemplatePickupService pickupService,
        AnomalyDetector anomalyDetector,
        DumperService dumperService,
        BlacklistManager blacklistManager,
        AutoHoeingConfig config,
        PathingPartyConfig? partyConfig = null)
    {
        _pickupService = pickupService;
        _anomalyDetector = anomalyDetector;
        _dumperService = dumperService;
        _blacklistManager = blacklistManager;
        _config = config;
        _partyConfig = partyConfig;
    }

    /// <summary>
    /// 执行单条路线，并发启动所有子任务
    /// </summary>
    public async Task<RouteExecutionResult> ExecuteRoute(
        RouteInfo route, CancellationToken ct, int currentJsonRouteIndex = 0)
    {
        var result = new RouteExecutionResult();
        _running = true;
        _anomalyDetector.ShouldSwitchFurina = false;

        // 设置路线相关材料过滤
        if (_config.UseRouteRelatedMaterialsOnly)
            _pickupService.SetRouteRelatedMaterials(route.MonsterInfo, route.PickupHistory);
        else
            _pickupService.ResetAllEnabled();

        var sw = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedCt = cts.Token;

        bool IsRunning() => _running && !linkedCt.IsCancellationRequested;

        bool pathingFullyCompleted = false;
        bool skipRouteRequested = false;
        string? skipRouteReason = null;

        // 主路线执行任务
        var pathingTask = Task.Run(async () =>
        {
            try
            {
                Logger.LogInformation("开始执行路线: {Name}", route.FileName);
                var task = PathingTask.BuildFromFilePath(route.FullPath);
                if (task != null)
                {
                    var executor = new PathExecutor(ct);
                    executor.PartyConfig = _partyConfig;
                    executor.CurrentJsonRouteIndex = currentJsonRouteIndex;
                    
                    // 联机模式：注入 MultiplayerCoordinator，并禁用自动领取派遣
                    if (_config.MultiplayerEnabled && _coordinator != null)
                    {
                        executor.MultiplayerCoordinator = _coordinator;
                        executor.WorldStateMonitor = _worldStateMonitor;
                        PathExecutor.CurrentWorldStateMonitor = _worldStateMonitor;
                        executor.PartyConfig.DisableAutoFetchDispatch = true;
                        Logger.LogInformation("[联机] 已注入 MultiplayerCoordinator 到 PathExecutor，路线: {Name}", route.FileName);
                    }
                    else
                    {
                        Logger.LogDebug("[联机] MultiplayerEnabled={Enabled}，coordinator={HasCoord}，单机模式执行",
                            _config.MultiplayerEnabled, _coordinator != null);
                    }
                    
                    // 注册当前 executor，供 AnomalyDetector 异步信号使用
                    _activeExecutor = executor;
                    try
                    {
                        Logger.LogInformation("[DEBUG] 开始调用 executor.Pathing，路线: {Name}", route.FileName);
                        await executor.Pathing(task);
                        Logger.LogInformation("[DEBUG] executor.Pathing 完成，SuccessEnd={End}，路线: {Name}", executor.SuccessEnd, route.FileName);
                        pathingFullyCompleted = executor.SuccessEnd;

                        // 联机模式：传递路线跳过标志位（需求 1）
                        if (executor.SkipRouteRequested)
                        {
                            skipRouteRequested = true;
                            skipRouteReason = executor.SkipRouteReason;
                            pathingFullyCompleted = false; // 跳过的路线不算完整完成
                            Logger.LogInformation("[联机] 路线 {Name} 被标记为跳过: {Reason}", route.FileName, skipRouteReason);
                        }
                    }
                    finally
                    {
                        // 路线结束（含异常路径）解除引用，避免下一条路线之前 AnomalyDetector 误信号到旧 executor
                        if (ReferenceEquals(_activeExecutor, executor))
                        {
                            _activeExecutor = null;
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("[DEBUG] BuildFromFilePath 返回 null，路线: {Name}", route.FileName);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 让取消异常穿透，不吞掉
            }
            catch (Exception ex)
            {
                Logger.LogError("执行地图追踪出错: {Msg}", ex.Message);
            }
            finally
            {
                _running = false;
            }
        }, linkedCt);

        // 并发子任务列表
        var tasks = new List<Task> { pathingTask };

        // 模板匹配拾取
        if (_config.PickupMode.Contains("模板匹配"))
        {
            tasks.Add(Task.Run(() => _pickupService.RunPickupLoop(
                IsRunning, _blacklistManager.Blacklist,
                _config.PickupDelay, _config.RollingDelay,
                _config.ScrollCycle, _config.FindFInterval,
                linkedCt), linkedCt));
        }

        // 异常状态检测
        tasks.Add(Task.Run(() => _anomalyDetector.RunDetectionLoop(IsRunning, linkedCt), linkedCt));

        // 黑名单检测
        if (_config.PickupMode.Contains("模板匹配"))
        {
            tasks.Add(Task.Run(() => _blacklistManager.RunDetectionLoop(
                IsRunning, _pickupService.TargetItems.ToList(), linkedCt), linkedCt));
        }

        // 泥头车
        var dumperChars = ParseDumperCharacters(_config.DumperCharacters);
        if (dumperChars.Count > 0)
        {
            var pathingData = PathingTask.BuildFromFilePath(route.FullPath);
            if (pathingData != null)
            {
                CombatScenes? combatScenes = null;
                try
                {
                    using var region = CaptureToRectArea();
                    combatScenes = new CombatScenes().InitializeTeam(region);
                    if (!combatScenes.CheckTeamInitialized())
                    {
                        Logger.LogWarning("泥头车队伍识别失败，跳过泥头车功能");
                        combatScenes.Dispose();
                        combatScenes = null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("泥头车CombatScenes初始化异常: {Msg}", ex.Message);
                    combatScenes?.Dispose();
                    combatScenes = null;
                }

                if (combatScenes != null)
                {
                    var cs = combatScenes;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _dumperService.RunDumperLoop(
                                pathingData.Positions, dumperChars, route.MapName,
                                cs, IsRunning, linkedCt);
                        }
                        finally
                        {
                            cs.Dispose();
                        }
                    }, linkedCt));
                }
            }
        }

        // 等待所有任务完成
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogDebug("并发任务异常: {Msg}", ex.Message);
        }

        sw.Stop();
        result.ActualDuration = sw.Elapsed.TotalSeconds;
        result.ShouldSwitchFurina = _anomalyDetector.ShouldSwitchFurina;
        result.Success = true;
        result.FullyCompleted = pathingFullyCompleted;
        result.SkipRouteRequested = skipRouteRequested;
        result.SkipRouteReason = skipRouteReason;

        return result;
    }

    private static List<int> ParseDumperCharacters(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new();
        return input.Split('，')
            .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
            .Where(n => n >= 1 && n <= 4)
            .ToList();
    }
}
