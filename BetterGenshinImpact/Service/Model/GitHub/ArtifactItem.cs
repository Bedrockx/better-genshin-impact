using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.GitHub;

public class ArtifactItem
{
    [JsonProperty("id")] public long Id { get; set; }

    [JsonProperty("name")] public string Name { get; set; } = string.Empty;

    [JsonProperty("archive_download_url")] public string ArchiveDownloadUrl { get; set; } = string.Empty;

    [JsonProperty("expired")] public bool Expired { get; set; }
}
