namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 成员侧"房主路线列表查询结果"分类决策（纯函数，PBT 友好，无外部依赖）。
/// multiplayer-host-empty-route-member-wait-timeout-fix。
///
/// 解决的 bug：房主 CD 全过滤后上传空列表，成员无法区分"房主未上传"与"房主上传了空列表"，
/// 导致成员等满 90 秒后把"空列表"误报为"超时"并停止本轮。
///
/// 方案 A（服务端权威标志）：服务端 Room.HostRouteListUploaded 区分"从未上传"与"上传了空列表"，
/// 客户端通过 IsHostRouteListUploadedAsync 查询该标志。
/// 注意：不能用"房主已就绪(HostReady)"代理"已上传"——房主在组队阶段就上报就绪（早），
/// 而上传路线列表在开锄前（晚），两者之间存在较长时间窗，用 HostReady 会误判。
/// </summary>
public static class HostRouteListDecisions
{
    public enum MemberRouteListOutcome
    {
        /// <summary>拿到非空列表，正常按列表同步继续。</summary>
        ProceedWithRoutes,

        /// <summary>房主已上传且列表为空 → 房主路线为空，优雅跳过本轮（与房主一致）。</summary>
        SkipRoundEmpty,

        /// <summary>等待超时仍未确认房主上传（房主从未上传）→ 纯超时，停止本轮。</summary>
        TimeoutNoUpload,
    }

    /// <summary>
    /// 根据 (房主是否已上传路线列表, 收到的列表条数, 是否已超时) 分类成员应采取的动作。
    /// </summary>
    /// <param name="hostUploaded">IsHostRouteListUploadedAsync 查询结果（异常/旧服务端降级为 false）。</param>
    /// <param name="uploadedListCount">当前已获取到的房主路线条数（拉取或推送）。</param>
    /// <param name="timedOut">90s 等待是否已耗尽（仅在房主未上传时进入等待后才为 true）。</param>
    public static MemberRouteListOutcome ClassifyMemberRouteListResult(
        bool hostUploaded, int uploadedListCount, bool timedOut)
    {
        // 非空：永远正常继续（与是否已上传标志/超时无关）。
        if (uploadedListCount > 0)
        {
            return MemberRouteListOutcome.ProceedWithRoutes;
        }

        // 空列表 + 房主已上传 → 房主路线确实为空，优雅跳过本轮。
        // （房主未上传时降级返回 false，偏保守：宁可继续等待也不误判为空路线。）
        if (hostUploaded)
        {
            return MemberRouteListOutcome.SkipRoundEmpty;
        }

        // 空列表 + 房主未上传 → 纯超时未收到任何上传。
        // 调用方仅在拉取命中或 90s 超时后才询问本分支，故此处统一归为超时语义。
        return MemberRouteListOutcome.TimeoutNoUpload;
    }

    /// <summary>
    /// 从原子快照 (uploaded, routeCount) 推导成员动作。
    /// multiplayer-member-skip-round-stuck-roundend-sync-fix：
    /// uploaded 与 routeCount 必须来自服务端同一次 GetHostRouteListStatus 调用（同源快照），
    /// 否则会重蹈 TOCTOU 覆辙——房主在两次独立查询之间上传非空列表时，
    /// 成员拿到 (uploaded=true, count=0) 误判 SkipRoundEmpty。
    /// 本函数仅做语义转发，决策逻辑沿用 ClassifyMemberRouteListResult。
    /// </summary>
    public static MemberRouteListOutcome ClassifyFromAtomicSnapshot(bool uploaded, int routeCount, bool timedOut)
        => ClassifyMemberRouteListResult(uploaded, routeCount, timedOut);
}
