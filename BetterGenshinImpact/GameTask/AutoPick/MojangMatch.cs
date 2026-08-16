using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 莫版匹配结果
/// </summary>
public readonly record struct MojangMatchResult(
    string Name, string ItemName, double Score, Rect Region, Rect TemplateRect,
    double JudgeMs, double GrayMs, double NccMs, int TemplateCount, int ColorIndex);

/// <summary>
/// 莫版模板清单项（供黑白名单编辑界面展示）。
/// </summary>
public readonly record struct MojangTemplateInfo(string Name, string Color, int Len);

/// <summary>
/// 莫版匹配：基于文字颜色先验的特化模板匹配，替代 OCR 识别拾取物品。
/// 与原自动拾取逻辑相互独立，仅通过 <see cref="Match"/> 对外提供识别能力。
/// </summary>
public sealed class MojangMatch
{
    /// <summary>线性截断阈值：匹配度 = 255 * max(0, 1 - dE/T)</summary>
    private const double T = 80.0;

    /// <summary>参与颜色判定 / 灰度化归零的最小亮度 V</summary>
    private const int VoteMinV = 180;

    /// <summary>NCC 匹配度下限（读配置）</summary>
    private static double MinScore => TaskContext.Instance().Config.AutoPickConfig.MatchThreshold;

    /// <summary>bin 文件魔数 ("MBMB")</summary>
    private const int BinMagic = 0x4D424D42;

    /// <summary>1080p 下文字区域宽度（模板宽，X 方向精确不滑动）</summary>
    private const int RegionWidth = 140;

    /// <summary>1080p 下文字区域高度（模板高 26 + 上下各 1px）</summary>
    private const int RegionHeight = 28;

    /// <summary>1080p 下文字区域相对 F 键的左侧偏移（模板实际 X）</summary>
    private const int TextLeftOffset = 122;

    /// <summary>5 种参考文字颜色 (名称, R, G, B)</summary>
    private static readonly (string Name, byte R, byte G, byte B)[] Refs =
    [
        ("灰", 204, 204, 204),
        ("绿", 172, 255, 69),
        ("蓝", 79, 244, 255),
        ("紫", 249, 152, 255),
        ("白", 255, 255, 255),
    ];

    private static readonly double[][] RefLabs = Refs.Select(r => RgbToLab(r.R, r.G, r.B)).ToArray();

    /// <summary>颜色展示顺序，与 <see cref="Refs"/> 一致</summary>
    private static readonly string[] ColorOrder = Refs.Select(r => r.Name).ToArray();

    private static readonly Lazy<MojangMatch> LazyInstance = new(Load);

    public static MojangMatch Instance => LazyInstance.Value;

    /// <summary>
    /// 启动时预加载模板，避免首次识别时才触发加载（加载日志随启动输出）。
    /// </summary>
    public static void Preload()
    {
        _ = Instance;
    }

    private readonly Dictionary<(string Color, int Len), List<MojangTemplate>> _templatesByColorAndLen = [];

    private readonly Dictionary<string, MojangTemplate> _templatesByName = [];

    private readonly ILogger _logger = App.GetLogger<MojangMatch>();

    private MojangMatch()
    {
    }

