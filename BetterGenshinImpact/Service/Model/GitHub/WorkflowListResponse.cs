using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.GitHub;

public class WorkflowListResponse
{
    [JsonProperty("workflows")] public List<WorkflowItem> Workflows { get; set; } = new();
}
