#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 按线路切角色 Hook（hoeing-multiplayer-per-route-switch-roles）。
/// 由 RouteExecutionEngine 在「该线路配了角色」时注入 PathExecutor。承载本线路是否需切换 +
/// 「传送完成后执行切角色」的异步委托。SwitchAsync 内部 new AutoSwitchRolesTask + MultiplayerSwitchOverride。
/// </summary>
public sealed class PerRouteSwitchHook
{
    /// <summary>本线路是否配置了有效切角色（RouteNeedsSwitch）。false 时 Hook 整体短路。</summary>
    public bool RouteHasSwitch { get; init; }

    /// <summary>执行联机切角色。抛 OperationCanceledException 透传。</summary>
    public Func<CancellationToken, Task> SwitchAsync { get; init; } = null!;
}
