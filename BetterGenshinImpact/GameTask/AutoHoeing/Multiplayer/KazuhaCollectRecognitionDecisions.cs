#nullable enable
namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 联机锄地"聚物取万叶"三层方案（快照复用 → forceRefresh 重试 → 降级）的纯决策函数集合。
/// 抽离成 static class 是为了 PBT 友好（无 client/logger/CombatScenes 依赖，可重复调用、可撒输入）。
/// 由 <see cref="KazuhaCollectSyncCoordinator.RunAsKazuhaAsync"/> 兜底分支调用；
/// 真正的 IO（读 CombatScenesGoBackUp 快照、调 GetCombatScenes、SelectAvatar）留在协调器编排层。
///
/// spec: kazuha-collect-team-no-kazuha-false-skip-fix（design.md "Correctness Properties" P1/P3）。
/// </summary>
public static class KazuhaCollectRecognitionDecisions
{
    /// <summary>
    /// 第 2 层重试上限 N。硬编码 3，与声明阶段 Sample_Count=3 对齐，不新增配置字段（Preservation 3.8）。
    /// </summary>
    public const int MaxRecognitionRetries = 3;

    /// <summary>
    /// 第 1 层：是否复用战斗快照。等价于 snapshotHasKazuha
    /// （即 CombatScenesGoBackUp != null && SelectAvatar("枫原万叶") != null，由编排层判好后传入布尔）。
    /// 抽成纯函数便于 PBT + 语义清晰；只看万叶在不在，不校验队伍其他角色（Open Question Q9）。
    /// </summary>
    public static bool ShouldUseCombatSnapshot(bool snapshotHasKazuha) => snapshotHasKazuha;

    /// <summary>
    /// 第 2 层：是否还应继续重试 forceRefresh 识别。
    /// 在已尝试 attempt 次（attempt 从 0 计，表示"已完成的重试次数"）、尚未取到万叶（gotKazuha=false）、
    /// 且未达上限 maxAttempts 时返回 true。
    /// 等价定义：!gotKazuha && attempt < maxAttempts。
    /// </summary>
    public static bool ShouldContinueRetry(int attempt, int maxAttempts, bool gotKazuha)
        => !gotKazuha && attempt < maxAttempts;

    /// <summary>
    /// 第 3 层：在第 1 层未命中（snapshotHasKazuha=false）且第 2 层重试耗尽仍未取到万叶
    /// （gotKazuhaAfterRetries=false）时返回 true，表示应降级 team_no_kazuha。
    /// 等价定义：!snapshotHasKazuha && !gotKazuhaAfterRetries。
    /// 快照命中（snapshotHasKazuha=true）时永不降级（直接复用）。
    /// </summary>
    public static bool ShouldDegrade(bool snapshotHasKazuha, bool gotKazuhaAfterRetries)
        => !snapshotHasKazuha && !gotKazuhaAfterRetries;
}
