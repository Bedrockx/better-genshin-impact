#nullable enable

using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 集体卡死跳段静态信号通道（multiplayer-mutual-wait-collective-skip spec, design §8.7 备选 B）。
///
/// 与 <c>MultiplayerRevivalGate</c> 静态写法严格对称，专门解决：
/// <para>
/// 服务端 <c>RequestSkipToProgress</c> 事件触发后，需要让正在 <c>SyncBarrier.WaitAsync</c>
/// 等待中的 <c>TeamManager.WaitAtSyncPoint</c> 立即解封 — 否则等到 60s 客户端超时才能响应。
/// </para>
/// <para>
/// 设计选择"静态 helper"而非"<c>TeamManager</c> 注入 <c>MultiplayerCoordinator</c>"是为了
/// 避免 <c>TeamManager → MultiplayerCoordinator → TeamManager</c> 的循环引用问题，
/// 与既有 <c>MultiplayerRevivalGate</c> 模式保持一致。
/// </para>
///
/// 调用契约：
/// <list type="bullet">
///   <item><c>Token</c>：调用方在 <c>WaitAsync</c> 前 link 进 <c>linkedCts</c>，触发 <c>Cancel()</c> 后立即解封。</item>
///   <item><c>Cancel()</c>：由 <c>MultiplayerCoordinator.OnRequestSkipToProgressReceived</c> 调用。</item>
///   <item><c>Reset()</c>：由 <c>MultiplayerCoordinator.ResetForNewRound</c> 调用，
///         在 <c>Dispose</c> 旧 <c>CancellationTokenSource</c> 后重建新的。</item>
///   <item><c>TargetProgress</c>：缓存最近一次广播的 targetProgress，配合 <c>_remoteSkipRequested</c>
///         信号位使用，让 4 处消费点能拿到目标段进度。</item>
/// </list>
///
/// 单机模式：本类仍可被调用（<c>MultiplayerCoordinator == null</c> 时调用方守卫保证不命中），
/// 但 <c>OnRequestSkipToProgressReceived</c> 永不被触发 → <c>Cancel()</c> 永不被调用 →
/// <c>Token</c> 永不取消 → 行为与单机模式无异。
/// </summary>
public static class RemoteSkipGate
{
    private static CancellationTokenSource _cts = new();

    /// <summary>
    /// 当前 CancellationToken。<c>TeamManager.WaitAtSyncPoint</c> 把它 link 进 linkedCts 后，
    /// 调 <c>Cancel()</c> 即可让等待中的 <c>tcs.Task</c> 立即解封。
    /// </summary>
    public static CancellationToken Token => _cts.Token;

    /// <summary>
    /// 集体跳段广播载荷的 targetProgress（最近一次）。在 <c>Cancel()</c> 之前由
    /// <c>MultiplayerCoordinator.OnRequestSkipToProgressReceived</c> 写入。
    /// </summary>
    public static long TargetProgress { get; set; } = -1;

    /// <summary>
    /// 触发取消，让所有 link 了 <see cref="Token"/> 的等待立即解封。
    /// 由 <c>MultiplayerCoordinator.OnRequestSkipToProgressReceived</c> 调用。
    /// 已 Dispose 的情况下安静吞掉异常。
    /// </summary>
    public static void Cancel()
    {
        try { _cts.Cancel(); }
        catch (System.ObjectDisposedException) { /* 已 Reset 中 Dispose */ }
    }

    /// <summary>
    /// 多世界轮换 / DisposeAsync 时调用：Dispose 旧 CancellationTokenSource + 重建新的。
    /// 由 <c>MultiplayerCoordinator.ResetForNewRound</c> / <c>DisposeAsync</c> 调用。
    /// </summary>
    public static void Reset()
    {
        try { _cts.Cancel(); _cts.Dispose(); } catch { /* 已 Dispose */ }
        _cts = new CancellationTokenSource();
        TargetProgress = -1;
    }
}
