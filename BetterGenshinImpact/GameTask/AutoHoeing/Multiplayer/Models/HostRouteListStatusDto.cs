#nullable enable

using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 房主路线列表状态的原子快照（客户端反序列化用）。
/// multiplayer-member-skip-round-stuck-roundend-sync-fix：与服务端 HostRouteListStatus
/// 结构对应（属性名 Uploaded / RouteNames 必须一致），SignalR 按属性名匹配反序列化。
/// </summary>
public class HostRouteListStatusDto
{
    /// <summary>房主是否已上传过路线列表（含上传空列表）。</summary>
    public bool Uploaded { get; set; }

    /// <summary>房主当前路线列表（与 Uploaded 同一时刻快照）。</summary>
    public List<string> RouteNames { get; set; } = new();
}
