namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public interface IPauseCoordinator
{
    bool IsPaused { get; }
    void ToggleManualPause();
    void WaitIfPaused();
}
