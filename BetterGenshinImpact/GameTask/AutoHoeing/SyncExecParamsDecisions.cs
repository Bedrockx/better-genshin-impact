namespace BetterGenshinImpact.GameTask.AutoHoeing;

/// <summary>
/// hoeing-multiplayer-sync-execution-params：执行期参数同步的纯函数决策/钳制。
/// 无外部依赖，便于 PBT。
/// </summary>
public static class SyncExecParamsDecisions
{
    /// <summary>光柱拾取时长下限 0（对齐 ScanPickTask 语义，负数无意义）。</summary>
    public static int ClampSeconds(int v) => v < 0 ? 0 : v;

    /// <summary>简易等待时间 ms 下限 0（PathExecutor 用 `>0 ? v : 1`，负数归 0 等价走 1ms 分支）。</summary>
    public static int ClampDelayMs(int v) => v < 0 ? 0 : v;
}
