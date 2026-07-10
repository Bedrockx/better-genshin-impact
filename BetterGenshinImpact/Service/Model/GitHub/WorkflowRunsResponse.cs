using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.GitHub;

public class WorkflowRunsResponse
{
    [JsonProperty("workflow_runs")] public List<WorkflowRun> WorkflowRuns { get; set; } = new();
}
