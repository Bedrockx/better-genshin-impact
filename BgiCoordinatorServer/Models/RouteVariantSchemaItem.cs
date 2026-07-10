namespace BgiCoordinatorServer.Models;

public class RouteVariantSchemaItem
{
    public string LogicalRouteId { get; set; } = string.Empty;
    public string ActualVariantFileName { get; set; } = string.Empty;
    public List<string> SyncPointList { get; set; } = new();
    public List<int[]> TeleportSyncPointSequence { get; set; } = new();
}
