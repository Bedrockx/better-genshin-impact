namespace BgiCoordinatorServer.Models;

public class RoomSummary
{
    public string Code { get; set; } = "";
    public string HostName { get; set; } = "";
    public string HostUid { get; set; } = "";
    public int PlayerCount { get; set; }
    public int ExpectedPlayerCount { get; set; } = 4;
    public int MaxPlayers { get; set; } = 4;
}
