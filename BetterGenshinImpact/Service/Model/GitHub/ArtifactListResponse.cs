using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.GitHub;

public class ArtifactListResponse
{
    [JsonProperty("artifacts")] public List<ArtifactItem> Artifacts { get; set; } = new();
}
