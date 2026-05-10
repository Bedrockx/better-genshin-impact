#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

// ========== 以下为空壳类，替代被删除的异常检测模块 ==========

// WaitPointStateManager.cs 的空壳
public class WaitPointStateManager : IDisposable
{
    public void SetWorldRound(int round) { }
    public void UpdateState(string playerUid, WaitPointState state) { }
    public WaitPointState? GetState(string playerUid) => null;
    public void RemoveState(string playerUid) { }
    public List<WaitPointState> GetAllValidStates() => new();
    public int GetAbnormalPlayersAtPoint(string syncPointId) => 0;
    public int GetAbnormalPlayerCount() => 0;
    public bool IsAbnormalPlayer(string playerUid) => false;
    public void ResetCurrentRound() { }
    public WaitPointStats GetStatistics() => new();
    public void Dispose() { }
}

// WaitPointState 的空壳
public class WaitPointState
{
    public string PlayerUid { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string SyncPointId { get; set; } = "";
    public int WorldRound { get; set; }
    public DateTime LastUpdated { get; set; }
}

// WaitPointStats 的空壳
public class WaitPointStats
{
    public int CurrentWorldRound { get; set; } = 1;
}

// RouteSyncCoordinator.cs 的空壳
public class RouteSyncCoordinator
{
    public RouteSyncCoordinator(CoordinatorClient client, MultiplayerCoordinator coordinator, AutoHoeingConfig config) { }
    public Task<RouteSyncDecision> CheckSyncAtRouteStart(int targetRouteIndex, CancellationToken ct)
        => Task.FromResult(new RouteSyncDecision { Action = RouteSyncAction.Proceed });
    public Task ReportRouteCompletion(int completedRouteIndex) => Task.CompletedTask;
    public void RefreshCache() { }
    public void Reset() { }
}

// RouteSyncDecision 的空壳
public class RouteSyncDecision
{
    public RouteSyncAction Action { get; set; }
    public string? TargetSyncPoint { get; set; }
    public int TargetRouteIndex { get; set; }
    public int SkipRouteCount { get; set; }
}

// RouteSyncAction 的空壳
public enum RouteSyncAction { Proceed, SkipAndCatchUp, ProceedAndWait }

// AbnormalStatusManager.cs 的空壳
public class AbnormalStatusManager
{
    public AbnormalStatusManager(MultiplayerCoordinator coordinator, WaitPointStateManager stateManager, AutoHoeingConfig config) { }
    public void MarkAbnormal(string playerUid, string routeId, string syncPointId, string reason) { }
    public bool ClearAbnormalStatus(string playerUid, string reason) => true;
    public void ClearAllAbnormalStatuses(string reason) { }
    public bool IsAbnormal(string playerUid) => false;
    public List<string> GetAllAbnormalPlayerUids() => new();
    public void Reset() { }
}

// AbnormalStatusInfo 的空壳  
public class AbnormalStatusInfo
{
    public string PlayerUid { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string SyncPointId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime MarkedTime { get; set; }
    public bool IsCleared { get; set; }
}
