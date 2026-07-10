using BetterGenshinImpact.GameTask.AutoHoeing.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 怪物信息表仓库，从 monsterInfo.json 加载怪物数据
/// </summary>
public class MonsterInfoRepository
{
    private readonly ILogger _logger = App.GetLogger<MonsterInfoRepository>();
    private List<MonsterInfoEntry> _monsters = new();
    private Dictionary<string, MonsterInfoEntry> _monsterMap = new();

    /// <summary>
    /// 从文件加载怪物信息
    /// </summary>
    public void Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("怪物信息文件不存在: {Path}", filePath);
            return;
        }

        var json = File.ReadAllText(filePath);
        _monsters = JsonSerializer.Deserialize<List<MonsterInfoEntry>>(json) ?? new();
        _monsterMap = _monsters.ToDictionary(m => m.Name, m => m);
        _logger.LogInformation("已加载 {Count} 条怪物信息", _monsters.Count);
    }

    /// <summary>
    /// 按名称查询怪物信息
    /// </summary>
    public MonsterInfoEntry? FindByName(string name)
    {
        return _monsterMap.GetValueOrDefault(name);
    }

    public List<MonsterInfoEntry> GetAll() => _monsters;
}
