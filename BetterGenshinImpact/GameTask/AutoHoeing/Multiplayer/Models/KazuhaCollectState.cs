#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 万叶聚物同步周期内单个客户端的状态机。
/// 见 design.md "Data Models §1 客户端状态机"。
///
/// 合法迁移（其他迁移视为非法）：
///   Idle              → AtFightPoint, Skipped
///   AtFightPoint      → WaitingForPeers, WaitingForKazuha, Skipped
///   WaitingForPeers   → Collecting, Skipped
///   WaitingForKazuha  → PostFinishedWait, Skipped
///   Collecting        → Finished, Skipped
///   PostFinishedWait  → Finished
///   Finished / Skipped 是吸收态，不再迁移。
///
/// 这两个吸收态直接给出 Bug Condition 2/6 的不变式证明思路：
/// - Finished 与 Skipped 互斥，任意一个吸收态进入后不再迁移 → 终态唯一性。
/// - 从 Skipped 不可达 Finished → 一旦广播 Skipped 不会再广播 Finished。
/// </summary>
public enum KazuhaCollectState
{
    /// <summary>战斗中或还未到达战斗点。</summary>
    Idle,

    /// <summary>已到达，已上报 AtFightPoint。</summary>
    AtFightPoint,

    /// <summary>万叶身份：等其他人到齐再放 E。</summary>
    WaitingForPeers,

    /// <summary>普通身份：等万叶完成或被跳过。</summary>
    WaitingForKazuha,

    /// <summary>万叶身份：正在执行 KazuhaCollectExecutor。</summary>
    Collecting,

    /// <summary>普通身份：收到 Finished(true) 后再停 KazuhaSyncWaitSeconds。</summary>
    PostFinishedWait,

    /// <summary>终态：本周期结束，离开战斗点。</summary>
    Finished,

    /// <summary>终态：降级（含原因码），离开战斗点。</summary>
    Skipped,
}
