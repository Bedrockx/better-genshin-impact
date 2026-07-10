namespace BgiCoordinatorServer.Models;

/// <summary>版本校验结果（version-compatibility-check R5.2–R5.6）。
/// 通过 VersionCheckRejected 事件回传给被阻断的加入者展示。</summary>
public class VersionCheckResult
{
    public bool Compatible { get; set; }
    public string MemberVersion { get; set; } = "";
    public string BaselineVersion { get; set; } = "";
    public bool MemberIsWildcard { get; set; }
    public bool BaselineIsWildcard { get; set; }
    public string Hint { get; set; } = "";
}
