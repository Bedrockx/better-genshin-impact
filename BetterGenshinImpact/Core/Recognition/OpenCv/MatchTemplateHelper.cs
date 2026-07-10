using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

/// <summary>
///     多目标模板匹配demo
///     https://github.com/shimat/opencvsharp/issues/182
/// </summary>
public class MatchTemplateHelper
{
    private static readonly ILogger<MatchTemplateHelper> _logger = App.GetLogger<MatchTemplateHelper>();

    /// <summary>
    ///  模板匹配
    /// </summary>
    /// <param name="srcMat">原图像</param>
    /// <param name="dstMat">模板</param>
    /// <param name="matchMode">匹配方式</param>
    /// <param name="maskMat">遮罩</param>
    /// <param name="threshold">阈值</param>
    /// <param name="retryCount">重试次数</param>
    /// <returns>左上角的标点,由于(0,0)点作为未匹配的结果，所以不能做完全相同的模板匹配</returns>
    public static Point MatchTemplate(Mat srcMat, Mat dstMat, TemplateMatchModes matchMode, Mat? maskMat = null, double threshold = 0.8, int retryCount = 2)
    {
        int attempt = 0;
        while (attempt < retryCount)
        {
            try
            {
                using var result = new Mat();
                Cv2.MatchTemplate(srcMat, dstMat, result, matchMode, maskMat!);

                if (matchMode is TemplateMatchModes.SqDiff or TemplateMatchModes.CCoeff or TemplateMatchModes.CCorr)
                {
                    Cv2.Normalize(result, result, 0, 1, NormTypes.MinMax);
                }

                Cv2.MinMaxLoc(result, out var minValue, out var maxValue, out var minLoc, out var maxLoc);

                if (matchMode is TemplateMatchModes.SqDiff or TemplateMatchModes.SqDiffNormed)
                {
                    if (minValue <= 1 - threshold)
                    {
                        return minLoc;
                    }
                }
                else
                {
                    if (maxValue >= threshold)
                    {
                        return maxLoc;
                    }
                }

                return default;
            }
            catch (OpenCvSharp.OpenCVException ex)
            {
                _logger.LogError($"OpenCV内存异常, 重试第{attempt + 1}次: {ex.Message}");
                CleanupMemory();
                attempt++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                _logger.LogDebug(ex, ex.Message);
                CleanupMemory();
                return default;
            }
        }

        // 如果达到最大重试次数仍然失败，记录最终失败信息
        _logger.LogError("MatchTemplate方法在最大重试次数后仍然失败。");
        CleanupMemory();
        return default;
    }
    
