using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 黑名单管理器：背包满时OCR识别并拉黑物品
/// </summary>
public class BlacklistManager
{
    private static readonly ILogger Logger = App.GetLogger<BlacklistManager>();

    public HashSet<string> Blacklist { get; } = new();
    private string _filePath = "";
    private RecognitionObject? _itemFullRo;

    public void Load(string dataDir, string accountName)
    {
        _filePath = Path.Combine(dataDir, "blacklists", $"{accountName}.json");
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var items = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            foreach (var item in items)
                Blacklist.Add(item);
            Logger.LogInformation("已加载 {Count} 个黑名单物品", Blacklist.Count);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("加载黑名单失败: {Msg}", ex.Message);
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(Blacklist.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Logger.LogError("保存黑名单失败: {Msg}", ex.Message);
        }
    }

    public void LoadItemFullTemplate(string assetsDir)
    {
        var path = Path.Combine(assetsDir, "itemFull.png");
        if (!File.Exists(path)) return;
        var mat = Cv2.ImRead(path, ImreadModes.Color);
        // 不设ROI，全图搜索（避免缩放问题）
        _itemFullRo = RecognitionObject.TemplateMatch(mat);
        _itemFullRo.InitTemplate();
    }

    /// <summary>
    /// 背包满检测循环
    /// </summary>
    public async Task RunDetectionLoop(
        Func<bool> isRunning,
        List<TargetItem> targetItems,
        CancellationToken ct)
    {
        if (_itemFullRo == null) return;

        while (isRunning() && !ct.IsCancellationRequested)
        {
            try
            {
                // 每约1.5秒检测一次
                await Task.Delay(1500, ct);
                if (!isRunning()) break;

                using var region = CaptureToRectArea();
                using var result = region.Find(_itemFullRo);

                if (!result.IsExist()) continue;

                // 检测到背包已满，OCR识别物品名
                var scale = TaskContext.Instance().SystemInfo.AssetScale;
                var ocrRo = RecognitionObject.Ocr(
                    (int)(560 * scale), (int)(450 * scale),
                    (int)(800 * scale), (int)(170 * scale));
                var ocrResults = region.FindMulti(ocrRo);

                if (ocrResults.Count == 0) continue;

                // 取最长文本
                string ocrText = "";
                foreach (var r in ocrResults)
                {
                    if (r.Text.Length > ocrText.Length)
                        ocrText = r.Text;
                }

                // 只保留中文
                ocrText = new string(ocrText.Where(c => c >= '\u4e00' && c <= '\u9fff').ToArray());
                if (string.IsNullOrEmpty(ocrText)) continue;

                Logger.LogInformation("识别到背包已满，文本：{Text}", ocrText);

                // 滑动窗口匹配
                double maxRatio = 0;
                var matchedNames = new List<string>();

                foreach (var item in targetItems)
                {
                    var cnPart = new string(item.ItemName
                        .Where(c => c >= '\u4e00' && c <= '\u9fff').ToArray());
                    var ratio = CalcMatchRatio(cnPart, ocrText);
                    if (ratio > 0.75)
                    {
                        if (ratio > maxRatio)
                        {
                            maxRatio = ratio;
                            matchedNames.Clear();
                            matchedNames.Add(item.ItemName);
                        }
                        else if (Math.Abs(ratio - maxRatio) < 0.001)
                        {
                            matchedNames.Add(item.ItemName);
                        }
                    }
                }

                foreach (var name in matchedNames)
                {
                    if (Blacklist.Add(name))
                        Logger.LogWarning("物品 {Name} 加入黑名单（匹配度{Ratio:P1}）",
                            name, maxRatio);
                }

                if (matchedNames.Count > 0) Save();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Logger.LogDebug("黑名单检测异常: {Msg}", ex.Message);
            }
        }
    }

    /// <summary>
    /// 滑动窗口匹配度计算
    /// </summary>
    public static double CalcMatchRatio(string cnPart, string ocrText)
    {
        if (string.IsNullOrEmpty(cnPart) || string.IsNullOrEmpty(ocrText)) return 0;

        int len = cnPart.Length;
        int maxMatch = 0;

        for (int i = 0; i <= ocrText.Length - len; i++)
        {
            int match = 0;
            for (int j = 0; j < len; j++)
            {
                if (ocrText[i + j] == cnPart[j]) match++;
            }
            maxMatch = Math.Max(maxMatch, match);
        }

        return (double)maxMatch / len;
    }
}
