#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 文件夹式变体命名规则（route-variant-sync-by-logical-id spec / §15.1 / R15）。
/// 纯函数 + 常量，无 I/O / 无 logger / 无配置读取，PBT 友好。
///
/// 模型：总线路文件夹（如 "传奇"）下建固定命名的变体子文件夹 A变体/B变体/C变体/D变体，
/// 每个子文件夹里放该变体的路线 JSON（文件名带 _a/_b/_c/_d 后缀）。
/// 线路身份（配对键 = syncId 命名空间）= 去掉变体后缀的文件基名。
/// </summary>
public static class RouteVariantNaming
{
    /// <summary>
    /// 固定变体子文件夹名（写死匹配这 4 个），顺序即代表优先级（A→B→C→D）。
    /// </summary>
    public static readonly string[] VariantFolders = { "A变体", "B变体", "C变体", "D变体" };

    /// <summary>
    /// 变体说明文件名（放在每个变体子文件夹里，如 传奇/A变体/变体说明.txt），
    /// 内容是该变体的简要说明（如"打左边怪"），UI 选择时展示给玩家（R15.11）。
    /// </summary>
    public const string VariantDescriptionFileName = "变体说明.txt";

    /// <summary>
    /// 规范化变体说明文本：取第一非空行、trim、限长（默认 20 字符兜底，避免异常长文本撑爆 UI）。
    /// 纯函数，PBT 友好。raw 为空返回空字符串。
    /// </summary>
    public static string NormalizeDescription(string? raw, int maxChars = 20)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var firstLine = raw.Split('\n', '\r')
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
        if (maxChars > 0 && firstLine.Length > maxChars)
            firstLine = firstLine.Substring(0, maxChars);
        return firstLine;
    }

    private static readonly Dictionary<string, string> _suffixMap = new(StringComparer.Ordinal)
    {
        ["A变体"] = "_a",
        ["B变体"] = "_b",
        ["C变体"] = "_c",
        ["D变体"] = "_d",
    };

    /// <summary>
    /// 变体文件夹对应的文件名后缀（A变体→"_a" ... D变体→"_d"）；非变体文件夹返回 null。
    /// </summary>
    public static string? SuffixFor(string? variantFolder)
    {
        if (string.IsNullOrEmpty(variantFolder)) return null;
        return _suffixMap.TryGetValue(variantFolder, out var s) ? s : null;
    }

    /// <summary>
    /// 取该文件直接父目录名；若 ∈ VariantFolders 返回它（即"该文件属于哪个变体"），否则返回 null
    /// （null = 不在变体子文件夹里，视为普通老线路文件）。
    /// </summary>
    public static string? TryGetVariantFolder(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return null;
        var parent = Path.GetFileName(Path.GetDirectoryName(absolutePath) ?? string.Empty);
        return VariantFolders.Contains(parent, StringComparer.Ordinal) ? parent : null;
    }

    /// <summary>
    /// 取"总线路文件夹名"（= 变体子文件夹的父目录名，如 "传奇"）。
    /// 这是变体偏好的 key——按总文件夹选一次，整个文件夹下所有线路跟随同一变体（R15.6）。
    /// 文件不在变体子文件夹里时返回 null。
    /// </summary>
    public static string? TryGetTopFolderName(string absolutePath)
    {
        if (TryGetVariantFolder(absolutePath) == null) return null;
        var variantDir = Path.GetDirectoryName(absolutePath);              // ...\传奇\A变体
        var topDir = Path.GetDirectoryName(variantDir);                    // ...\传奇
        var name = Path.GetFileName(topDir ?? string.Empty);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// 去掉扩展名 + 变体后缀，得到"线路基名"（= 配对键 + syncId 命名空间）。
    /// variantFolder 为 null 时只去扩展名。后缀匹配大小写不敏感。
    /// 例：("B001南陵传奇_a.json", "A变体") → "B001南陵传奇"。
    /// </summary>
    public static string StripBaseName(string fileName, string? variantFolder)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var suffix = SuffixFor(variantFolder);
        if (!string.IsNullOrEmpty(suffix)
            && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - suffix.Length);
        }
        return name;
    }

    /// <summary>
    /// 不依赖"文件所在变体文件夹"的基名归一化：去扩展名后，若文件名以任一变体后缀
    /// （_a/_b/_c/_d，大小写不敏感）结尾就一并去掉，得到统一的线路基名。
    /// hoeing-variant-route 死等修复：手动模式 LogicalRouteId 为空时的 fallback 命名空间专用。
    /// 旧逻辑直接用原始 FileName（带 _a/_b 后缀和 .json），导致：
    ///   - 房主路线在 A变体/ 子文件夹 → 派生出基名命名空间；
    ///   - 成员同名路线落在扁平 pathing 目录 → LogicalRouteId 为空 → fallback 用原始 FileName；
    /// 两边命名空间不一致（基名 vs F085..._a.json）→ syncId 永不相等 → 全员死等。
    /// 用本方法归一化后，无论文件在哪个目录、跑 _a 还是 _b，fallback 命名空间都收敛到同一基名。
    /// 纯函数，PBT 友好。
    /// </summary>
    public static string StripBaseNameAnyVariant(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        foreach (var folder in VariantFolders)
        {
            var suffix = SuffixFor(folder);
            if (!string.IsNullOrEmpty(suffix)
                && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - suffix.Length);
            }
        }
        return name;
    }

    /// <summary>
    /// 代表选择：给定同一基名下各变体文件夹集合，按 A→B→C→D 顺序返回第一个存在的变体文件夹。
    /// 用于 LoadFixedDebugRoutes 去重（每个基名只跑一个代表，跨玩家确定性一致）。
    /// 找不到任何变体文件夹时返回 null。
    /// </summary>
    public static string? PickRepresentativeFolder(IEnumerable<string> availableFolders)
    {
        if (availableFolders == null) return null;
        var set = new HashSet<string>(availableFolders, StringComparer.Ordinal);
        foreach (var f in VariantFolders)
            if (set.Contains(f)) return f;
        return null;
    }

    /// <summary>
    /// 偏好优先的代表选择：给定同一基名下各可用变体文件夹集合 + 该基名所属总文件夹的偏好变体，
    /// 若偏好变体存在于 availableFolders 则返回它；否则回退到 PickRepresentativeFolder（A→B→C→D 第一个存在）。
    /// preferredFolder 为 null/空（未配偏好）时逐字节等价于 PickRepresentativeFolder。
    /// 纯函数，PBT 友好。
    /// </summary>
    public static string? PickPreferredFolder(IEnumerable<string> availableFolders, string? preferredFolder)
    {
        if (availableFolders == null) return null;
        var set = new HashSet<string>(availableFolders, StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(preferredFolder) && set.Contains(preferredFolder))
            return preferredFolder;
        return PickRepresentativeFolder(availableFolders);
    }
}
