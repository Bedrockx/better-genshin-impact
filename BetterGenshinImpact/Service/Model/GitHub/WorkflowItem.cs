using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.GitHub;

public class WorkflowItem
{
    [JsonProperty("id")] public long Id { get; set; }

    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
}
