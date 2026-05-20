#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// fire-and-forget 异常兜底 helper：
/// 把 task 的未观察异常 LogWarning 一次（避免 unobserved task exception）。
/// 与 CoordinatorClient 内部既有 try/catch + LogWarning 静默吞 SignalR 异常并存 ——
/// 此 helper 只兜底 CoordinatorClient try 之外抛出的（极少发生的）同步异常 / OperationCanceledException。
/// 不允许使用裸 <c>_ = client.InvokeAsync(...)</c>（异常 sink 会丢失日志）。
/// 详见 design.md §3.3 / §6 PBT-5。
/// </summary>
public static class FireAndForgetHelper
{
    /// <summary>
    /// 包装一个 fire-and-forget Task：失败时 LogWarning，成功时不日志。
    /// 返回值是 ContinueWith 的 Task，便于测试 await 等其完成；生产代码可丢弃。
    /// </summary>
    public static Task ObserveExceptions(Task task, ILogger logger, string opLabel)
    {
        return task.ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                logger.LogWarning(t.Exception.GetBaseException(),
                    "[联机][聚物] {Op} 失败（fire-and-forget）", opLabel);
            }
        }, TaskScheduler.Default);
    }
}
