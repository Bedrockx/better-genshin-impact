using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 莫版拾取黑名单过滤。白名单即莫版模板全集（有图即可拾取），黑名单为独立文件，初始为空。
/// 可交互 = 模板命中 且 交互名不在黑名单。
/// </summary>
public sealed class MojangPickFilter
{
    /// <summary>黑名单文件（每行一个交互名 Name）</summary>
    public const string BlackListPath = @"User\mojang_black_lists.txt";

    private HashSet<string> _blackList = [];

    /// <summary>重新加载黑名单文件。</summary>
    public void Init()
    {
        _blackList = Load();
    }

    /// <summary>判断交互名是否可交互（不在黑名单即可）。</summary>
    public bool ShouldPick(string name) => !_blackList.Contains(name);

    /// <summary>加入黑名单并持久化（去重）。</summary>
    public void AddToBlackList(IEnumerable<string> names)
    {
        var changed = false;
        foreach (var n in names)
        {
            if (_blackList.Add(n))
            {
                changed = true;
            }
        }

        if (changed)
        {
            Save(_blackList);
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
