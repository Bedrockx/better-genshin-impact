using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.OcrSwitchWeapon.Data;

/// <summary>
/// 复用 BGI 本体既有 <c>combat_avatar.json</c>，构建角色数据映射。
/// 对应 JS <c>loadCombatAvatarData</c>：
/// - 别名/正式名 → 正式名（<see cref="AliasToName"/>）
/// - 正式名 → 武器类型编码（<see cref="NameToWeaponType"/>）
/// - 所有正式名列表（<see cref="FormalNames"/>，供 FuzzyMatch 候选）
/// </summary>
public class CombatAvatarRepository
{
    /// <summary>别名/正式名 → 正式名。</summary>
    public IReadOnlyDictionary<string, string> AliasToName { get; }

    /// <summary>正式名 → weapon 武器类型编码（1/10/11/12/13）。</summary>
    public IReadOnlyDictionary<string, string> NameToWeaponType { get; }

    /// <summary>所有正式名，供 FuzzyMatch 候选。</summary>
    public IReadOnlyList<string> FormalNames { get; }

    private CombatAvatarRepository(
        IReadOnlyDictionary<string, string> aliasToName,
        IReadOnlyDictionary<string, string> nameToWeaponType,
        IReadOnlyList<string> formalNames)
    {
        AliasToName = aliasToName;
        NameToWeaponType = nameToWeaponType;
        FormalNames = formalNames;
    }

    /// <summary>
    /// 从 <c>Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json")</c> 读取，
    /// Newtonsoft 反序列化为 <see cref="IEnumerable{CombatAvatar}"/>。
    /// 解析失败让异常抛出（由 Task 层捕获转 LogError 终止，本类不吞，对应 R10.4）。
    /// </summary>
    public static CombatAvatarRepository Load()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoFight\Assets\combat_avatar.json"));
        var avatars = JsonConvert.DeserializeObject<IEnumerable<CombatAvatar>>(json)
                      ?? throw new Exception("combat_avatar.json 反序列化结果为空");

        // 用 Dictionary 索引赋值（重复 key 覆盖），不用 ToDictionary 以免重复 key 抛异常
        var aliasToName = new Dictionary<string, string>();
        var nameToWeaponType = new Dictionary<string, string>();
        var formalNames = new List<string>();

        foreach (var character in avatars)
        {
            aliasToName[character.Name] = character.Name;
            nameToWeaponType[character.Name] = character.Weapon;
            formalNames.Add(character.Name);

            foreach (var alias in character.Alias)
            {
                aliasToName[alias] = character.Name;
            }
        }

        return new CombatAvatarRepository(aliasToName, nameToWeaponType, formalNames);
    }
}
