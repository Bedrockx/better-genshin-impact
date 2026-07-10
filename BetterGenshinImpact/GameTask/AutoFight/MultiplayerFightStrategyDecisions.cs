using System;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 联机锄地战斗策略路径决策（纯函数，PBT 友好）。
/// <para>
/// 设计目标：把"联机锄地是否覆盖 CombatStrategyPath 为固定文件 / 是否记 INFO 日志"的
/// 业务规则集中到一个无副作用的静态函数中，让 <see cref="AutoPathing.Handler.AutoFightHandler"/>
/// 只负责调用决策函数 + 应用结果，便于单元测试撒输入。
/// </para>
/// <para>
/// 详见 .kiro/specs/multiplayer-hoeing-fixed-fight-strategy/design.md §1。
/// </para>
/// </summary>
public static class MultiplayerFightStrategyDecisions
{
    /// <summary>
    /// 决策联机锄地下应当使用的战斗策略路径与日志触发标志。
    /// </summary>
    /// <param name="isMultiplayer">
    /// 是否为"联机锄地正在运行"分支。生产路径下传入
    /// <see cref="Core.Config.PathingConditionConfig.MultiplayerFightTimeoutOverride"/>.HasValue。
    /// </param>
    /// <param name="fixedFilePath">固定策略文件绝对路径，调用方负责调用 <c>Global.Absolute</c>。</param>
    /// <param name="originalResolvedPath">由 <see cref="AutoFightParam"/> 构造期解析出的原路径。</param>
    /// <param name="originalStrategyName">
    /// 原始策略名（仅用于日志触发条件判定），可为 <c>null</c> / 空字符串 / "根据队伍自动选择" / 任意自定义名。
    /// </param>
    /// <param name="fileExists">文件存在性检查器；生产路径传入 <c>System.IO.File.Exists</c>，PBT 路径传入 mock。</param>
    /// <returns>
    /// (Path, ShouldLogOverride)
    /// - Path：最终应当写入 <c>taskParams.CombatStrategyPath</c> 的路径
    /// - ShouldLogOverride：是否触发一次 INFO 日志（避免无意义的"未发生覆盖"也打日志）
    /// </returns>
    public static (string Path, bool ShouldLogOverride) ResolveCombatStrategyPath(
        bool isMultiplayer,
        string fixedFilePath,
        string originalResolvedPath,
        string? originalStrategyName,
        Func<string, bool> fileExists)
    {
        if (fileExists is null) throw new ArgumentNullException(nameof(fileExists));

        // 单机 / 非锄地路径：完全无感知，返回原解析结果。
        if (!isMultiplayer)
        {
            return (originalResolvedPath, false);
        }

        // 联机锄地但固定文件不存在：静默回退到原策略解析逻辑（OQ-1 决议方案 E）。
        // 故意不在运行时创建文件，避免污染用户目录；UI 按钮路径走另一份"显式编辑"语义。
        if (!fileExists(fixedFilePath))
        {
            return (originalResolvedPath, false);
        }

        // 联机锄地且固定文件存在：覆盖路径。
        // 仅在"原 StrategyName 非空 AND 原解析路径与固定路径不同"时记一次 INFO 日志。
        bool shouldLog =
            !string.IsNullOrEmpty(originalStrategyName)
            && !string.Equals(originalResolvedPath, fixedFilePath, StringComparison.OrdinalIgnoreCase);

        return (fixedFilePath, shouldLog);
    }

    /// <summary>
    /// 决策"是否对联机锄地战斗应用固定策略覆盖"。
    /// 等价于把布尔拼装从 AutoFightHandler 中提出来便于 PBT。
    /// 调用方将返回值作为 ResolveCombatStrategyPath 的 isMultiplayer 实参：
    /// true → 走联机覆盖逻辑；false → 等价"不覆盖"（返回 originalResolvedPath）。
    /// </summary>
    /// <param name="isMultiplayerHoeing">是否处于联机锄地运行分支（Multiplayer_Signal）。</param>
    /// <param name="useFixedFightStrategy">本机开关 MultiplayerUseFixedFightStrategy 的值。</param>
    public static bool ShouldApplyFixedStrategy(bool isMultiplayerHoeing, bool useFixedFightStrategy)
        => isMultiplayerHoeing && useFixedFightStrategy;
}