    /// <summary>
    /// 轻量内存清理（非阻塞、非压缩，避免在热路径上阻塞线程）
    /// </summary>
    public static void CleanupMemory()
    {
        try
        {
            GC.Collect();
            _logger.LogDebug("内存清理流程执行完成");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "内存清理流程执行失败");
        }
    }

    /// <summary>
    ///     模板匹配多个结果
    ///     不好用
    /// </summary>
    /// <param name="srcMat"></param>
    /// <param name="dstMat"></param>
    /// <param name="maskMat"></param>
    /// <param name="threshold"></param>
    /// <param name="maxCount"></param>
    /// <returns></returns>
    [Obsolete]
    public static List<Point> MatchTemplateMulti(Mat srcMat, Mat dstMat, Mat? maskMat = null, double threshold = 0.8, int maxCount = 8)
    {
        var points = new List<Point>();
        try
        {
            using var result = new Mat();
            Cv2.MatchTemplate(srcMat, dstMat, result, TemplateMatchModes.CCoeffNormed, maskMat!);

            var mask = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.White);
            var maskSub = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.Black);
            while (true)
            {
                Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLoc, mask);
                var maskRect = new Rect(maxLoc.X, maxLoc.Y, dstMat.Width, dstMat.Height);
                maskSub.Rectangle(maskRect, Scalar.White, -1);
                mask -= maskSub;
                if (maxValue >= threshold)
                    points.Add(maxLoc);
                else
                    break;
            }

            return points;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
            _logger.LogDebug(ex, ex.Message);
            return points;
        }
    }

    public static List<Point> MatchTemplateMulti(Mat srcMat, Mat dstMat, double threshold)
    {
        return MatchTemplateMulti(srcMat, dstMat, null, threshold);
    }

    /// <summary>
    ///     在一张图中查找多个模板
    ///     查到一个遮盖一个的笨方法，效率很低，但是很准确
    /// </summary>
    /// <param name="srcMat"></param>
    /// <param name="imgSubDictionary"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static Dictionary<string, List<Point>> MatchMultiPicForOnePic(Mat srcMat, Dictionary<string, Mat> imgSubDictionary, double threshold = 0.8)
    {
        // Clone srcMat so the caller's original Mat is never mutated.
        using var workingMat = srcMat.Clone();
        var dictionary = new Dictionary<string, List<Point>>();
        foreach (var kvp in imgSubDictionary)
        {
            var list = new List<Point>();

            while (true)
            {
                var point = MatchTemplate(workingMat, kvp.Value, TemplateMatchModes.CCoeffNormed, null, threshold);
                if (point != new Point())
                {
                    // 把结果给遮掩掉，避免重复识别
                    Cv2.Rectangle(workingMat, point, new Point(point.X + kvp.Value.Width, point.Y + kvp.Value.Height), Scalar.Black, -1);
                    list.Add(point);
                }
                else
                {
                    break;
                }
            }

            dictionary.Add(kvp.Key, list);
        }

        return dictionary;
    }

    /// <summary>
    ///     在一张图中查找多个模板
    ///     查到一个遮盖一个的笨方法，效率很低，但是很准确
    /// </summary>
    /// <param name="srcMat"></param>
    /// <param name="imgSubList"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static List<Rect> MatchMultiPicForOnePic(Mat srcMat, List<Mat> imgSubList, double threshold = 0.8)
    {
        // Clone srcMat so the caller's original Mat is never mutated.
        using var workingMat = srcMat.Clone();
        List<Rect> list = [];
        foreach (var sub in imgSubList)
            while (true)
            {
                var point = MatchTemplate(workingMat, sub, TemplateMatchModes.CCoeffNormed, null, threshold);
                if (point != new Point())
                {
                    // 把结果给遮掩掉，避免重复识别
                    Cv2.Rectangle(workingMat, point, new Point(point.X + sub.Width, point.Y + sub.Height), Scalar.Black, -1);
                    list.Add(new Rect(point.X, point.Y, sub.Width, sub.Height));
                }
                else
                {
                    break;
                }
            }

        return list;
    }

    /// <summary>
    ///     在一张图中查找一个个模板
    /// </summary>
    /// <param name="srcMat"></param>
    /// <param name="dstMat"></param>
    /// <param name="maskMat"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static List<Rect> MatchOnePicForOnePic(Mat srcMat, Mat dstMat, Mat? maskMat = null, double threshold = 0.8)
    {
        // Clone srcMat so the caller's original Mat is never mutated.
        using var workingMat = srcMat.Clone();
        List<Rect> list = [];

        while (true)
        {
            var point = MatchTemplate(workingMat, dstMat, TemplateMatchModes.CCoeffNormed, maskMat, threshold);
            if (point != new Point())
            {
                // 把结果给遮掩掉，避免重复识别
                Cv2.Rectangle(workingMat, point, new Point(point.X + dstMat.Width, point.Y + dstMat.Height), Scalar.Black, -1);
                list.Add(new Rect(point.X, point.Y, dstMat.Width, dstMat.Height));
            }
            else
            {
                break;
            }
        }

        return list;
    }

    /// <summary>
    ///    在一张图中查找一个个模板
    /// </summary>
    /// <param name="srcMat"></param>
    /// <param name="dstMat"></param>
    /// <param name="matchMode"></param>
    /// <param name="maskMat"></param>
    /// <param name="threshold"></param>
    /// <param name="maxCount"></param>
    /// <returns></returns>
    public static List<Rect> MatchOnePicForOnePic(Mat srcMat, Mat dstMat, TemplateMatchModes matchMode, Mat? maskMat, double threshold, int maxCount = -1)
    {
        // Clone srcMat so the caller's original Mat is never mutated.
        using var workingMat = srcMat.Clone();
        List<Rect> list = [];

        if (maxCount < 0)
        {
            maxCount = srcMat.Width * srcMat.Height / dstMat.Width / dstMat.Height;
        }

        for (int i = 0; i < maxCount; i++)
        {
            var point = MatchTemplate(workingMat, dstMat, matchMode, maskMat, threshold);
            if (point != new Point())
            {
                // 把结果给遮掩掉，避免重复识别
                Cv2.Rectangle(workingMat, point, new Point(point.X + dstMat.Width, point.Y + dstMat.Height), Scalar.Black, -1);
                list.Add(new Rect(point.X, point.Y, dstMat.Width, dstMat.Height));
            }
            else
            {
                break;
            }
        }

        return list;
    }
}
