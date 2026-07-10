using BetterGenshinImpact.GameTask.AutoPathing;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

/// <summary>
/// Bug Condition Exploration Test - Property 1
/// 
/// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
/// 
/// This test encodes the EXPECTED (correct) behavior:
/// When autoEatEnabled = true, first detection fails (numLabels &lt;= 1),
/// but recheck succeeds (numLabels &gt; 1), the system SHOULD:
///   - Perform a recheck (RecheckPerformed = true)
///   - Set AutoEatCount = 0 (enable auto-eat)
///   - NOT call RetryAssembly
/// 
/// On UNFIXED code, this test is EXPECTED TO FAIL because the current
/// implementation does not perform a recheck — it directly enters the
/// assembly branch when first detection fails.
/// 
/// GOAL: Produce a counterexample proving the bug exists.
/// </summary>
public class AutoEatDetectionBugConditionTest
{
    /// <summary>
    /// Property 1: Bug Condition - 首次检测失败后未执行复检即进入装配流程
    /// 
    /// For all inputs where:
    ///   - autoEatEnabled = true
    ///   - firstDetectionNumLabels &lt;= 1 (transient occlusion, first detection fails)
    ///   - recheckNumLabels &gt; 1 (recheck would succeed, nutrition bag is actually equipped)
    /// 
    /// The system SHOULD:
    ///   - Perform a recheck (RecheckPerformed = true)
    ///   - Set AutoEatCount = 0 (auto-eat enabled)
    ///   - NOT call RetryAssembly (ShouldRetryAssembly = false)
    ///   - NOT consume RetryAssemblyNum
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BugConditionArbitrary) })]
    public Property BugCondition_FirstDetectionFails_ShouldRecheckBeforeAssembly(
        BugConditionInput input)
    {
        // Act: evaluate using the current (unfixed) detection logic
        var result = AutoEatDetectionLogic.Evaluate(
            autoEatEnabled: input.AutoEatEnabled,
            firstDetectionNumLabels: input.FirstDetectionNumLabels,
            recheckNumLabels: input.RecheckNumLabels,
            retryAssemblyNum: input.RetryAssemblyNum);

        // Assert expected behavior (correct behavior after fix):
        // 1. A recheck should have been performed
        // 2. Since recheck succeeds (numLabels > 1), AutoEatCount should be 0
        // 3. RetryAssembly should NOT be called
        return (result.RecheckPerformed == true &&
                result.AutoEatCount == 0 &&
                result.ShouldRetryAssembly == false)
            .Label("Expected: RecheckPerformed=true, AutoEatCount=0, ShouldRetryAssembly=false")
            .Label($"Actual: RecheckPerformed={result.RecheckPerformed}, AutoEatCount={result.AutoEatCount}, ShouldRetryAssembly={result.ShouldRetryAssembly}");
    }
}

/// <summary>
/// Input model for the bug condition scenario.
/// Constrains inputs to the specific failure scenario:
///   - AutoEatEnabled = true
///   - FirstDetectionNumLabels &lt;= 1 (transient occlusion)
///   - RecheckNumLabels &gt; 1 (recheck would succeed)
/// </summary>
public class BugConditionInput
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
/// Smart generator that constrains inputs to the bug condition scenario:
///   - AutoEatEnabled = true (always)
///   - FirstDetectionNumLabels in [0, 1] (first detection fails)
///   - RecheckNumLabels in [2, 10] (recheck would succeed)
///   - RetryAssemblyNum in [0, 5] (varies to cover both branches)
/// </summary>
public class BugConditionArbitrary
{
    public static Arbitrary<BugConditionInput> BugConditionInputArb()
    {
        var gen = from firstNumLabels in Gen.Choose(0, 1)        // First detection fails: numLabels <= 1
                  from recheckNumLabels in Gen.Choose(2, 10)     // Recheck succeeds: numLabels > 1
                  from retryAssemblyNum in Gen.Choose(0, 5)      // Vary retry count
                  select new BugConditionInput
                  {
                      AutoEatEnabled = true,                      // Always enabled for bug condition
                      FirstDetectionNumLabels = firstNumLabels,
                      RecheckNumLabels = recheckNumLabels,
                      RetryAssemblyNum = retryAssemblyNum
                  };

        return Arb.From(gen);
    }
}
