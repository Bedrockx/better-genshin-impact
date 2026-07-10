namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Pure function extraction of the detection decision logic from InitializeAutoEat.
/// This mirrors the FIXED logic in PathExecutor.InitializeAutoEat.
/// 
/// The fixed code flow:
///   1. If !autoEatEnabled → AutoEatCount = 3, return
///   2. Capture screen, detect numLabels via ConnectedComponentsWithStats
///   3. If numLabels <= 1 (not detected):
///      a. Wait 300-500ms for screen to stabilize
///      b. Perform recheck: capture again, detect recheckNumLabels
///      c. If recheckNumLabels > 1 → AutoEatCount = 0, return (recheck succeeded)
///      d. If recheckNumLabels <= 1 → original logic:
///         i.  If retryAssemblyNum > 0 → call RetryAssembly (ShouldRetryAssembly = true)
///         ii. Else → AutoEatCount = 3
///   4. If numLabels > 1 (detected) → AutoEatCount = 0
/// 
/// FIX: A recheck is now performed when first detection fails (numLabels <= 1).
/// </summary>
public static class AutoEatDetectionLogic
{
    /// <summary>
    /// Evaluates the auto-eat detection decision based on the fixed code logic.
    /// When first detection fails, a recheck is performed using recheckNumLabels.
    /// </summary>
    /// <param name="autoEatEnabled">Whether auto-eat is enabled in config</param>
    /// <param name="firstDetectionNumLabels">numLabels from first ConnectedComponentsWithStats call</param>
    /// <param name="recheckNumLabels">numLabels from recheck ConnectedComponentsWithStats call</param>
    /// <param name="retryAssemblyNum">Remaining retry assembly attempts</param>
    /// <returns>Detection result describing what actions to take</returns>
    public static AutoEatDetectionResult Evaluate(
        bool autoEatEnabled,
        int firstDetectionNumLabels,
        int recheckNumLabels,
        int retryAssemblyNum)
    {
        var result = new AutoEatDetectionResult();

        if (!autoEatEnabled)
        {
            result.AutoEatCount = 3;
            result.ShouldRetryAssembly = false;
            result.RecheckPerformed = false;
            return result;
        }

        if (firstDetectionNumLabels <= 1)
        {
            // FIX: Perform recheck before entering assembly branch
            result.RecheckPerformed = true;

            if (recheckNumLabels > 1)
            {
                // Recheck succeeded — nutrition bag is equipped
                result.AutoEatCount = 0;
                result.ShouldRetryAssembly = false;
            }
            else
            {
                // Recheck also failed — proceed with original logic
                if (retryAssemblyNum > 0)
                {
                    result.ShouldRetryAssembly = true;
                    // AutoEatCount depends on RetryAssembly result,
                    // but for the purpose of this test we track the call
                    result.AutoEatCount = 0; // optimistic: RetryAssembly might succeed
                }
                else
                {
                    result.ShouldRetryAssembly = false;
                    result.AutoEatCount = 3;
                }
            }
        }
        else
        {
            // First detection succeeded
            result.AutoEatCount = 0;
            result.ShouldRetryAssembly = false;
            result.RecheckPerformed = false;
        }

        return result;
    }
}
