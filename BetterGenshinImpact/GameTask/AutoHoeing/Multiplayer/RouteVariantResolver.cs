#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 客户端变体替换决策（route-variant-sync-by-logical-id spec / §15.3 / R15.4）。
/// 纯函数：无 I/O / 无 logger / 无配置读取，所有输入显式传参，PBT 友好。
/// 调用方负责装配输入（扫描结果、偏好字典、代表文件名/基名）+ 处理输出（按 AbsolutePath 加载）。
/// </summary>
public static class RouteVariantResolver
{
    /// <summary>
    /// 决策的 fallback 原因码（None 表示无 fallback，正常路径）。
    /// </summary>
    public enum FallbackReason
    {
        /// <summary>无 fallback：要么基名为空（老路径），要么偏好命中且变体文件可用。</summary>
        None,

        /// <summary>基名非空但偏好字典里没这个 key（用户没配置过该线路）→ 跑代表（A变体）。</summary>
        NoPreference,

        /// <summary>偏好的变体文件夹在扫描结果里该基名下没有对应文件（缺该变体）→ 跑代表。</summary>
        FilePreferenceMissing,

        /// <summary>偏好对应文件加载失败（损坏 / 不可读）→ 跑代表。
        /// 本纯函数不直接产出该枚举（保留以便上层 wiring 复用同一日志路径）。</summary>
        FilePreferenceUnreadable,
    }

    /// <summary>
    /// 决策结果。ActualFileName / ActualAbsolutePath 指向本玩家实际要跑的文件；
    /// Reason != None 时两者回退为代表（调用方传入的 representative*）。
    /// </summary>
    public sealed record ResolveResult(string ActualFileName, string ActualAbsolutePath, FallbackReason Reason);

    /// <summary>
    /// 根据代表文件 + 玩家偏好（总文件夹名→变体文件夹名）+ 扫描结果，决定本玩家实际跑的文件。
    /// 偏好按"总文件夹"粒度：点一次 传奇 选 B变体，整个 传奇 下所有线路都跑 B变体（R15.6）。
    /// </summary>
    /// <param name="representativeFileName">代表变体的文件名（房主广播 / 去重选出的）</param>
    /// <param name="representativeAbsolutePath">代表变体的绝对路径</param>
    /// <param name="baseName">代表的线路基名；空表示老路径，直接返回代表（无 fallback）。</param>
    /// <param name="topFolderName">代表所属总文件夹名（如 "传奇"）；偏好字典的 key。</param>
    /// <param name="variantPreferences">玩家偏好字典（key=总文件夹名, value=变体文件夹名 如 "B变体"）</param>
    /// <param name="scanByBaseName">扫描结果按基名分组</param>
    public static ResolveResult Resolve(
        string representativeFileName,
        string representativeAbsolutePath,
        string? baseName,
        string? topFolderName,
        IReadOnlyDictionary<string, string> variantPreferences,
        IReadOnlyDictionary<string, List<VariantFileEntry>> scanByBaseName)
    {
        // 1. 老路径：基名 / 总文件夹为空 → 直接代表
        if (string.IsNullOrEmpty(baseName) || string.IsNullOrEmpty(topFolderName))
            return new ResolveResult(representativeFileName, representativeAbsolutePath, FallbackReason.None);

        // 2. 偏好字典里没这个总文件夹（或空值）→ NoPreference fallback
        if (variantPreferences == null
            || !variantPreferences.TryGetValue(topFolderName, out var preferredFolder)
            || string.IsNullOrEmpty(preferredFolder))
        {
            return new ResolveResult(representativeFileName, representativeAbsolutePath, FallbackReason.NoPreference);
        }

        // 3. 在扫描结果该基名下找 VariantFolder == 偏好文件夹 的 entry
        if (scanByBaseName == null
            || !scanByBaseName.TryGetValue(baseName, out var entries)
            || entries == null)
        {
            return new ResolveResult(representativeFileName, representativeAbsolutePath, FallbackReason.FilePreferenceMissing);
        }

        var matched = entries.FirstOrDefault(e =>
            string.Equals(e.VariantFolder, preferredFolder, StringComparison.Ordinal));

        if (matched == null)
            return new ResolveResult(representativeFileName, representativeAbsolutePath, FallbackReason.FilePreferenceMissing);

        // 4. 命中：若偏好变体就是代表本身（同文件）→ 等价于无替换，返回代表（None）
        if (string.Equals(matched.AbsolutePath, representativeAbsolutePath, StringComparison.OrdinalIgnoreCase))
            return new ResolveResult(representativeFileName, representativeAbsolutePath, FallbackReason.None);

        // 5. 通路：返回偏好变体文件
        return new ResolveResult(matched.FileName, matched.AbsolutePath, FallbackReason.None);
    }
}