    /// <summary>
    /// 获取全部模板清单（按颜色、交互名排序），供黑白名单编辑界面展示。
    /// </summary>
    public IReadOnlyList<MojangTemplateInfo> GetTemplateInfos()
    {
        return _templatesByName.Values
            .OrderBy(t => Array.IndexOf(ColorOrder, t.Color))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new MojangTemplateInfo(t.Name, t.Color, t.Len))
            .ToList();
    }

    /// <summary>
    /// 根据满背包提示文字（物品名或交互名），查找对应的交互名（Name）列表。
    /// 返回与文本匹配度最高（且 > 0.75）的模板的交互名。
    /// </summary>
    public IReadOnlyList<string> FindNamesByItemText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var pureText = KeepChinese(text);
        if (pureText.Length == 0)
        {
            return [];
        }

        var best = new List<string>();
        var bestRatio = 0.0;
        foreach (var t in _templatesByName.Values)
        {
            var ratio = Math.Max(MatchRatio(KeepChinese(t.Name), pureText),
                                 MatchRatio(KeepChinese(t.ItemName), pureText));
            if (ratio < 0.75)
            {
                continue;
            }

            if (ratio > bestRatio + 1e-9)
            {
                bestRatio = ratio;
                best = [t.Name];
            }
            else if (Math.Abs(ratio - bestRatio) <= 1e-9)
            {
                best.Add(t.Name);
            }
        }

        return best;
    }

    /// <summary>仅保留中文字符。</summary>
    private static string KeepChinese(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return new string(s.Where(c => c >= '\u4e00' && c <= '\u9fff').ToArray());
    }

    /// <summary>子串匹配比例：part 在 text 中滑动，返回最长公共匹配字符数 / part 长度。</summary>
    private static double MatchRatio(string part, string text)
    {
        if (string.IsNullOrEmpty(part) || string.IsNullOrEmpty(text) || part.Length > text.Length)
        {
            return 0;
        }

        var len = part.Length;
        var maxMatch = 0;
        for (var i = 0; i <= text.Length - len; i++)
        {
            var match = 0;
            for (var j = 0; j < len; j++)
            {
                if (text[i + j] == part[j])
                {
                    match++;
                }

                maxMatch = Math.Max(maxMatch, match);
            }
        }

        return (double)maxMatch / len;
    }

    /// <summary>
    /// 识别 F 键右侧文字区域对应的物品。
    /// </summary>
    /// <param name="srcMat">捕获区域源图（BGR）</param>
    /// <param name="fKeyRegion">F 键识别结果区域</param>
    /// <param name="scale">分辨率缩放系数</param>
    /// <returns>识别结果；未识别到或区域越界返回 null</returns>
    public MojangMatchResult? Match(Mat srcMat, Region fKeyRegion, double scale)
    {
        var centerY = fKeyRegion.Y + fKeyRegion.Height / 2;
        var rect = new Rect(
            fKeyRegion.X + (int)(TextLeftOffset * scale),
            centerY - (int)(RegionHeight / 2.0 * scale),
            (int)(RegionWidth * scale),
            (int)(RegionHeight * scale));

        if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > srcMat.Width || rect.Y + rect.Height > srcMat.Height)
        {
            return null;
        }

        using var roiMat = new Mat(srcMat, rect);
        using var bgrMat = roiMat.Channels() == 4
            ? roiMat.CvtColor(ColorConversionCodes.BGRA2BGR)
            : roiMat.Clone();
        var (gray, colorIndex, judgeMs, grayMs, maxX) = ToGray(bgrMat);
        var colorName = Refs[colorIndex].Name;

        // 用 V>=180 像素的最大 x 估算字数
        var estLen = maxX <= 65 ? 2 : maxX <= 94 ? 3 : maxX <= 122 ? 4 : 5;

        // 按 x, x-1, x+1, x-2, x+2 ... 顺序生成待匹配的字数列表
        var lens = new List<int> { estLen };
        var left = estLen - 1;
        var right = estLen + 1;
        while (left >= 2 || right <= 5)
        {
            if (left >= 2)
            {
                lens.Add(left);
            }

            if (right <= 5)
            {
                lens.Add(right);
            }

            left--;
            right++;
        }

        var (sumI, sumI2) = BuildIntegral(gray, bgrMat.Width, bgrMat.Height);

        var nccSw = Stopwatch.StartNew();
        MojangTemplate? best = null;
        var bestScore = -1.0;
        var bestDx = 0;
        var bestDy = 0;
        var matchedCount = 0;
        foreach (var len in lens)
        {
            if (!_templatesByColorAndLen.TryGetValue((colorName, len), out var list) || list.Count == 0)
            {
                continue;
            }

            // 识别完当前字数类后，若该类最高分已达标则提前退出
            var lenBestScore = -1.0;
            foreach (var t in list)
            {
                var (score, dx, dy) = NccMax(gray, sumI, sumI2, bgrMat.Width, bgrMat.Height, t);
                matchedCount++;
                if (score > lenBestScore)
                {
                    lenBestScore = score;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = t;
                    bestDx = dx;
                    bestDy = dy;
                }
            }

            if (lenBestScore > MinScore)
            {
                break;
            }
        }
        nccSw.Stop();

        if (best is null || bestScore < MinScore)
        {
            if (TaskContext.Instance().Config.AutoPickConfig.TestModeEnabled
                || TaskContext.Instance().Config.AutoPickConfig.PickLogLevel >= 2)
            {
                _logger.LogInformation(
                    "莫版测试未识别到：颜色={Color} 估算字数={EstLen} 最高分={BestScore:F3} 判定={JudgeMs:F2}ms 灰度={GrayMs:F2}ms NCC={NccMs:F2}ms 模板={TemplateCount}",
                    colorName, estLen, best is null ? 0 : bestScore, judgeMs, grayMs, nccSw.Elapsed.TotalMilliseconds, matchedCount);
            }

            return null;
        }

        var templateRect = new Rect(rect.X + bestDx, rect.Y + bestDy, best.Width, best.Height);
        return new MojangMatchResult(best.Name, best.ItemName, bestScore, rect, templateRect,
            judgeMs, grayMs, nccSw.Elapsed.TotalMilliseconds, matchedCount, colorIndex);
    }

    /// <summary>
    /// 二次确认：仅对指定物品模板做复检，不重新判定颜色、不遍历全量模板。
    /// </summary>
    /// <param name="srcMat">重新截图的捕获区域源图（BGR）</param>
    /// <param name="fKeyRegion">重新检测到的 F 键区域</param>
    /// <param name="scale">分辨率缩放系数</param>
    /// <param name="name">第一次匹配到的模板名（交互名）</param>
    /// <param name="colorIndex">第一次判定的颜色索引</param>
    /// <returns>匹配度是否达标</returns>
    public bool ConfirmMatch(Mat srcMat, Region fKeyRegion, double scale, string name, int colorIndex)
    {
        var centerY = fKeyRegion.Y + fKeyRegion.Height / 2;
        var rect = new Rect(
            fKeyRegion.X + (int)(TextLeftOffset * scale),
            centerY - (int)(RegionHeight / 2.0 * scale),
            (int)(RegionWidth * scale),
            (int)(RegionHeight * scale));

        if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > srcMat.Width || rect.Y + rect.Height > srcMat.Height)
        {
            return false;
        }

        if (!_templatesByName.TryGetValue(name, out var template))
        {
            return false;
        }

        using var roiMat = new Mat(srcMat, rect);
        using var bgrMat = roiMat.Channels() == 4
            ? roiMat.CvtColor(ColorConversionCodes.BGRA2BGR)
            : roiMat.Clone();
        var gray = ToGrayByColor(bgrMat, colorIndex);
        var (sumI, sumI2) = BuildIntegral(gray, bgrMat.Width, bgrMat.Height);
        var (score, _, _) = NccMax(gray, sumI, sumI2, bgrMat.Width, bgrMat.Height, template);
        return score >= MinScore;
    }

    /// <summary>
    /// 使用指定颜色做灰度化（不重新判定颜色）。
    /// </summary>
    private static byte[] ToGrayByColor(Mat bgrMat, int colorIndex)
    {
        var w = bgrMat.Width;
        var h = bgrMat.Height;
        var n = w * h;

        using var mat3 = new Mat<Vec3b>(bgrMat);
        var indexer = mat3.GetIndexer();

        var gray = new byte[n];
        var refLab = RefLabs[colorIndex];
        for (var y = 0; y < h; y++)
        {
            var rowOffset = y * w;
            for (var x = 0; x < w; x++)
            {
                var px = indexer[y, x];
                var b = px.Item0;
                var g = px.Item1;
                var r = px.Item2;
                var v = Max3(b, g, r);
                if (v < VoteMinV)
                {
                    gray[rowOffset + x] = 0;
                    continue;
                }

                var lab = RgbToLab(r, g, b);
                var dE = LabDist(lab, refLab);
                gray[rowOffset + x] = (byte)(Math.Clamp(1.0 - dE / T, 0.0, 1.0) * 255.0);
            }
        }

        return gray;
    }

    /// <summary>
    /// 颜色判定 + 灰度化。与预处理脚本（autopick_preprocess.py）保持完全一致。
    /// </summary>
    private static (byte[] Gray, int ColorIndex, double JudgeMs, double GrayMs, int MaxX) ToGray(Mat bgrMat)
    {
        var w = bgrMat.Width;
        var h = bgrMat.Height;
        var n = w * h;

        using var mat3 = new Mat<Vec3b>(bgrMat);
        var indexer = mat3.GetIndexer();

        var b = new byte[n];
        var g = new byte[n];
        var r = new byte[n];

        // 1. 颜色判定：V>=180 中亮度前 30% 像素的平均色 -> 最近参考色
        var judgeSw = Stopwatch.StartNew();
        var brightV = new List<int>(n);
        var maxX = 0;
        for (var y = 0; y < h; y++)
        {
            var rowOffset = y * w;
            for (var x = 0; x < w; x++)
            {
                var px = indexer[y, x];
                var i = rowOffset + x;
                b[i] = px.Item0;
                g[i] = px.Item1;
                r[i] = px.Item2;
                var v = Max3(b[i], g[i], r[i]);
                if (v >= VoteMinV)
                {
                    brightV.Add(v);
                    if (x > maxX)
                    {
                        maxX = x;
                    }
                }
            }
        }

        byte[] gray = new byte[n];
        if (brightV.Count == 0)
        {
            judgeSw.Stop();
            return (gray, 0, judgeSw.Elapsed.TotalMilliseconds, 0, 0);
        }

        brightV.Sort();
        var idx = 0.7 * (brightV.Count - 1);
        var lower = (int)Math.Floor(idx);
        var upper = (int)Math.Ceiling(idx);
        var thr = lower == upper
            ? brightV[lower]
            : brightV[lower] + (idx - lower) * (brightV[upper] - brightV[lower]);

        double sumR = 0, sumG = 0, sumB = 0;
        var cnt = 0;
        for (var i = 0; i < n; i++)
        {
            var v = Max3(b[i], g[i], r[i]);
            if (v >= thr)
            {
                sumR += r[i];
                sumG += g[i];
                sumB += b[i];
                cnt++;
            }
        }

        var coreLab = RgbToLab(sumR / cnt, sumG / cnt, sumB / cnt);
        var colorIndex = 0;
        var minDist = double.MaxValue;
        for (var i = 0; i < RefLabs.Length; i++)
        {
            var d = LabDist(coreLab, RefLabs[i]);
            if (d < minDist)
            {
                minDist = d;
                colorIndex = i;
            }
        }
        judgeSw.Stop();

        // 2. 灰度化：向主颜色的匹配度
        var graySw = Stopwatch.StartNew();
        var refLab = RefLabs[colorIndex];
        for (var i = 0; i < n; i++)
        {
            var v = Max3(b[i], g[i], r[i]);
            if (v < VoteMinV)
            {
                gray[i] = 0;
                continue;
            }

            var lab = RgbToLab(r[i], g[i], b[i]);
            var dE = LabDist(lab, refLab);
            var match = Math.Clamp(1.0 - dE / T, 0.0, 1.0) * 255.0;
            gray[i] = (byte)match;
        }
        graySw.Stop();

        return (gray, colorIndex, judgeSw.Elapsed.TotalMilliseconds, graySw.Elapsed.TotalMilliseconds, maxX);
    }

    /// <summary>在截图灰度图上滑动模板（仅 y 方向，x 方向固定对齐），返回最大 NCC 及其位置。</summary>
    private static (double Score, int Dx, int Dy) NccMax(byte[] img, long[] sumI, long[] sumI2, int imgW, int imgH, MojangTemplate t)
    {
        var dxMax = imgW - t.Width + 1;
        var dyMax = imgH - t.Height + 1;
        if (dxMax <= 0 || dyMax <= 0)
        {
            return (0, 0, 0);
        }

        // x 方向固定对齐（截图与模板 x 相同），仅 y 方向上下滑动
        var best = -1.0;
        var bestDy = 0;
        for (var dy = 0; dy < dyMax; dy++)
        {
            var s = NccAt(img, sumI, sumI2, imgW, t, 0, dy);
            if (s > best)
            {
                best = s;
                bestDy = dy;
            }
        }

        return (best, 0, bestDy);
    }

    /// <summary>在 (dx, dy) 位置计算模板与截图窗口的归一化互相关（NCC）。窗口和/平方和由积分图 O(1) 查询。</summary>
    private static double NccAt(byte[] img, long[] sumI, long[] sumI2, int imgW, MojangTemplate t, int dx, int dy)
    {
        var n = t.Width * t.Height;
        var sI = (double)WindowSum(sumI, imgW, dx, dy, t.Width, t.Height);
        var sI2 = (double)WindowSum(sumI2, imgW, dx, dy, t.Width, t.Height);

        double sumIT = 0;
        foreach (var (y, x, tg) in t.NonZero)
        {
            sumIT += img[(dy + y) * imgW + dx + x] * tg;
        }

        var meanI = sI / n;
        var numerator = sumIT - n * meanI * t.MeanT;
        var denom = Math.Sqrt((sI2 - n * meanI * meanI) * t.VarT);
        if (denom < 1e-9)
        {
            return 0;
        }

        return numerator / denom;
    }

    /// <summary>构建灰度图的求和积分图与平方和积分图（尺寸 (h+1)x(w+1)）。</summary>
    private static (long[] SumI, long[] SumI2) BuildIntegral(byte[] img, int w, int h)
    {
        var stride = w + 1;
        var sumI = new long[(h + 1) * stride];
        var sumI2 = new long[(h + 1) * stride];
        for (var y = 0; y < h; y++)
        {
            var rowOffset = y * w;
            var cur = y * stride;
            var next = (y + 1) * stride;
            for (var x = 0; x < w; x++)
            {
                var v = img[rowOffset + x];
                var v2 = v * v;
                sumI[next + x + 1] = v + sumI[cur + x + 1] + sumI[next + x] - sumI[cur + x];
                sumI2[next + x + 1] = v2 + sumI2[cur + x + 1] + sumI2[next + x] - sumI2[cur + x];
            }
        }

        return (sumI, sumI2);
    }

    /// <summary>积分图窗口求和：区域 [x0, x0+w) x [y0, y0+h)。</summary>
    private static long WindowSum(long[] integral, int imgW, int x0, int y0, int w, int h)
    {
        var stride = imgW + 1;
        var a = integral[y0 * stride + x0];
        var b = integral[y0 * stride + x0 + w];
        var c = integral[(y0 + h) * stride + x0];
        var d = integral[(y0 + h) * stride + x0 + w];
        return d - b - c + a;
    }

    private static double[] RgbToLab(double r, double g, double b)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        var linR = R <= 0.04045 ? R / 12.92 : Math.Pow((R + 0.055) / 1.055, 2.4);
        var linG = G <= 0.04045 ? G / 12.92 : Math.Pow((G + 0.055) / 1.055, 2.4);
        var linB = B <= 0.04045 ? B / 12.92 : Math.Pow((B + 0.055) / 1.055, 2.4);

        var x = 0.4124564 * linR + 0.3575761 * linG + 0.1804375 * linB;
        var y = 0.2126729 * linR + 0.7151522 * linG + 0.0721750 * linB;
        var z = 0.0193339 * linR + 0.1191920 * linG + 0.9503041 * linB;

        const double xn = 0.95047, yn = 1.0, zn = 1.08883;
        var fx = LabF(x / xn);
        var fy = LabF(y / yn);
        var fz = LabF(z / zn);

        var L = 116.0 * fy - 16.0;
        var a = 500.0 * (fx - fy);
        var bb = 200.0 * (fy - fz);
        return [L, a, bb];
    }

    private static double LabF(double t)
    {
        const double d = 6.0 / 29.0;
        return t > d * d * d ? Math.Cbrt(t) : t / (3.0 * d * d) + 4.0 / 29.0;
    }

    private static double LabDist(double[] a, double[] b)
    {
        var dL = a[0] - b[0];
        var da = a[1] - b[1];
        var db = a[2] - b[2];
        return Math.Sqrt(dL * dL + da * da + db * db);
    }

    private static int Max3(byte a, byte b, byte c) => Math.Max(a, Math.Max(b, c));

    private static MojangMatch Load()
    {
        var assetsDir = Global.Absolute(@"GameTask\AutoPick\Assets");
        var binPath = Path.Combine(assetsDir, "莫版模板.bin");
        if (File.Exists(binPath))
        {
            return LoadFromBin(binPath);
        }

        return LoadFromJsonAndPng(Path.Combine(assetsDir, "莫版模板"));
    }

    private static MojangMatch LoadFromBin(string binPath)
    {
        var matcher = new MojangMatch();
        using var fs = File.OpenRead(binPath);
        using var br = new BinaryReader(fs);
        if (br.ReadInt32() != BinMagic || br.ReadInt32() != 1)
        {
            return matcher;
        }

        var count = br.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var name = ReadString(br);
            var color = ReadString(br);
            var itemName = ReadString(br);
            var len = br.ReadInt32();
            var width = br.ReadInt32();
            var height = br.ReadInt32();
            var gray = br.ReadBytes(width * height);
            var c = CropTemplate(gray, width, height);
            var template = new MojangTemplate
            {
                Name = name,
                Color = color,
                ItemName = itemName,
                Gray = c.Gray,
                Width = c.Width,
                Height = height,
                Len = len,
                MeanT = c.MeanT,
                VarT = c.VarT,
                NonZero = c.NonZero,
            };

            AddTemplate(matcher, template);
        }

        LogLoaded(matcher);
        return matcher;
    }

    private static MojangMatch LoadFromJsonAndPng(string baseDir)
    {
        var matcher = new MojangMatch();
        var jsonPath = Path.Combine(baseDir, "templates.json");
        if (!File.Exists(jsonPath))
        {
            return matcher;
        }

        var entries = JsonSerializer.Deserialize<List<TemplateEntry>>(File.ReadAllText(jsonPath)) ?? [];
        foreach (var e in entries)
        {
            var imgPath = Path.Combine(baseDir, e.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(imgPath))
            {
                continue;
            }

            using var mat = new Mat(imgPath, ImreadModes.Grayscale);
            byte[] gray = new byte[mat.Rows * mat.Cols];
            mat.GetArray<byte>(out gray);

            var c = CropTemplate(gray, mat.Cols, mat.Rows);
            var template = new MojangTemplate
            {
                Name = e.Name,
                Color = e.Color,
                ItemName = e.ItemName,
                Gray = c.Gray,
                Width = c.Width,
                Height = mat.Rows,
                Len = e.Len > 0 ? e.Len : Math.Min(e.Name.Length, 5),
                MeanT = c.MeanT,
                VarT = c.VarT,
                NonZero = c.NonZero,
            };

            AddTemplate(matcher, template);
        }

        LogLoaded(matcher);
        return matcher;
    }

    private static string ReadString(BinaryReader br)
    {
        var len = br.ReadInt32();
        return System.Text.Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    /// <summary>
    /// 按有效宽度裁剪模板并重算统计量。有效宽度 = 亮度&gt;0 像素的最大 x - 2（自动过滤最右边缘）。
    /// 截图与模板 x 方向对齐，右侧超出部分无需参与计算，直接丢弃。
    /// 同时构建非零像素列表，供 NCC 分子只遍历非零区域。
    /// </summary>
    private static CroppedTemplate CropTemplate(byte[] gray, int width, int height)
    {
        var maxX = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (gray[row + x] > 0 && x > maxX)
                {
                    maxX = x;
                }
            }
        }

        var effW = Math.Max(1, maxX - 2);
        var cropped = new byte[effW * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(gray, y * width, cropped, y * effW, effW);
        }

        double sumT = 0, sumT2 = 0;
        foreach (var v in cropped)
        {
            sumT += v;
            sumT2 += v * v;
        }

        var meanT = sumT / cropped.Length;
        return new CroppedTemplate(cropped, effW, meanT, sumT2 - cropped.Length * meanT * meanT, BuildNonZero(cropped, effW, height));
    }

    /// <summary>构建模板非零像素列表 (Y, X, 灰度)，跳过共同为 0 的区域。</summary>
    private static (int Y, int X, byte Gray)[] BuildNonZero(byte[] gray, int width, int height)
    {
        var list = new List<(int, int, byte)>(gray.Length / 2);
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var v = gray[row + x];
                if (v > 0)
                {
                    list.Add((y, x, v));
                }
            }
        }

        return list.ToArray();
    }

    private sealed record CroppedTemplate(byte[] Gray, int Width, double MeanT, double VarT, (int Y, int X, byte Gray)[] NonZero);

    private static void AddTemplate(MojangMatch matcher, MojangTemplate template)
    {
        var key = (template.Color, template.Len);
        if (!matcher._templatesByColorAndLen.TryGetValue(key, out var list))
        {
            list = [];
            matcher._templatesByColorAndLen[key] = list;
        }

        list.Add(template);

        if (!matcher._templatesByName.ContainsKey(template.Name))
        {
            matcher._templatesByName[template.Name] = template;
        }
    }

    private static void LogLoaded(MojangMatch matcher)
    {
        var total = matcher._templatesByColorAndLen.Values.Sum(v => v.Count);
        App.GetLogger<MojangMatch>().LogInformation("莫版匹配模板加载完成，共 {Total} 个模板", total);
    }

    private sealed class MojangTemplate
    {
        public required string Name { get; init; }
        public required string Color { get; init; }
        public required string ItemName { get; init; }
        public required byte[] Gray { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int Len { get; init; }
        public double MeanT { get; init; }
        public double VarT { get; init; }
        public required (int Y, int X, byte Gray)[] NonZero { get; init; }
    }

    private sealed class TemplateEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "";
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("itemName")] public string ItemName { get; set; } = "";
        [JsonPropertyName("len")] public int Len { get; set; }
    }
}
