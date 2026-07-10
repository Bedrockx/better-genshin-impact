using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoSwitchRoles;

/// <summary>
/// 模式选择枚举（对齐 JS option 字段三分支）。
/// </summary>
public enum SwitchRolesMode
{
    /// <summary>推荐-非快速配对模式 @Tool_tingsu</summary>
    Recommended,

    /// <summary>存在bug-快速配对模式 @兩夢三醒</summary>
    QuickPair,

    /// <summary>未知模式（含 null/空）</summary>
    Unknown
}

/// <summary>
/// 配对界面切换角色任务的纯函数判定层。
/// 无 IO/UI/logger 依赖，全部为可被属性测试直接覆盖的纯函数，逻辑 1:1 对齐 JS 脚本同名逻辑。
/// </summary>
public static class AutoSwitchRolesDecisions
{
    /// <summary>combat_avatar.json 单条记录模型（仅取 name + alias，对齐 JS readAliases）。</summary>
    private sealed class CombatAvatarEntry
    {
        public string? Name { get; set; }
        public List<string>? Alias { get; set; }
    }

    /// <summary>
    /// 候选 1：别名映射构建（R5.1 / R4.7，对应 JS readAliases）。
    /// 解析 [{name, alias[]}]，对每个 entry：name 非空且 alias 非空时，把每个非空 alias 映射到该 entry 的 name。
    /// 后出现的别名覆盖先出现的（对齐 JS 对象赋值语义）。
    /// 解析失败时让 JsonException 抛出，由调用方（LoadAliasMap）按致命处理。
    /// </summary>
    public static Dictionary<string, string> BuildAliasMap(string combatAvatarJson)
    {
        var map = new Dictionary<string, string>();
        var entries = JsonSerializer.Deserialize<List<CombatAvatarEntry>>(combatAvatarJson);
        if (entries == null)
        {
            return map;
        }

        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Name) || entry.Alias == null)
            {
                continue;
            }

            foreach (var alias in entry.Alias)
            {
                if (!string.IsNullOrEmpty(alias))
                {
                    map[alias] = entry.Name;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// 候选 1：号位输入解析（R5.2 / R11.4，对应 JS aliases[input] || input）。
    /// input 为 null 或 trim 后为空 → 返回 null；否则返回 aliasMap[trimmed] ?? trimmed。
    /// </summary>
    public static string? ResolvePosition(string? input, IReadOnlyDictionary<string, string> aliasMap)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return aliasMap.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>
    /// 候选 2：attribute.txt 解析（R11.1~R11.3，对应 JS split(/，\s*/)）。
    /// 按行处理，每行先 Trim；按中文逗号（可后随空白）分割，取前三段为 name/element/weapon；
    /// 空段 → null；name（第一段去空白后）为空的行忽略；同名后行覆盖前行（对齐 JS 对象赋值）。
    /// </summary>
    public static Dictionary<string, (string? Element, string? Weapon)> ParseAttribute(string text)
    {
        var dict = new Dictionary<string, (string? Element, string? Weapon)>();
        if (text == null)
        {
            return dict;
        }

        var lines = text.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // 中文逗号 `，` 后可跟随空白（等价 JS split(/，\s*/)）。
            var parts = Regex.Split(line, "，\\s*");

            var name = parts.Length > 0 ? EmptyToNull(parts[0]) : null;
            var element = parts.Length > 1 ? EmptyToNull(parts[1]) : null;
            var weapon = parts.Length > 2 ? EmptyToNull(parts[2]) : null;

            // 角色名段为空的行忽略（对齐 JS if (name)）。
            if (name == null)
            {
                continue;
            }

            dict[name] = (element, weapon);
        }

        return dict;
    }

    /// <summary>空字符串 → null（对齐 JS item || null）。</summary>
    private static string? EmptyToNull(string? item)
        => string.IsNullOrEmpty(item) ? null : item;

    /// <summary>
    /// 候选 3：目标角色数组构建（R5.3，对应 JS initialAvatars[index] = actualName）。
    /// 结果长度恒等于 initialAvatars.Length；对 index 0..3：当 index < Length 且 positionResolved[index] != null
    /// 时替换为该值，否则保持原值。超出 initialAvatars.Length 的设置项跳过（防越界）。
    /// </summary>
    public static string[] BuildTargetAvatars(string[] initialAvatars, IReadOnlyList<string?> positionResolved)
    {
        var result = new string[initialAvatars.Length];
        Array.Copy(initialAvatars, result, initialAvatars.Length);

        for (var index = 0; index < 4; index++)
        {
            if (index < initialAvatars.Length && index < positionResolved.Count && positionResolved[index] != null)
            {
                result[index] = positionResolved[index]!;
            }
        }

        return result;
    }

    /// <summary>
    /// 候选 4：全空判定（R5.4，对应 JS positionSettings.every(item => !item)）。
    /// 所有元素为 null → true（空列表也 true）。
    /// </summary>
    public static bool IsAllEmpty(IReadOnlyList<string?> positionResolved)
    {
        for (var i = 0; i < positionResolved.Count; i++)
        {
            if (positionResolved[i] != null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 候选 5：模式选择（R6.3~R6.5）。
    /// </summary>
    public static SwitchRolesMode SelectMode(string? option)
    {
        return option switch
        {
            "推荐-非快速配对模式 @Tool_tingsu" => SwitchRolesMode.Recommended,
            "存在bug-快速配对模式 @兩夢三醒" => SwitchRolesMode.QuickPair,
            _ => SwitchRolesMode.Unknown
        };
    }

    /// <summary>
    /// 候选 6：数组相等（R10.2，对应 JS arraysEqual）。
    /// 长度相等且逐位置 Ordinal 相等 → true。
    /// </summary>
    public static bool AvatarsEqual(IReadOnlyList<string> target, IReadOnlyList<string> final)
    {
        if (target.Count != final.Count)
        {
            return false;
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (!StringComparer.Ordinal.Equals(target[i], final[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 候选 7：角色头像文件名（R4.6，对应 JS `${name}${paddedNum}`，NN 两位补零）。
    /// </summary>
    public static string CharacterImageFileName(string name, int index)
        => $"{name}{index:D2}";

    /// <summary>
    /// 候选 8：打开配对单轮重试决策（R7.2，对应 JS openTries &lt; 3）。
    /// </summary>
    public static bool ShouldRetrySingleRound(int openTries) => openTries < 3;

    /// <summary>
    /// 候选 8：打开配对累计传送重开决策（R7.3 / R7.4，对应 JS totalTries &lt; 6）。
    /// </summary>
    public static bool ShouldTeleportAndReopen(int totalTries) => totalTries < 6;
}
