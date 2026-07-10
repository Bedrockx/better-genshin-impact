using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

/// <summary>
/// 房主本地多世界轮换进度。记录"已锄完的房主世界 UID 集合"，供房主重开时上报服务端裁剪权威序列。
/// 仅由房主依据自己本地进度维护，不做多人进度合并。
/// hoeing-multiworld-host-restart-resume-round Req 1 / 2。
/// </summary>
public class MultiWorldProgress
{
    /// <summary>已锄完的房主世界 UID（每完成一轮追加该轮房主 UID）。</summary>
    [JsonProperty("completedHostUids")]
    public List<string> CompletedHostUids { get; set; } = new();

    /// <summary>本进度对应的轮换序列签名（全体房主 UID 升序拼接），用于判定数据是否仍可信。</summary>
    [JsonProperty("orderSignature")]
    public string OrderSignature { get; set; } = "";

    /// <summary>最后更新时间（UTC），仅用于诊断与陈旧判定。</summary>
    [JsonProperty("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.MinValue;
}
