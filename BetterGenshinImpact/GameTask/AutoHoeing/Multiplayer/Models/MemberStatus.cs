namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 成员状态枚举，用于联机模式下的状态同步。
/// </summary>
public enum MemberStatus
{
    /// <summary>
    /// 正常状态
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 战斗中
    /// </summary>
    Fighting = 1,

    /// <summary>
    /// 重新加入中（如掉线重连、传送后等待）
    /// </summary>
    Rejoining = 2,

    /// <summary>
    /// 复苏中（角色死亡后等待复苏）
    /// </summary>
    Reviving = 3,

    /// <summary>
    /// 离线/退出
    /// </summary>
    Offline = 4
}
