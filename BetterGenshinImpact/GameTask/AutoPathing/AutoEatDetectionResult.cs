namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Represents the result of the auto-eat detection decision logic.
/// Extracted from InitializeAutoEat to enable testability.
/// </summary>
public class AutoEatDetectionResult
{
    /// <summary>
    /// The value to set for AutoEatCount (0 = enabled, 3 = disabled).
    /// </summary>
    public int AutoEatCount { get; set; }

    /// <summary>
    /// Whether RetryAssembly should be called.
    /// </summary>
    public bool ShouldRetryAssembly { get; set; }

    /// <summary>
    /// Whether a recheck was performed after the first detection failed.
    /// </summary>
    public bool RecheckPerformed { get; set; }
}
