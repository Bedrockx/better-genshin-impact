using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 恰斯卡特征识别模型懒加载器。
/// 模型数据从单文件 JSON（Assets\chasca-feature-models.json，由自训练工具导出）反序列化而来，
/// 首次使用时才读取文件，之后复用内存中的模型，避免每次识别都读文件。
/// </summary>
public static class ChascaFeatureModelLoader
{
    /// <summary>
    /// 恰斯卡模型文件相对路径（Global.Absolute 解析到输出目录）。
    /// </summary>
    private const string ModelFilePath = @"GameTask\AutoFight\Assets\chasca-feature-models.json";

    /// <summary>
    /// 懒加载：初次访问时读取 JSON 并解析，之后复用。
    /// </summary>
    private static readonly Lazy<ChascaModelsJson> _models = new(Load);

    /// <summary>
    /// 子弹框模型（判断恰斯卡是否处于喷射状态：子弹框不存在即喷射）。
    /// </summary>
    public static FeatureScorerExportData BulletBoxModel => _models.Value.BulletBox;

    /// <summary>
    /// 获取指定槽位、指定元素的子弹特征模型；缺失时返回 null（对应槽位判定为空）。
    /// </summary>
    /// <param name="pos">槽位 0-5</param>
    /// <param name="elem">元素索引 0-4（风火水雷冰）</param>
    public static FeatureScorerExportData? GetBulletModel(int pos, int elem)
    {
        var slot = _models.Value.Bullets;
        if (slot.TryGetValue(pos.ToString(), out var elements)
            && elements.TryGetValue(elem.ToString(), out var model))
        {
            return model;
        }
        return null;
    }

    private static ChascaModelsJson Load()
    {
        var path = Global.Absolute(ModelFilePath);
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<ChascaModelsJson>(json, options)
               ?? throw new InvalidDataException($"恰斯卡特征模型文件解析失败：{path}");
    }
}

/// <summary>
/// 恰斯卡模型单文件 JSON 结构（对应 chasca-feature-models.json）。
/// </summary>
internal class ChascaModelsJson
{
    /// <summary>
    /// 子弹框模型。
    /// </summary>
    public FeatureScorerExportData BulletBox { get; set; } = new();

    /// <summary>
    /// 子弹模型：外层键为槽位 pos（0-5），内层键为元素索引（0-4）。
    /// </summary>
    public Dictionary<string, Dictionary<string, FeatureScorerExportData>> Bullets { get; set; } = new();
}
