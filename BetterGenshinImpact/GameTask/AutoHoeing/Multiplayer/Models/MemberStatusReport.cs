#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 成员状态上报消息，携带版本号用于乱序控制。
/// 版本号由客户端生成（Interlocked.Increment），服务器透传，
/// 接收方只接受版本号更大的更新。
/// </summary>
public class MemberStatusReport
{
    /// <summary>玩家 UID（游戏内唯一标识）</summary>
    public string PlayerUid { get; set; } = "";

    /// <summary>状态字符串（MemberStatus 枚举的 ToString()）</summary>
    public string Status { get; set; } = "";

    /// <summary>客户端递增版本号，用于防止网络延迟导致的乱序覆盖</summary>
    public long Version { get; set; }
}
