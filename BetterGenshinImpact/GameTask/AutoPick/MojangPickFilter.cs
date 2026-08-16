using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 莫版拾取名单过滤。
/// 白名单即莫版模板全集（有图即可拾取），在此之上叠加：
///   - 全局黑名单（独立文件，初始为空）；
///   - 配置组白名单（覆盖全局黑名单）；
///   - 配置组黑名单（优先级最高）。
/// 判定优先级（由高到低）：配置组黑名单 > 配置组白名单 > 全局黑名单 > 有图即拾取。
/// 注意：背包满自动加入的名单写入全局黑名单，不会写入配置组名单。
/// </summary>
public sealed class MojangPickFilter
{
    /// <summary>黑名单文件（每行一个交互名 Name）</summary>
    public const string BlackListPath = @"User\mojang_black_lists.txt";

    /// <summary>全局黑名单（文件持久化）</summary>
    private HashSet<string> _globalBlackList = [];

    /// <summary>当前配置组白名单（仅引用，内容由配置组对象实时同步）</summary>
    private HashSet<string> _groupWhiteList = [];

    /// <summary>当前配置组黑名单（仅引用，内容由配置组对象实时同步）</summary>
    private HashSet<string> _groupBlackList = [];

    /// <summary>当前生效的配置组名（null 表示全局模式）</summary>
    public string? CurrentGroupName { get; private set; }

    /// <summary>重新加载全局黑名单文件。</summary>
    public void Init()
    {
        _globalBlackList = Load();
    }

    /// <summary>
    /// 切换当前配置组并引用其名单；组名/配置组为 null 时仅使用全局名单。
    /// </summary>
    public void SetCurrentGroup(string? groupName, AutoPickGroupConfig? groupConfig)
    {
        CurrentGroupName = groupName;
        _groupWhiteList = groupConfig?.WhiteList ?? [];
        _groupBlackList = groupConfig?.BlackList ?? [];
    }

    /// <summary>
    /// 判断交互名是否可交互。
    /// 优先级：配置组黑名单 > 配置组白名单 > 全局黑名单 > 有图即拾取。
    /// </summary>
    public bool ShouldPick(string name)
    {
        if (_groupBlackList.Contains(name))
        {
            return false;
        }

        if (_groupWhiteList.Contains(name))
        {
            return true;
        }

        return !_globalBlackList.Contains(name);
    }

    /// <summary>加入全局黑名单并持久化（去重）。</summary>
    public void AddToBlackList(IEnumerable<string> names)
    {
        var changed = false;
        foreach (var n in names)
        {
            if (_globalBlackList.Add(n))
            {
                changed = true;
            }
        }

        if (changed)
        {
            Save(_globalBlackList);
        }
    }

    /// <summary>读取黑名单文件。</summary>
    public static HashSet<string> Load()
    {
        try
        {
            var txt = Global.ReadAllTextIfExist(BlackListPath);
            if (!string.IsNullOrEmpty(txt))
            {
                return new HashSet<string>(txt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch (Exception e)
        {
            App.GetLogger<MojangPickFilter>().LogError(e, "读取莫版拾取黑名单失败");
        }

        return [];
    }

    /// <summary>保存黑名单文件（按交互名排序）。</summary>
    public static void Save(IEnumerable<string> names)
    {
        Global.WriteAllText(BlackListPath, string.Join(Environment.NewLine, names.OrderBy(n => n, StringComparer.Ordinal)));
    }
}
