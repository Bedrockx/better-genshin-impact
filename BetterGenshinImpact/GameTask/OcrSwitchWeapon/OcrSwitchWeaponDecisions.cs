using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BetterGenshinImpact.GameTask.OcrSwitchWeapon;

/// <summary>
/// OCR切换武器任务的纯函数决策类（无副作用、不依赖任何外部状态/IO/logger）。
/// 严格对齐 JS 脚本 <c>User/JsScript/OCR切换武器/main.js</c> 的同名逻辑，
/// 便于属性测试（PBT）与单元测试。参考 bgi-implementation-patterns 决策函数纯化模式。
/// </summary>
public static class OcrSwitchWeaponDecisions
{
    /// <summary>剔除干扰词集合（对应 JS calculateMatchRatio 的 ignoreWords）。</summary>
    private static readonly HashSet<char> IgnoreWords = ['剑', '之', '弓', '枪', '长', '大', '典', '章'];

    /// <summary>
    /// 标准编辑距离（Levenshtein）。对应 JS fuzzyMatch 内嵌的 levenshteinDistance。
    /// </summary>
    public static int LevenshteinDistance(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        int m = a.Length + 1;
        int n = b.Length + 1;
        var d = new int[m, n];
        for (int i = 0; i < m; i++) d[i, 0] = i;
        for (int j = 0; j < n; j++) d[0, j] = j;
        for (int i = 1; i < m; i++)
        {
            for (int j = 1; j < n; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[m - 1, n - 1];
    }

    /// <summary>
    /// 模糊匹配。对应 JS fuzzyMatch：
    /// 对每个 candidate，weight = (candidate.Contains(target) ? 0.8 : 0)
    /// + (1 - dist/Max(target.Length, candidate.Length)) * 0.2，其中 dist = LevenshteinDistance(target, candidate)。
    /// 遍历过程中首个 weight ≥ weightThreshold 的 candidate 立即返回；否则返回 bestWeight 最大者。
    /// candidates 为空返回 null。
    /// 除零保护：当 Max(len)=0（target 与 candidate 都为空串）时，长度项记为 0（对齐 JS 中 1 - dist/0 = NaN
    /// 不进入 weight 比较的语义，使 weight 仅剩关键字项）。
    /// </summary>
    public static string? FuzzyMatch(string target, IReadOnlyList<string> candidates, double weightThreshold = 0.6)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        target ??= string.Empty;

        string? bestMatch = null;
        double bestWeight = 0;

        foreach (var candidate in candidates)
        {
            var cand = candidate ?? string.Empty;
            int distance = LevenshteinDistance(target, cand);
            const double keywordWeight = 0.8;
            const double lengthWeight = 0.2;
            bool keywordMatch = cand.Contains(target);
            int maxLen = Math.Max(target.Length, cand.Length);
            // 除零保护：maxLen==0 时长度项取 0（对齐 JS NaN 不影响后续比较的效果）。
            double lengthTerm = maxLen == 0 ? 0 : (1 - (double)distance / maxLen) * lengthWeight;
            double weight = (keywordMatch ? keywordWeight : 0) + lengthTerm;

            if (weight >= weightThreshold)
            {
                return candidate;
            }

            if (weight > bestWeight)
            {
                bestWeight = weight;
                bestMatch = candidate;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// 匹配占比计算。对应 JS calculateMatchRatio：
    /// 先从 target、candidate 各自剔除干扰词 [剑/之/弓/枪/长/大/典/章]，得 targetClean、candidateClean；
    /// common = targetClean 中逐字符（不去重）出现在 candidateClean 里的字符个数；
    /// 返回 common / targetClean.Length。targetClean.Length == 0 时返回 0.0
    /// （对齐 JS NaN → 不匹配，避免除零）。结果恒 ∈ [0,1]。
    /// </summary>
    public static double MatchRatio(string target, string candidate)
    {
        target ??= string.Empty;
        candidate ??= string.Empty;

        var targetClean = new string(target.Where(c => !IgnoreWords.Contains(c)).ToArray());
        var candidateClean = new string(candidate.Where(c => !IgnoreWords.Contains(c)).ToArray());

        if (targetClean.Length == 0)
        {
            return 0.0;
        }

        int common = targetClean.Count(c => candidateClean.Contains(c));
        return (double)common / targetClean.Length;
    }

    /// <summary>
    /// 众数合并。对应 JS combineResults：
    /// 统计各结果出现频次，按频次降序排序得 sortedResults（相同频次保持首次出现顺序，稳定排序）。
    /// 优先返回 sortedResults 中首个 Length == 2 的元素；否则若 sortedResults.Count ≥ 2，
    /// 返回 sortedResults[0] + sortedResults[1] 拼接；否则返回 sortedResults[0]（若存在）；空集返回 ""。
    /// </summary>
    public static string CombineResults(IReadOnlyList<string> results)
    {
        if (results == null || results.Count == 0)
        {
            return string.Empty;
        }

        // 统计频次，并记录每个 key 的首次出现顺序（对齐 JS Object.keys 的插入顺序）。
        var frequency = new Dictionary<string, int>();
        var insertionOrder = new List<string>();
        foreach (var raw in results)
        {
            var result = raw ?? string.Empty;
            if (!frequency.ContainsKey(result))
            {
                frequency[result] = 0;
                insertionOrder.Add(result);
            }
            frequency[result]++;
        }

        // 频次降序、相同频次保持首次出现顺序（OrderByDescending 是稳定排序）。
        var sortedResults = insertionOrder
            .OrderByDescending(key => frequency[key])
            .ToList();

        foreach (var result in sortedResults)
        {
            if (result.Length == 2)
            {
                return result;
            }
        }

        if (sortedResults.Count >= 2)
        {
            return sortedResults[0] + sortedResults[1];
        }

        return sortedResults.Count > 0 ? sortedResults[0] : string.Empty;
    }

    /// <summary>
    /// pageScrollCount 钳制。对应 JS Math.min(99, Math.max(0, Math.floor(Number(settings.pageScrollCount) || 2)))。
    /// 解析 raw 为数字（double）；解析失败、空/null、或解析得到 0（JS 中 0 为 falsy，`0 || 2` = 2）→ 回退 2；
    /// 否则 Floor 后 Clamp 到 [0,99]。结果恒 ∈ [0,99]。
    /// </summary>
    public static int ClampPageScrollCount(string? raw)
    {
        // 对齐 JS Number(x)：非数字/空 → NaN，NaN || 2 = 2；数字 0 → 0 || 2 = 2。
        // 使用默认 TryParse 重载，与 PBT Property 2 的 numeric 判定（double.TryParse(raw, out _)）保持一致。
        if (string.IsNullOrWhiteSpace(raw) ||
            !double.TryParse(raw, out double parsed) ||
            double.IsNaN(parsed) ||
            parsed == 0)
        {
            parsed = 2;
        }

        int floored = (int)Math.Floor(parsed);
        return Math.Min(99, Math.Max(0, floored));
    }

    /// <summary>
    /// gridPosition 解析+校验。对应 JS scanWeaponsQuick 的 gridPosition 解析逻辑：
    /// len==3：row=前两位、col=第三位；len==2：row=首位、col=次位；其它长度 → Ok=false。
    /// 解析失败（非数字）→ Ok=false。校验 row ∈ [1,99] 且 col ∈ [1,4]，否则 Ok=false。
    /// Ok=false 时 Row/Col 返回 0。
    /// </summary>
    public static (bool Ok, int Row, int Col) ParseGridPosition(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (false, 0, 0);
        }

        int row, col;
        if (raw.Length == 3)
        {
            if (!int.TryParse(raw.Substring(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out row) ||
                !int.TryParse(raw.Substring(2, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            {
                return (false, 0, 0);
            }
        }
        else if (raw.Length == 2)
        {
            if (!int.TryParse(raw.Substring(0, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out row) ||
                !int.TryParse(raw.Substring(1, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out col))
            {
                return (false, 0, 0);
            }
        }
        else
        {
            return (false, 0, 0);
        }

        if (row < 1 || row > 99 || col < 1 || col > 4)
        {
            return (false, 0, 0);
        }

        return (true, row, col);
    }
}
