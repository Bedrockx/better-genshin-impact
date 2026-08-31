namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public interface INetworkHealthMonitor
{
    NetworkHealthSnapshot? LastSnapshot { get; }
    void RequestCheck();
}
