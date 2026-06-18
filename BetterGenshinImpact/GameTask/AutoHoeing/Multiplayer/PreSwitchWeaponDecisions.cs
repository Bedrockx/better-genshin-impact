#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 开锄前换武器的决策/序列化纯函数（无副作用、不依赖外部状态/IO/logger）。
/// 集中承载「行启用判定」「序列化/反序列化 Group_Settings['preSwitchWeaponRows']」
/// 「构造 OcrSwitchWeaponTask 的 settings 覆盖字典」三组逻辑，便于属性测试（PBT）。
/// </summary>
public static class PreSwitchWeaponDecisions
{
    /// <summary>固定行数（联机最多 2 个可操作角色）。</summary>
    public const int RowCount = 2;

    /// <summary>Row_Configured：Character 与 Weapon 去空白后均非空。</summary>
    public static bool IsRowConfigured(PreSwitchWeaponRow? row)
    {
        return row != null
               && !string.IsNullOrWhiteSpace(row.Character)
               && !string.IsNullOrWhiteSpace(row.Weapon);
    }

    /// <summary>Row_Enabled：勾选 且 已配置（Character/Weapon 均非空）。</summary>
    public static bool IsRowEnabled(PreSwitchWeaponRow? row)
    {
        return row != null && row.Enabled && IsRowConfigured(row);
    }

    /// <summary>
    /// 把一行转成 OcrSwitchWeaponTask 需要的 settings 覆盖字典。
    /// 键名严格等于 OcrSwitchWeaponTask.ApplySettingsOverride 读取键（大小写敏感）。
    /// </summary>
    public static Dictionary<string, object?> BuildRowSettingsDict(PreSwitchWeaponRow row)
    {
        return new Dictionary<string, object?>
        {
            ["Character"] = row.Character,
            ["Weapon"] = row.Weapon,
            ["Element"] = row.Element,
            ["quickMode"] = row.QuickMode,
            ["gridPosition"] = row.GridPosition,
            ["pageScrollCount"] = row.PageScrollCount,
        };
    }

    /// <summary>
    /// 把行列表序列化为可写入 Group_Settings 的纯数据结构（List of Dictionary）。
    /// 每项含 enabled（小写）+ 6 参数键。
    /// </summary>
    public static List<Dictionary<string, object?>> SerializeRows(IReadOnlyList<PreSwitchWeaponRow> rows)
    {
        var list = new List<Dictionary<string, object?>>();
        rows ??= Array.Empty<PreSwitchWeaponRow>();
        for (int i = 0; i < RowCount; i++)
        {
            var row = i < rows.Count ? rows[i] : new PreSwitchWeaponRow();
            list.Add(new Dictionary<string, object?>
            {
                ["enabled"] = row.Enabled,
                ["Character"] = row.Character,
                ["Weapon"] = row.Weapon,
                ["Element"] = row.Element,
                ["quickMode"] = row.QuickMode,
                ["gridPosition"] = row.GridPosition,
                ["pageScrollCount"] = row.PageScrollCount,
            });
        }
        return list;
    }

    /// <summary>
    /// 从 Group_Settings 的 'preSwitchWeaponRows' 原始值解析出固定长度 RowCount 的行列表。
    /// 兼容 JsonElement 数组（STJ 反序列化 Dictionary&lt;string,object?&gt; 的默认形态）与 IEnumerable。
    /// 缺失 / 非数组 / 解析异常 → 返回 RowCount 行默认空白行（不足补默认、超出截断）。
    /// </summary>
    public static List<PreSwitchWeaponRow> ParseRows(object? raw)
    {
        var rows = new List<PreSwitchWeaponRow>();
        try
        {
            if (raw is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in je.EnumerateArray())
                    {
                        rows.Add(ParseRowFromJsonElement(item));
                    }
                }
            }
            else if (raw is IEnumerable enumerable && raw is not string)
            {
                foreach (var item in enumerable)
                {
                    rows.Add(ParseRowFromObject(item));
                }
            }
        }
        catch
        {
            // 任何解析异常都回退为默认两行（可恢复：旧/损坏配置不应阻断启动或弹窗）。
            rows.Clear();
        }

        // 归一到固定长度 RowCount：不足补默认行，超出截断。
        while (rows.Count < RowCount) rows.Add(new PreSwitchWeaponRow());
        if (rows.Count > RowCount) rows = rows.GetRange(0, RowCount);
        return rows;
    }

    private static PreSwitchWeaponRow ParseRowFromJsonElement(JsonElement obj)
    {
        var row = new PreSwitchWeaponRow();
        if (obj.ValueKind != JsonValueKind.Object) return row;

        if (obj.TryGetProperty("enabled", out var en))
        {
            row.Enabled = en.ValueKind == JsonValueKind.True
                          || (en.ValueKind == JsonValueKind.String && bool.TryParse(en.GetString(), out var b) && b);
        }
        row.Character = GetJsonString(obj, "Character", row.Character);
        row.Weapon = GetJsonString(obj, "Weapon", row.Weapon);
        row.Element = GetJsonString(obj, "Element", row.Element);
        row.GridPosition = GetJsonString(obj, "gridPosition", row.GridPosition);
        row.PageScrollCount = GetJsonString(obj, "pageScrollCount", row.PageScrollCount);
        if (obj.TryGetProperty("quickMode", out var qm))
        {
            row.QuickMode = qm.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => !bool.TryParse(qm.GetString(), out var qb) || qb,
                _ => row.QuickMode
            };
        }
        return row;
    }

    private static string GetJsonString(JsonElement obj, string name, string fallback)
    {
        if (obj.TryGetProperty(name, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString() ?? fallback;
            if (p.ValueKind == JsonValueKind.Number) return p.ToString();
        }
        return fallback;
    }

    private static PreSwitchWeaponRow ParseRowFromObject(object? item)
    {
        var row = new PreSwitchWeaponRow();
        if (item is JsonElement je) return ParseRowFromJsonElement(je);
        if (item is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue("enabled", out var en) && en is bool eb) row.Enabled = eb;
            row.Character = GetDictString(dict, "Character", row.Character);
            row.Weapon = GetDictString(dict, "Weapon", row.Weapon);
            row.Element = GetDictString(dict, "Element", row.Element);
            row.GridPosition = GetDictString(dict, "gridPosition", row.GridPosition);
            row.PageScrollCount = GetDictString(dict, "pageScrollCount", row.PageScrollCount);
            if (dict.TryGetValue("quickMode", out var qm) && qm is bool qb) row.QuickMode = qb;
        }
        else if (item is PreSwitchWeaponRow existing)
        {
            return existing;
        }
        return row;
    }

    private static string GetDictString(IDictionary<string, object?> dict, string key, string fallback)
    {
        return dict.TryGetValue(key, out var v) && v != null ? v.ToString() ?? fallback : fallback;
    }
}
