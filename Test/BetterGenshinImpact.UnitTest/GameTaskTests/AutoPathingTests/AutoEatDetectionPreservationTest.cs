using BetterGenshinImpact.GameTask.AutoPathing;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

/// <summary>
/// Preservation Property Tests - Property 2 (Properties 3, 4 from design)
/// 
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// 
/// These tests capture the baseline behavior of the CURRENT (unfixed) code
/// for non-bug scenarios. All tests MUST PASS on unfixed code, confirming
/// that the preservation baseline is correctly captured.
/// 
/// After the fix is applied, these same tests verify no regression occurred.
/// </summary>
public class AutoEatDetectionPreservationTest
{
    /// <summary>
    /// Property 4 (Design): Preservation - AutoEatEnabled = false 时行为不变
    /// 
    /// **Validates: Requirements 3.2**
    /// 
    /// Observation: On unfixed code, when AutoEatEnabled = false,
    /// AutoEatCount is set to 3 and the method returns immediately
    /// without performing any pixel detection.
    /// 
    /// For all inputs where AutoEatEnabled = false:
    ///   - AutoEatCount SHALL be 3 (auto-eat disabled)
    ///   - ShouldRetryAssembly SHALL be false (no assembly attempted)
    ///   - RecheckPerformed SHALL be false (no detection at all)
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AutoEatDisabledArbitrary) })]
    public Property Preservation_AutoEatDisabled_AlwaysSetsCount3_NoDetection(
        AutoEatDisabledInput input)
    {
        var result = AutoEatDetectionLogic.Evaluate(
            autoEatEnabled: input.AutoEatEnabled,
            firstDetectionNumLabels: input.FirstDetectionNumLabels,
            recheckNumLabels: input.RecheckNumLabels,
            retryAssemblyNum: input.RetryAssemblyNum);

        return (result.AutoEatCount == 3 &&
                result.ShouldRetryAssembly == false &&
                result.RecheckPerformed == false)
            .Label("Expected: AutoEatCount=3, ShouldRetryAssembly=false, RecheckPerformed=false")
            .Label($"Actual: AutoEatCount={result.AutoEatCount}, ShouldRetryAssembly={result.ShouldRetryAssembly}, RecheckPerformed={result.RecheckPerformed}");
    }

    /// <summary>
    /// Property 3 (Design): Preservation - 首次检测成功时行为不变
    /// 
    /// **Validates: Requirements 3.1**
    /// 
    /// Observation: On unfixed code, when numLabels > 1 (first detection succeeds),
    /// AutoEatCount is set to 0 and RetryAssembly is not called.
    /// 
    /// For all inputs where AutoEatEnabled = true AND firstDetectionNumLabels > 1:
    ///   - AutoEatCount SHALL be 0 (auto-eat enabled)
    ///   - ShouldRetryAssembly SHALL be false (no assembly needed)
    ///   - RecheckPerformed SHALL be false (no recheck needed)
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FirstDetectionSuccessArbitrary) })]
    public Property Preservation_FirstDetectionSuccess_AlwaysSetsCount0_NoRetryAssembly(
        FirstDetectionSuccessInput input)
    {
        var result = AutoEatDetectionLogic.Evaluate(
            autoEatEnabled: input.AutoEatEnabled,
            firstDetectionNumLabels: input.FirstDetectionNumLabels,
            recheckNumLabels: input.RecheckNumLabels,
            retryAssemblyNum: input.RetryAssemblyNum);

        return (result.AutoEatCount == 0 &&
                result.ShouldRetryAssembly == false &&
                result.RecheckPerformed == false)
            .Label("Expected: AutoEatCount=0, ShouldRetryAssembly=false, RecheckPerformed=false")
            .Label($"Actual: AutoEatCount={result.AutoEatCount}, ShouldRetryAssembly={result.ShouldRetryAssembly}, RecheckPerformed={result.RecheckPerformed}");
    }

    /// <summary>
    /// Preservation - numLabels &lt;= 1 且 RetryAssemblyNum &lt;= 0 时 AutoEatCount = 3
    /// 
    /// **Validates: Requirements 3.4**
    /// 
    /// Observation: On unfixed code, when numLabels &lt;= 1 (detection fails)
    /// and RetryAssemblyNum &lt;= 0 (no retries left), AutoEatCount is set to 3.
    /// 
    /// For all inputs where AutoEatEnabled = true AND firstDetectionNumLabels &lt;= 1
    /// AND retryAssemblyNum &lt;= 0:
    ///   - AutoEatCount SHALL be 3 (auto-eat disabled)
    ///   - ShouldRetryAssembly SHALL be false (no retries available)
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DetectionFailNoRetryArbitrary) })]
    public Property Preservation_DetectionFailNoRetry_AlwaysSetsCount3(
        DetectionFailNoRetryInput input)
    {
        var result = AutoEatDetectionLogic.Evaluate(
            autoEatEnabled: input.AutoEatEnabled,
            firstDetectionNumLabels: input.FirstDetectionNumLabels,
            recheckNumLabels: input.RecheckNumLabels,
            retryAssemblyNum: input.RetryAssemblyNum);

        return (result.AutoEatCount == 3 &&
                result.ShouldRetryAssembly == false)
            .Label("Expected: AutoEatCount=3, ShouldRetryAssembly=false")
            .Label($"Actual: AutoEatCount={result.AutoEatCount}, ShouldRetryAssembly={result.ShouldRetryAssembly}");
    }
}


