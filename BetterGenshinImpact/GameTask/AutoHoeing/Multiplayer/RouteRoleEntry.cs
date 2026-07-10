#nullable enable
namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 单条线路的切角色配置：线路标识 + 1/2 号位目标角色名（字符串，可空=不切该号位）。
/// 纯数据 POCO，无行为；判定/序列化见 PerRouteSwitchRolesDecisions。
/// 语义：填了角色即启用、留空即不切换（OQ-10，无独立启用开关）。
/// </summary>
public sealed class RouteRoleEntry
{
    /// <summary>线路全局标识 = "{FolderName}/{RelativeId}"，唯一稳定。</summary>
    public string RouteId { get; set; } = "";

    /// <summary>我的 1 号位目标角色名/别名，空=不切 1 号位。</summary>
    public string Position1 { get; set; } = "";

    /// <summary>我的 2 号位目标角色名/别名，空=不切 2 号位。</summary>
    public string Position2 { get; set; } = "";
}
