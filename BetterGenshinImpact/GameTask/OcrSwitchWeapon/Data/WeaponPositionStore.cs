using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.OcrSwitchWeapon.Data;

/// <summary>
/// 运行时武器位置记录持久化：武器名 → 行*10+列。
/// 落 <c>User\OcrSwitchWeapon\weapon_positions.json</c>（方案 A，与 JS 脚本同目录语义最接近）。
/// 读失败/不存在以空记录开始（可恢复，不抛）；写失败 LogWarning 继续（R11.4）。
/// </summary>
public class WeaponPositionStore
{
    private static readonly ILogger<WeaponPositionStore> Logger = App.GetLogger<WeaponPositionStore>();

    private readonly Dictionary<string, int> _map;

    /// <summary>持久化文件路径；为 null 表示内存模式（测试用），不落盘。</summary>
    private readonly string? _filePath;

    private WeaponPositionStore(Dictionary<string, int> map, string? filePath)
    {
        _map = map;
        _filePath = filePath;
    }

    /// <summary>
    /// 从 <c>Global.Absolute(@"User\OcrSwitchWeapon\weapon_positions.json")</c> 读取。
    /// 文件不存在或解析失败 → 空记录开始（R11.2，可恢复，LogInformation，不抛）。
    /// </summary>
    public static WeaponPositionStore Load()
    {
        var filePath = Global.Absolute(@"User\OcrSwitchWeapon\weapon_positions.json");
        var map = new Dictionary<string, int>();
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                map = JsonConvert.DeserializeObject<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
            }
            else
            {
                Logger.LogInformation("武器位置记录文件不存在，以空记录开始：{FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            // 可恢复异常：解析失败时以空记录开始，不影响切换流程（R11.2）
            Logger.LogInformation(ex, "武器位置记录文件解析失败，以空记录开始：{FilePath}", filePath);
            map = new Dictionary<string, int>();
        }

        return new WeaponPositionStore(map, filePath);
    }

    /// <summary>内存构造（filePath=null，不落盘），供 PBT Property 7 往返测试使用。</summary>
    public static WeaponPositionStore CreateInMemoryForTest()
    {
        return new WeaponPositionStore(new Dictionary<string, int>(), null);
    }

    /// <summary>查询武器名对应的格子位置（行*10+列）；不存在返回 null。</summary>
    public int? TryGet(string weaponName)
    {
        return _map.TryGetValue(weaponName, out var pos) ? pos : null;
    }

    /// <summary>
    /// 记录武器位置：更新内存 map 并尝试落盘。
    /// filePath 为 null（测试）时跳过落盘；落盘失败 LogWarning 继续，不抛（R11.4）。
    /// </summary>
    public void Record(string weaponName, int gridPos)
    {
        _map[weaponName] = gridPos;

        if (_filePath == null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_map, Formatting.Indented));
        }
        catch (Exception ex)
        {
            // 落盘失败不影响本次切换：内存记录已更新，仅丢失持久化（R11.4）
            Logger.LogWarning(ex, "保存武器位置记录失败");
        }
    }
}
