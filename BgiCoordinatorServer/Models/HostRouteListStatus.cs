namespace BgiCoordinatorServer.Models;

/// <summary>
/// 房主路线列表状态的原子快照。
/// multiplayer-member-skip-round-stuck-roundend-sync-fix：
/// 取代成员侧 GetHostRouteList + IsHostRouteListUploaded 两次独立查询，
/// 在一次 RPC 内同时返回 (Uploaded, RouteNames)，消除 TOCTOU 竞态。
/// </summary>
public class HostRouteListStatus
{
    /// <summary>房主是否已上传过路线列表（含上传空列表）。</summary>
    public bool Uploaded { get; set; }

    /// <summary>房主当前路线列表（与 Uploaded 同一时刻快照）。</summary>
    public List<string> RouteNames { get; set; } = [];
}
