using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.GitHub;

public class WorkflowRun
{
    [JsonProperty("id")] public long Id { get; set; }

    [JsonProperty("display_title")] public string DisplayTitle { get; set; } = string.Empty;

    [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }

    [JsonProperty("status")] public string Status { get; set; } = string.Empty;

    [JsonProperty("conclusion")] public string Conclusion { get; set; } = string.Empty;
}
