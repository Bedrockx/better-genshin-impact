#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 联机锄地成员异常恢复状态。
/// 与 <see cref="PlayerStatus"/>（Waiting/Ready/Pathing/WaitingAtSync/Fighting）不同，
/// MemberStatus 专门用于异常恢复场景的状态感知，供 SyncBarrier 等组件查询。
/// </summary>
public enum MemberStatus
{
    /// <summary>正常运行</summary>
    Normal,

    /// <summary>战斗中（进入战斗节点时上报，战斗结束后清除）</summary>
    Fighting,

    /// <summary>跳过路线后等待在下一同步点汇合（成员侧，汇合成功后清除）</summary>
    Rejoining,

    /// <summary>恢复中——房主侧路线异常重跑（重跑完成或跳过后清除）</summary>
    Reviving,

    /// <summary>已离线（退出世界/降级/连续超时后上报，从状态字典中移除而非存储）</summary>
    Offline
}
