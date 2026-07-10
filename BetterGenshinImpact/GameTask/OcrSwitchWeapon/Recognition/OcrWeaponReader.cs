using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.OcrSwitchWeapon.Recognition;

/// <summary>
/// 区域 OCR 武器/角色名识别器。对应 JS 脚本 <c>User/JsScript/OCR切换武器/main.js</c> 的
/// <c>recognizeAndCombineWeaponName</c> / <c>recognizeText</c> 逻辑：
/// 固定基准坐标区域 OCR + 错字修正（ReplacementMap）+ 众数合并（OcrSwitchWeaponDecisions.CombineResults）。
/// 持有 <see cref="CancellationToken"/> 因为多次 OCR 尝试之间需要 Delay。
/// </summary>
public class OcrWeaponReader
{
    private readonly ILogger<OcrWeaponReader> _logger = App.GetLogger<OcrWeaponReader>();
    private readonly CancellationToken _ct;

    /// <summary>武器名 OCR 区域，固定 1920×1080 基准坐标（R9.1，对齐 JS）。</summary>
    private static readonly Rect OcrRegion = new(1463, 135, 256, 32);

    /// <summary>
    /// OCR 错字修正表（硬编码，对应 JS replacementMap，R9.2 / OQ4）。
    /// 每个 wrongChar → correctChar，对识别文本做全字符替换。
    /// </summary>
    private static readonly Dictionary<string, string> ReplacementMap = new()
    {
        ["卵"] = "卯",
        ["姐"] = "妲",
        ["去"] = "云",
        ["日"] = "甘",
        ["螨"] = "螭",
        ["知"] = "矢",
        ["钱"] = "钺",
        ["础"] = "咄",
        ["厘"] = "匣",
        ["排"] = "绯",
        ["朦"] = "曚",
        ["矿"] = "斫",
        ["镰"] = "簾",
        ["廉"] = "簾",
        ["救"] = "赦",
        ["塑"] = "槊",
        ["雍"] = "薙",
    };

    public OcrWeaponReader(CancellationToken ct)
    {
        _ct = ct;
    }

    /// <summary>
    /// 对识别文本应用 <see cref="ReplacementMap"/> 全字符替换。
    /// </summary>
    private static string ApplyReplacement(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        foreach (var (wrongChar, correctChar) in ReplacementMap)
        {
            text = text.Replace(wrongChar, correctChar);
        }

        return text;
    }

    /// <summary>
    /// 多次（默认 5）区域 OCR + 错字修正，最后用众数合并得到武器名。
    /// 对应 JS <c>recognizeAndCombineWeaponName</c>。武器名识别阈值 0.9。
    /// </summary>
    public async Task<string> RecognizeAndCombineWeaponNameAsync(int maxAttempts = 5)
    {
        var allResults = new List<string>();

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using var ra = TaskControl.CaptureToRectArea();
                var ocrRo = RecognitionObject.Ocr(OcrRegion.X, OcrRegion.Y, OcrRegion.Width, OcrRegion.Height);
                ocrRo.Threshold = 0.9; // 武器名识别阈值（对齐 JS）
                var resList = ra.FindMulti(ocrRo);
                foreach (var region in resList)
                {
                    var corrected = ApplyReplacement(region.Text);
                    allResults.Add(corrected);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消异常必须向上传播，不能被吞掉（R1.3 取消透传）。
                throw;
            }
            catch (Exception ex)
            {
                // 单次 OCR 失败不应中断整个识别流程（对齐 JS：单次异常记日志后继续重试）。
                _logger.LogWarning(ex, "武器名单次 OCR 识别失败，继续重试（第 {Attempt}/{Max} 次）", attempt + 1, maxAttempts);
            }

            await TaskControl.Delay(20, _ct);
        }

        return OcrSwitchWeaponDecisions.CombineResults(allResults);
    }

    /// <summary>
    /// 角色名识别：区域 OCR + 错字修正 + 别名映射 + 模糊匹配（候选=所有正式名），
    /// 命中目标正式名返回 true。对应 JS <c>recognizeText</c> / <c>selectCharacter</c> 内部判定。
    /// 角色名识别阈值 0.8。
    /// </summary>
    public async Task<bool> RecognizeCharacterAsync(string targetFormalName,
        IReadOnlyList<string> formalNames, IReadOnlyDictionary<string, string> aliasToName)
    {
        try
        {
            using var ra = TaskControl.CaptureToRectArea();
            var ocrRo = RecognitionObject.Ocr(OcrRegion.X, OcrRegion.Y, OcrRegion.Width, OcrRegion.Height);
            ocrRo.Threshold = 0.8; // 角色名识别阈值（对齐 JS）
            var resList = ra.FindMulti(ocrRo);
            foreach (var region in resList)
            {
                var correctedText = ApplyReplacement(region.Text);

                // 别名 → 正式名（命中则取正式名，否则用修正后文本）。
                var recognizedFormalName = aliasToName.TryGetValue(correctedText, out var mapped)
                    ? mapped
                    : correctedText;

                // 在所有正式名中模糊匹配，命中则覆盖（对齐 JS：fuzzyMatch 优先于别名映射结果）。
                recognizedFormalName = OcrSwitchWeaponDecisions.FuzzyMatch(correctedText, formalNames)
                    ?? recognizedFormalName;

                if (recognizedFormalName == targetFormalName)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 取消异常必须向上传播，不能被吞掉（R1.3 取消透传）。
            throw;
        }
        catch (Exception ex)
        {
            // 单次 OCR 失败不应中断角色筛选循环（由上层 SelectCharacter 控制重试/切换）。
            _logger.LogWarning(ex, "角色名单次 OCR 识别失败，本次视为未命中目标角色 {Target}", targetFormalName);
        }

        return false;
    }
}
