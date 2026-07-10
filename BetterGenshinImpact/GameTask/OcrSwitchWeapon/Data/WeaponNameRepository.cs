using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.OcrSwitchWeapon.Data;

/// <summary>
/// 加载随任务携带的 <c>weaponName.json</c>，构建「武器类型中文名 → 武器名列表」映射。
/// 对应 JS <c>loadWeaponNames</c>。
/// </summary>
public class WeaponNameRepository
{
    /// <summary>武器类型中文名（如「单手剑」） → 该类型下的武器名列表。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ByWeaponType { get; }

    private WeaponNameRepository(IReadOnlyDictionary<string, IReadOnlyList<string>> byWeaponType)
    {
        ByWeaponType = byWeaponType;
    }

    /// <summary>
    /// 从 <c>Global.Absolute(@"GameTask\OcrSwitchWeapon\Assets\weaponName.json")</c> 读取。
    /// JSON 结构（对应 JS）：顶层是数组，每项是单键对象，如 <c>{"单手剑":["磐岩结绿", ...]}</c>。
    /// 反序列化为 <c>List&lt;Dictionary&lt;string, List&lt;string&gt;&gt;&gt;</c>，遍历每项的每个 entry 合并进映射。
    /// 解析失败让异常抛出（由 Task 层捕获转 LogError 终止，对应 R10.4）。
    /// </summary>
    public static WeaponNameRepository Load()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\OcrSwitchWeapon\Assets\weaponName.json"));
        var parsed = JsonConvert.DeserializeObject<List<Dictionary<string, List<string>>>>(json)
                     ?? throw new Exception("weaponName.json 反序列化结果为空");

        var byWeaponType = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var item in parsed)
        {
            foreach (var entry in item)
            {
                // 键=武器类型中文名，值=武器名列表（重复键覆盖）
                byWeaponType[entry.Key] = entry.Value;
            }
        }

        return new WeaponNameRepository(byWeaponType);
    }
}