#region Input Models

/// <summary>
/// Input for AutoEatEnabled = false scenario.
/// All other parameters vary freely to prove the property holds universally.
/// </summary>
public class AutoEatDisabledInput
{
    public bool AutoEatEnabled { get; set; }
    public int FirstDetectionNumLabels { get; set; }
    public int RecheckNumLabels { get; set; }
    public int RetryAssemblyNum { get; set; }

    public override string ToString() =>
        $"AutoEatEnabled={AutoEatEnabled}, FirstNumLabels={FirstDetectionNumLabels}, " +
        $"RecheckNumLabels={RecheckNumLabels}, RetryAssemblyNum={RetryAssemblyNum}";
}

/// <summary>
/// Input for first detection success scenario (numLabels > 1).
/// </summary>
public class FirstDetectionSuccessInput
{
    public bool AutoEatEnabled { get; set; }
    public int FirstDetectionNumLabels { get; set; }
    public int RecheckNumLabels { get; set; }
    public int RetryAssemblyNum { get; set; }

    public override string ToString() =>
        $"AutoEatEnabled={AutoEatEnabled}, FirstNumLabels={FirstDetectionNumLabels}, " +
        $"RecheckNumLabels={RecheckNumLabels}, RetryAssemblyNum={RetryAssemblyNum}";
}

/// <summary>
/// Input for detection fail with no retries left scenario.
/// </summary>
public class DetectionFailNoRetryInput
{
    public bool AutoEatEnabled { get; set; }
    public int FirstDetectionNumLabels { get; set; }
    public int RecheckNumLabels { get; set; }
    public int RetryAssemblyNum { get; set; }

    public override string ToString() =>
        $"AutoEatEnabled={AutoEatEnabled}, FirstNumLabels={FirstDetectionNumLabels}, " +
        $"RecheckNumLabels={RecheckNumLabels}, RetryAssemblyNum={RetryAssemblyNum}";
}

#endregion

#region Arbitraries

/// <summary>
/// Generator for AutoEatEnabled = false scenario.
/// AutoEatEnabled is always false; other params vary freely.
/// </summary>
public class AutoEatDisabledArbitrary
{
    public static Arbitrary<AutoEatDisabledInput> AutoEatDisabledInputArb()
    {
        var gen = from firstNumLabels in Gen.Choose(0, 10)
                  from recheckNumLabels in Gen.Choose(0, 10)
                  from retryAssemblyNum in Gen.Choose(-2, 5)
                  select new AutoEatDisabledInput
                  {
                      AutoEatEnabled = false,
                      FirstDetectionNumLabels = firstNumLabels,
                      RecheckNumLabels = recheckNumLabels,
                      RetryAssemblyNum = retryAssemblyNum
                  };

        return Arb.From(gen);
    }
}

/// <summary>
/// Generator for first detection success scenario.
/// AutoEatEnabled = true, firstDetectionNumLabels > 1 (range [2, 10]).
/// Other params vary freely.
/// </summary>
public class FirstDetectionSuccessArbitrary
{
    public static Arbitrary<FirstDetectionSuccessInput> FirstDetectionSuccessInputArb()
    {
        var gen = from firstNumLabels in Gen.Choose(2, 10)       // First detection succeeds: numLabels > 1
                  from recheckNumLabels in Gen.Choose(0, 10)     // Recheck value irrelevant in this path
                  from retryAssemblyNum in Gen.Choose(-2, 5)     // Retry count irrelevant in this path
                  select new FirstDetectionSuccessInput
                  {
                      AutoEatEnabled = true,
                      FirstDetectionNumLabels = firstNumLabels,
                      RecheckNumLabels = recheckNumLabels,
                      RetryAssemblyNum = retryAssemblyNum
                  };

        return Arb.From(gen);
    }
}

/// <summary>
/// Generator for detection fail with no retries left scenario.
/// AutoEatEnabled = true, firstDetectionNumLabels &lt;= 1, retryAssemblyNum &lt;= 0.
/// </summary>
public class DetectionFailNoRetryArbitrary
{
    public static Arbitrary<DetectionFailNoRetryInput> DetectionFailNoRetryInputArb()
    {
        var gen = from firstNumLabels in Gen.Choose(0, 1)        // First detection fails: numLabels <= 1
                  from recheckNumLabels in Gen.Choose(0, 1)      // Recheck also fails: numLabels <= 1 (matches Req 3.4: "复检后仍未检测到营养袋")
                  from retryAssemblyNum in Gen.Choose(-5, 0)     // No retries left: retryAssemblyNum <= 0
                  select new DetectionFailNoRetryInput
                  {
                      AutoEatEnabled = true,
                      FirstDetectionNumLabels = firstNumLabels,
                      RecheckNumLabels = recheckNumLabels,
                      RetryAssemblyNum = retryAssemblyNum
                  };

        return Arb.From(gen);
    }
}

#endregion
