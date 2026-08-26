using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using AutoFightParam = BetterGenshinImpact.GameTask.AutoFight.AutoFightParam;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗中视觉识别配置（从 AutoFightParam 或全局配置集中获取）
/// </summary>
public sealed record VisualRecognitionConfig(
    int TargetingDetectionInterval = 50,
    bool DrawRecognitionResults = true,
    double LockLostWaitTime = 0.5,
    DamageNumberRecognitionMode DamageNumberRecognitionMode = DamageNumberRecognitionMode.Color);

/// <summary>
/// 战斗识别相关的通用工具函数
/// </summary>
public static class AvatarRecognition
{
    /// <summary>
    /// 当前战斗的 AutoFightParam（由 AutoFightTask/AutoFightJsonTask 在 Start 开头设置），
    /// 用于让 <see cref="GetVisualRecognitionConfig"/> 优先读取逐队伍配置而非全局配置。
    /// AsyncLocal 会沿 async 调用链自动传递，包括 <see cref="Task.Run"/> 创建的后台任务。
    /// </summary>
    private static readonly AsyncLocal<AutoFightParam?> _currentAutoFightParam = new();

    /// <summary>
    /// 索敌叠加层目标框共享画笔（避免每帧新建 Pen 导致 GDI+ 句柄抖动）
    /// </summary>
    private static readonly System.Drawing.Pen _targetPen = new(System.Drawing.Color.LimeGreen, 2);

    /// <summary>
    /// 设置当前战斗参数，后续的视觉配置读取将优先使用此参数中的值而非全局配置。
    /// 应在 Start 开头调用，并在 Start 的 finally 中调用 <see cref="ClearCurrentAutoFightParam"/> 清理。
    /// </summary>
    public static void SetCurrentAutoFightParam(AutoFightParam? param) => _currentAutoFightParam.Value = param;

    /// <summary>
    /// 清除当前战斗参数，后续视觉配置回退到全局配置。
    /// </summary>
    public static void ClearCurrentAutoFightParam() => _currentAutoFightParam.Value = null;

    /// <summary>
    /// 清除传奇血条追踪状态。每次新战斗开始时应调用，避免上一场战斗
    /// 已累积的阈值在新战斗的普通血条上被误判为传奇血条。
    /// </summary>
    public static void ClearLegendaryBarTracker()
    {
        lock (_legendaryBarLock)
        {
            _legendaryBarTracker.Clear();
        }
    }

    /// <summary>
    /// 排他锁：保护持续索敌的"检查+MoveMouseBy"与独占操作的 BeginExclusiveOperation/Dispose 互斥，
    /// 解决 volatile bool 的 check-then-act 竞态。
    /// </summary>
    private static readonly object _seekLock = new();

    /// <summary>
    /// 持续索敌跳过引用计数：&gt;0 表示至少有一个独占视角操作正在进行，
    /// 持续索敌循环应跳过本帧。使用引用计数支持嵌套独占操作。
    /// </summary>
    private static int _skipSeekCount;

    /// <summary>
    /// 开始独占视角操作（引用计数 +1，含锁保证互斥）。
    /// 返回的 <see cref="SkipSeekScope"/> 在 Dispose 时自动递减计数。
    /// 使用方应通过 using 语句确保异常安全。
    /// </summary>
    internal static SkipSeekScope BeginExclusiveOperation()
    {
        lock (_seekLock)
        {
            _skipSeekCount++;
        }
        return new SkipSeekScope();
    }

    /// <summary>
    /// 独占操作作用域。Dispose 时自动递减排他计数（锁内递减保证互斥）。
    /// </summary>
    internal readonly struct SkipSeekScope : IDisposable
    {
        public void Dispose()
        {
            lock (_seekLock)
            {
                _skipSeekCount--;
            }
        }
    }

    /// <summary>
    /// 资源缩放比例
    /// </summary>
    private static double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    /// <summary>
    /// 传奇血条动态追踪字典：(xBin, yBin) → 连续出现计数。
    /// x 以 5px 粒度分箱（传奇血条左侧坐标相对稳定），y 以 2px 粒度分箱。
    /// 按 y 分层使用不同阈值判定传奇：
    ///   y &lt; 100 → 连续 2 帧判传奇
    ///   y 100-200 → 连续 4 帧判传奇
    ///   y ≥ 200 → 连续 10 帧判传奇
    /// </summary>
    private static readonly Dictionary<(int xBin, int yBin), int> _legendaryBarTracker = new();
    private static readonly object _legendaryBarLock = new();
    private const int LegendaryBarMaxCount = 10;

    /// <summary>
    /// 更新传奇血条动态追踪状态。
    /// 对全部血条的 (x, y) 进行帧间连续性追踪，连续出现达到对应阈值后标记为传奇。
    /// 允许1帧容错：某帧未出现时计数递减而非直接清零。
    /// </summary>
    private static void UpdateLegendaryBarTracker(IEnumerable<(int x, int y)> bars)
    {
        lock (_legendaryBarLock)
        {
            var currentBins = bars.Select(b => (xBin: b.x / 5 * 5, yBin: b.y / 2 * 2))
                                  .ToHashSet();

            // 存在：递增（上限为最大阈值）
            foreach (var bin in currentBins)
            {
                if (_legendaryBarTracker.TryGetValue(bin, out var cnt))
                    _legendaryBarTracker[bin] = Math.Min(cnt + 1, LegendaryBarMaxCount);
                else
                    _legendaryBarTracker[bin] = 1;
            }

            // 不存在：递减（1帧容错），归零则移除
            foreach (var bin in _legendaryBarTracker.Keys.ToArray())
            {
                if (!currentBins.Contains(bin))
                {
                    _legendaryBarTracker[bin]--;
                    if (_legendaryBarTracker[bin] <= 0)
                        _legendaryBarTracker.Remove(bin);
                }
            }
        }
    }

    /// <summary>
    /// 判断指定 (x, y) 坐标的血条是否为传奇血条。
    /// y &lt; 100 连续 2 帧判传奇；y 100-200 连续 4 帧判传奇；y ≥ 200 连续 10 帧判传奇。
    /// </summary>
    public static bool IsLegendaryBar(int x, int y)
    {
        lock (_legendaryBarLock)
        {
            if (!_legendaryBarTracker.TryGetValue((x / 5 * 5, y / 2 * 2), out var cnt))
                return false;

            int threshold = y < (int)(100 * AssetScale) ? 2
                          : y < (int)(200 * AssetScale) ? 4
                          : 10;
            return cnt >= threshold;
        }
    }

    /// <summary>
    /// 检测屏幕中的红色血条（连通域分析）
    /// </summary>
    public static List<(int x, int y, int width, int height)> FindBloodBars(ImageRegion? existingCapture = null)
    {
        var results = new List<(int x, int y, int width, int height)>();

        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var image = existingCapture ?? selfCapture!;
            var bloodLower = new Scalar(255, 90, 90); // BGR 红色

            using var cropped = image.DeriveCrop(0, 0, (int)(1500 * AssetScale), (int)(900 * AssetScale));
            using Mat mask = OpenCvCommonHelper.Threshold(
                cropped.SrcMat, bloodLower);

            using Mat labels = new Mat();
            using Mat stats = new Mat();
            using Mat centroids = new Mat();

            int numLabels = Cv2.ConnectedComponentsWithStats(
                mask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            for (int i = 1; i < numLabels; i++)
            {
                using Mat row = stats.Row(i);
                if (row.GetArray(out int[] arr))
                {
                    int x = arr[0], y = arr[1], width = arr[2], height = arr[3];
                    results.Add((x, y, width, height));
                }
            }

            // 自动更新传奇血条动态追踪（排除左侧 UI 区域 x<=200，避免队伍头像等红色元素被误计为传奇血条）
            UpdateLegendaryBarTracker(results.Where(r => r.x > (int)(200 * AssetScale)).Select(r => (r.x, r.y)));

            return results;
        }
    }

    /// <summary>
    /// 获取视觉识别相关配置项。
    /// 调用方通过此方法获取配置，而非直接读取全局 config，确保配置访问集中管理。
    /// </summary>
    public static VisualRecognitionConfig GetVisualRecognitionConfig()
    {
        var param = _currentAutoFightParam.Value;
        if (param != null)
        {
            return new VisualRecognitionConfig(
                Math.Clamp(param.TargetingDetectionInterval, 1, 200),
                param.DrawRecognitionResults,
                param.LockLostWaitTime,
                param.DamageNumberRecognitionMode);
        }

        var config = TaskContext.Instance().Config.AutoFightConfig;
        return new VisualRecognitionConfig(
            Math.Clamp(config.TargetingDetectionInterval, 1, 200),
            config.DrawRecognitionResults,
            config.LockLostWaitTime,
            config.DamageNumberRecognitionMode);
    }

    /// <summary>
    /// 根据配置的伤害数字识别模式寻找伤害数字/反应文字。
    ///   - Disabled：直接返回 null
    ///   - Ocr：使用 OCR 识别
    ///   - Color：使用颜色分析识别
    /// 配置来源：<see cref="GetVisualRecognitionConfig"/>
    /// </summary>
    public static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumber(ImageRegion? existingCapture = null)
    {
        var mode = GetVisualRecognitionConfig().DamageNumberRecognitionMode;
        switch (mode)
        {
            case DamageNumberRecognitionMode.Disabled:
                return null;
            case DamageNumberRecognitionMode.Color:
                return FindDamageNumberByColor(existingCapture);
            case DamageNumberRecognitionMode.Ocr:
            default:
                return FindDamageNumberByOcr(existingCapture);
        }
    }

    /// <summary>
    /// OCR 寻找伤害数字/反应文字作为追踪目标（备用寻敌）。
    /// 在 450,240-1600,900 区域 OCR，过滤条件：
    ///   - 有效项1：排除首位 '+'，去除非数字后纯数字 ≥4 位
    ///   - 有效项2：文本包含反应关键词（免疫/蒸发/感电/结晶/扩散/绽放/冻结/超载/融化/燃烧/超导/激化），跳过数字过滤
    /// 按 h²×文本字数 加权得到中心坐标，返回离加权中心最近的有效项。
    /// </summary>
    private static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumberByOcr(ImageRegion? existingCapture = null)
    {
        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var ra = existingCapture ?? selfCapture!;
            var ocrResults = ra.FindMulti(RecognitionObject.Ocr((int)(450 * AssetScale), (int)(240 * AssetScale), (int)(1150 * AssetScale), (int)(660 * AssetScale)));

            string[] reactionKeywords = ["免疫", "蒸发", "感电", "结晶", "扩散", "绽放", "冻结", "超载", "融化", "燃烧", "超导", "激化"];
            var validItems = new List<(int cx, int cy, int area, string text, int x, int y, int w, int h)>();

            foreach (var r in ocrResults)
            {
                var text = r.Text?.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                // 有效项2：反应关键词（跳过所有过滤）
                if (reactionKeywords.Any(k => text.Contains(k)))
                {
                    validItems.Add((r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * r.Height * text.Length, text, r.X, r.Y, r.Width, r.Height));
                    continue;
                }

                // 有效项1：排除 '+' 开头
                if (text[0] == '+') continue;

                // 去除非数字，纯数字 ≥4 位
                var digits = new string(text.Where(char.IsDigit).ToArray());
                if (digits.Length >= 4)
                {
                    validItems.Add((r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * r.Height * text.Length, text, r.X, r.Y, r.Width, r.Height));
                }
            }

            if (validItems.Count == 0) return null;

            int totalArea = validItems.Sum(i => i.area);
            if (totalArea == 0) return null;

            double avgX = (double)validItems.Sum(i => i.cx * i.area) / totalArea;
            double avgY = (double)validItems.Sum(i => i.cy * i.area) / totalArea;

            var closest = validItems.OrderBy(i => Math.Abs(i.cx - avgX) + Math.Abs(i.cy - avgY)).First();

            return (closest.cx, closest.cy, closest.text, closest.x, closest.y, closest.w, closest.h);
        }
    }

    /// <summary>
    /// 颜色分析模式：在 450,240-1600,900 区域内查找固定颜色的像素，
    /// 经连通域分析后舍弃高度小于20的区域，返回加权中心。
    /// </summary>
    private static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumberByColor(ImageRegion? existingCapture = null)
    {
        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var ra = existingCapture ?? selfCapture!;

            // 目标颜色 (RGB)
            Scalar[] targetColors =
            [
                new(225, 155, 255), // 雷 #E19BFF
                new(153, 255, 255), // 冰 #99FFFF
                new(51, 204, 255),  // 水 #33CCFF
                new(102, 255, 204), // 风 #66FFCC
                new(255, 155, 0),   // 火 #FF9B00
                new(0, 234, 82),    // 草 #00EA52
                new(255, 204, 102), // 岩 #FFCC66
            ];

            int roiX = (int)(450 * AssetScale);
            int roiY = (int)(240 * AssetScale);
            int roiW = (int)(1150 * AssetScale);
            int roiH = (int)(660 * AssetScale);

            using var cropped = ra.DeriveCrop(roiX, roiY, roiW, roiH);
            using var rgbMat = new Mat();
            Cv2.CvtColor(cropped.SrcMat, rgbMat, ColorConversionCodes.BGR2RGB);

            using var combinedMask = new Mat(cropped.SrcMat.Size(), MatType.CV_8UC1, Scalar.All(0));

            foreach (var color in targetColors)
            {
                using var mask = new Mat();
                Cv2.InRange(rgbMat, color, color, mask);
                Cv2.BitwiseOr(combinedMask, mask, combinedMask);
            }

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var numLabels = Cv2.ConnectedComponentsWithStats(combinedMask, labels, stats, centroids,
                connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

            if (numLabels <= 1) return null;

            var validItems = new List<(int cx, int cy, int area, int x, int y, int w, int h)>();
            for (int i = 1; i < numLabels; i++)
            {
                int x = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int y = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

                if (height < (int)(20 * AssetScale)) continue;

                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                validItems.Add((x + width / 2 + roiX, y + height / 2 + roiY, area, x + roiX, y + roiY, width, height));
            }

            if (validItems.Count == 0) return null;

            int totalArea = validItems.Sum(i => i.area);
            if (totalArea == 0) return null;

            double avgX = (double)validItems.Sum(i => i.cx * i.area) / totalArea;
            double avgY = (double)validItems.Sum(i => i.cy * i.area) / totalArea;

            var closest = validItems.OrderBy(i => Math.Abs(i.cx - avgX) + Math.Abs(i.cy - avgY)).First();

            return (closest.cx, closest.cy, "", closest.x, closest.y, closest.w, closest.h);
        }
    }

    /// <summary>
    /// 战斗中持续索敌循环：在战斗过程中持续尝试面朝敌人。
    /// 受 EnableCombatTargeting 配置项控制总开关。
    /// 当 SkipSeek 为 true 时（如部分角色重击索敌期间）跳过本帧。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="isFightEnd">战斗是否已结束（外部标志，为 true 时退出循环）</param>
    public static async Task ContinuousTargetingLoopAsync(
        CancellationToken ct,
        Func<bool>? isFightEnd = null)
    {
        var dpi = TaskContext.Instance().DpiScale;
        var visConfig = GetVisualRecognitionConfig();
        var frameIntervalMs = visConfig.TargetingDetectionInterval;
        var drawResults = visConfig.DrawRecognitionResults;
        var lockLostWaitTime = visConfig.LockLostWaitTime;
        DateTime? lastSeenTargetTime = null;  // 最后找到目标的时间（null = 从未找到）

        try
        {
            while (!ct.IsCancellationRequested && !(isFightEnd?.Invoke() ?? false))
            {
                // 快速路径：排他计数 > 0 时跳过本轮，避免不必要的截图开销
                if (Volatile.Read(ref _skipSeekCount) > 0)
                {
                    await Task.Delay(frameIntervalMs, ct);
                    continue;
                }

                using (var capture = CaptureToRectArea())
                {
                    int preAimX = (int)(capture.Width * 0.5);
                    int preAimY = (int)(capture.Height * (480.0 / 1080.0));

                    // 不在主界面时跳过本轮（避免菜单/地图/对话等界面下误操作）
                    if (!Bv.IsInMainUi(capture))
                    {
                        await Task.Delay(frameIntervalMs, ct);
                        continue;
                    }

                    // 1. 血条识别：检测红色血条并过滤左侧 UI 区域 (x > 200)
                    var bars = FindBloodBars(capture);
                    var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();

                    var drawList = new List<RectDrawable>();

                    bool hasLegendaryBar = valid.Any(b => IsLegendaryBar(b.x, b.y));

                    // 2. 血条追踪：存在有效普通血条且无传奇时，朝最近血条方向移动鼠标
                    if (valid.Count > 0 && !hasLegendaryBar)
                    {
                        lastSeenTargetTime = DateTime.UtcNow;
                        var nearest = valid.OrderBy(b =>
                            Math.Abs((b.x + b.width / 2) - preAimX) +
                            Math.Abs((b.y + b.height / 2) - preAimY)).First();
                        var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                        var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                        lock (_seekLock)
                        {
                            if (_skipSeekCount > 0) continue;
                            Simulation.SendInput.Mouse.MoveMouseBy(
                                (int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));
                        }

                        // 叠加层：最近血条绿色粗框，其余红色细框
                        if (drawResults)
                        {
                            foreach (var b in valid)
                            {
                                var rect = new OpenCvSharp.Rect(b.x, b.y, b.width, b.height);
                                bool isTarget = b.x == nearest.x && b.y == nearest.y &&
                                                b.width == nearest.width && b.height == nearest.height;
                                drawList.Add(capture.ToRectDrawable(rect,
                                    isTarget ? "target" : "blood",
                                    isTarget
                                        ? _targetPen
                                        : null));
                            }
                        }
                    }
                    else
                    {
                        // 3. 伤害数字追踪：血条无效时尝试通过伤害数字/反应文字定位
                        var damageResult = FindDamageNumber(capture);
                        if (damageResult.HasValue)
                        {
                            var (dcx, dcy, _, dx, dy, dw, dh) = damageResult.Value;
                            lastSeenTargetTime = DateTime.UtcNow;
                            var offsetX = dcx - preAimX;
                            var offsetY = dcy - preAimY;
                            lock (_seekLock)
                            {
                                if (_skipSeekCount > 0) continue;
                                Simulation.SendInput.Mouse.MoveMouseBy(
                                    (int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));
                            }

                            // 叠加层：伤害数字区域绿色框
                            if (drawResults)
                            {
                                drawList.Add(capture.ToRectDrawable(
                                    new OpenCvSharp.Rect(dx, dy, dw, dh),
                                    "damage_target",
                                    _targetPen));
                            }
                        }

                        // 4. 脱锁旋转：血条和伤害数字都找不到时，脱锁等待后旋转视角
                        if (!damageResult.HasValue)
                        {
                            // 从未找到过目标，或距离上次找到已超过脱锁等待时间 → 开始旋转
                            if (!lastSeenTargetTime.HasValue ||
                                (DateTime.UtcNow - lastSeenTargetTime.Value).TotalSeconds >= lockLostWaitTime)
                            {
                                lock (_seekLock)
                                {
                                    if (_skipSeekCount > 0) continue;
                                    Simulation.SendInput.Mouse.MoveMouseBy((int)(250 * dpi), 0);
                                }
                            }
                        }
                    }

                    // 提交叠加层
                    VisionContext.Instance().DrawContent.PutOrRemoveRectList("ContinuousTargeting", drawList);
                }

                // 按配置的索敌识别间隔等待
                await Task.Delay(frameIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // 退出时释放所有按键、点按中键回正视角、清除叠加层
            // 注意：清理阶段使用 CancellationToken.None，因为 ct 可能在到此之前已被取消，
            // 若使用已取消的 token 会导致 Task.Delay 抛出异常，跳过中键复位和叠加层清理。
            Simulation.ReleaseAllKey();
            await Task.Delay(50, CancellationToken.None);
            Simulation.SendInput.Mouse.MiddleButtonClick();
            VisionContext.Instance().DrawContent.RemoveRect("ContinuousTargeting");
        }
    }

    // ============================================================
    // 红箭头索敌识别（FindRedArrowAngles 系列），本块为独立追加的自足代码。
    //
    // 合并冲突处理说明：
    // 本块由分支 feat-red-arrow-enemy-locating 追加到文件末尾（类结束 `}` 之前）。
    // 分支 feat-chasca-and-arlecchino-specialization 也在同一位置追加了相同的识别块
    // （272 行逐字节一致），因此把该分支合并进来时，Git 会在本位置报一个“空 vs 空”
    // 的插入位置冲突（并非逻辑冲突）。解决方式：
    //   1. 出现 `<<<<<<< HEAD` / `=======` / `>>>>>>>` 标记时，删除这三行冲突标记；
    //   2. 保留其中一份识别块（两边内容相同），删除另一半重复内容，避免重复定义的编译错误；
    //   3. 本块末尾的 `AngleDiff` 结束 `}` 与类结束 `}` 之间不要遗漏内容。
    // 若对方分支后续在文件中间新增了恰斯卡参数(excludeRects/Chasca*/Arlecchino*)，
    // 那部分与识别块无关，按常规冲突逐个解决即可。
    // ============================================================
    /// <summary>
    /// 识别屏幕中所有红色箭头，返回每个箭头到屏幕中心连线的角度（度）。
    /// 识别到几个箭头返回几个角度，未识别到返回空列表。
    /// 角度约定：右方为 0°，逆时针为正，范围 (-180, 180]。
    /// 算法流水线（1080p 基准，自动按截图宽高缩放）：
    ///   1. 颜色分类：血条 (255,90,90)±5 显式排除；箭头色带 r>240、G∈[40,115]、B∈[70,138]、B-G≥-15、r-g/r-b≥110
    ///      （半透明混合后随背景色变化，G 上限 115/B 上限 138 覆盖暗背景与偏蓝背景）
    ///   2. 环带过滤：像素距环心距离与傅里叶4阶拟合环 r(φ)（42 个确认箭头，avg 残差 0.6px）之差 ≤50px
    ///   3. 3×3 闭运算合并箭头的两部分（内尖菱形 + 外包箭头）
    ///   4. 连通域（面积 ≥10）
    ///   5. 内尖/外尖判定：MinD∈[Ring-18,Ring-4] / MaxD∈[Ring+6,Ring+20] 且腰宽 MaxW≥18（排除固定方块/横条等非箭头）
    ///   6. 箭头组合：内尖外尖同域（both）、角度差≤6° 的 innerOnly+outerOnly 配对、
    ///      血条遮挡分支（对应环带内血条像素 ≥5 时仅一侧尖也可判为箭头）
    /// </summary>
    /// <param name="existingCapture">已有截图（避免二次截图）</param>
    /// <param name="arrowRects">可选：非 null 时填充每个箭头的外接矩形（供绘制）</param>
    public static List<double> FindRedArrowAngles(ImageRegion? existingCapture = null, List<Rect>? arrowRects = null)
    {
        var selfCapture = existingCapture == null ? CaptureToRectArea() : null;
        using (selfCapture)
        {
            var image = existingCapture ?? selfCapture!;
            return AnalyzeRedArrows(image.SrcMat, arrowRects);
        }
    }

    /// <summary>
    /// 红色箭头连通域（识别中间结构）
    /// </summary>
    private sealed class RedArrowComp
    {
        public List<int> Pix = new();
        public double MinD = double.MaxValue, MaxD = 0;
        public double Phi, Ring;
        public double MaxW;
        public bool HasInner, HasOuter;
        public int MinX = int.MaxValue, MaxX = -1, MinY = int.MaxValue, MaxY = -1;
    }

    /// <summary>
    /// 红色箭头核心识别算法（见 <see cref="FindRedArrowAngles"/> 说明）。
    /// </summary>
    private static List<double> AnalyzeRedArrows(Mat src, List<Rect>? arrowRects)
    {
        int w = src.Width, h = src.Height;
        double scale = w / 1920.0; // 1080p 基准，自动适配非 1080p 截图
        double cx = w * 0.5, cy = h * 0.5;

        // 傅里叶4阶环拟合系数（1080p 基准，环心相对屏幕中心 (960,540) 偏移约 26px）
        // r(φ) = a0 + Σ(a_k·cos(kφ) + b_k·sin(kφ))
        static double RingR(double phi, double sc)
        {
            double v = 462.60;
            v += -0.72 * Math.Cos(phi) + -26.55 * Math.Sin(phi);
            v += 41.49 * Math.Cos(2 * phi) + 0.10 * Math.Sin(2 * phi);
            v += 0.17 * Math.Cos(3 * phi) + -2.63 * Math.Sin(3 * phi);
            v += 4.09 * Math.Cos(4 * phi) + -0.27 * Math.Sin(4 * phi);
            return v * sc;
        }

        // 1. 颜色分类 + 环带过滤
        var bytes = new byte[src.Total() * src.ElemSize()];
        Marshal.Copy(src.Data, bytes, 0, bytes.Length);
        int step = (int)src.Step();
        var cls = new byte[w * h]; // 0=无, 1=箭头候选, 2=血条
        var hpPts = new List<int>();
        for (int y = 0; y < h; y++)
        {
            int row = y * step;
            for (int x = 0; x < w; x++)
            {
                int i = row + x * 3;
                byte b = bytes[i], g = bytes[i + 1], r = bytes[i + 2];
                if (Math.Abs(r - 255) <= 5 && Math.Abs(g - 90) <= 5 && Math.Abs(b - 90) <= 5)
                {
                    cls[y * w + x] = 2;
                    hpPts.Add(y * w + x);
                }
                else if (r > 240 && g >= 40 && g <= 115 && b >= 70 && b <= 138
                    && (b - g) >= -15 && (r - g) >= 110 && (r - b) >= 110)
                {
                    double dx = x - cx, dy = y - cy;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    double phi = Math.Atan2(dy, dx);
                    if (Math.Abs(d - RingR(phi, scale)) <= 50 * scale) cls[y * w + x] = 1;
                }
            }
        }

        // 2. 3×3 闭运算：合并箭头两部分（内尖菱形 + 外包箭头）为一个连通域
        using (var maskMat = new Mat(h, w, MatType.CV_8UC1))
        using (var closed = new Mat())
        {
            Marshal.Copy(cls, 0, maskMat.Data, cls.Length);
            Cv2.MorphologyEx(maskMat, closed, MorphTypes.Close,
                Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
            var closedBytes = new byte[w * h];
            Marshal.Copy(closed.Data, closedBytes, 0, w * h);
            cls = closedBytes;
        }

        // 3. 连通域（8 邻域，面积 ≥10）
        var visited = new bool[w * h];
        var stack = new Stack<int>();
        var comps = new List<RedArrowComp>();
        for (int idx = 0; idx < w * h; idx++)
        {
            if (cls[idx] != 1 || visited[idx]) continue;
            var c = new RedArrowComp();
            stack.Push(idx);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                if (visited[p] || cls[p] != 1) continue;
                visited[p] = true;
                c.Pix.Add(p);
                int px = p % w, py = p / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && !visited[ny * w + nx])
                            stack.Push(ny * w + nx);
                    }
            }
            if (c.Pix.Count >= 10) comps.Add(c);
        }

        // 4. 每域统计：内外端点、质心角、腰宽
        foreach (var c in comps)
        {
            double sx = 0, sy = 0;
            var ds = new List<double>(c.Pix.Count);
            var phis = new List<double>(c.Pix.Count);
            foreach (var p in c.Pix)
            {
                int px = p % w, py = p / w;
                double dx = px - cx, dy = py - cy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                ds.Add(d);
                phis.Add(Math.Atan2(dy, dx));
                if (d < c.MinD) c.MinD = d;
                if (d > c.MaxD) c.MaxD = d;
                sx += px;
                sy += py;
                if (px < c.MinX) c.MinX = px;
                if (px > c.MaxX) c.MaxX = px;
                if (py < c.MinY) c.MinY = py;
                if (py > c.MaxY) c.MaxY = py;
            }
            c.Phi = Math.Atan2(sy / c.Pix.Count - cy, sx / c.Pix.Count - cx);
            c.Ring = RingR(c.Phi, scale);

            // 腰宽 MaxW：按径向距离 2px 分箱，箱内角度跨度 × 该箱半径 的最大值
            var bMin = new Dictionary<int, double>();
            var bMax = new Dictionary<int, double>();
            var bCent = new Dictionary<int, double>();
            for (int i = 0; i < ds.Count; i++)
            {
                double dphi = phis[i] - c.Phi;
                while (dphi > Math.PI) dphi -= 2 * Math.PI;
                while (dphi < -Math.PI) dphi += 2 * Math.PI;
                int bin = (int)(ds[i] / 2);
                if (!bMin.TryGetValue(bin, out _))
                {
                    bMin[bin] = dphi;
                    bMax[bin] = dphi;
                    bCent[bin] = ds[i];
                }
                else
                {
                    if (dphi < bMin[bin]) bMin[bin] = dphi;
                    if (dphi > bMax[bin]) bMax[bin] = dphi;
                }
            }
            foreach (var kv in bMin)
            {
                double bw = (bMax[kv.Key] - bMin[kv.Key]) * bCent[kv.Key];
                if (bw > c.MaxW) c.MaxW = bw;
            }

            c.HasInner = c.MinD >= c.Ring - 18 * scale && c.MinD <= c.Ring - 4 * scale && c.MaxW >= 18 * scale;
            c.HasOuter = c.MaxD >= c.Ring + 6 * scale && c.MaxD <= c.Ring + 20 * scale && c.MaxW >= 18 * scale;
        }

        // 5. 箭头组合：both / 配对 / 血条遮挡分支
        var both = new List<RedArrowComp>();
        var innerOnly = new List<RedArrowComp>();
        var outerOnly = new List<RedArrowComp>();
        foreach (var c in comps)
        {
            if (c.HasInner && c.HasOuter) both.Add(c);
            else if (c.HasInner) innerOnly.Add(c);
            else if (c.HasOuter) outerOnly.Add(c);
        }

        var arrows = new List<(RedArrowComp? Inner, RedArrowComp? Outer)>();
        foreach (var c in both) arrows.Add((c, c));

        var usedI = new bool[innerOnly.Count];
        var usedO = new bool[outerOnly.Count];
        for (int i = 0; i < innerOnly.Count; i++)
        {
            if (usedI[i]) continue;
            for (int j = 0; j < outerOnly.Count; j++)
            {
                if (usedO[j]) continue;
                if (Math.Abs(AngleDiff(innerOnly[i].Phi, outerOnly[j].Phi)) <= 0.105)
                {
                    arrows.Add((innerOnly[i], outerOnly[j]));
                    usedI[i] = true;
                    usedO[j] = true;
                    break;
                }
            }
        }
        for (int i = 0; i < innerOnly.Count; i++)
        {
            if (usedI[i]) continue;
            if (HasHpInBand(hpPts, w, cx, cy, innerOnly[i], innerOnly[i].Ring + 2 * scale, innerOnly[i].Ring + 50 * scale))
                arrows.Add((innerOnly[i], null));
        }
        for (int j = 0; j < outerOnly.Count; j++)
        {
            if (usedO[j]) continue;
            if (HasHpInBand(hpPts, w, cx, cy, outerOnly[j], outerOnly[j].Ring - 50 * scale, outerOnly[j].Ring - 2 * scale))
                arrows.Add((null, outerOnly[j]));
        }

        // 6. 产出：角度（质心角）+ 可选外接矩形（内外尖部件合并）
        var result = new List<double>(arrows.Count);
        foreach (var a in arrows)
        {
            var comp = a.Inner ?? a.Outer!;
            result.Add(comp.Phi * 180.0 / Math.PI);
            arrowRects?.Add(new Rect(
                Math.Min(a.Inner?.MinX ?? int.MaxValue, a.Outer?.MinX ?? int.MaxValue),
                Math.Min(a.Inner?.MinY ?? int.MaxValue, a.Outer?.MinY ?? int.MaxValue),
                Math.Max(a.Inner?.MaxX ?? int.MinValue, a.Outer?.MaxX ?? int.MinValue) - Math.Min(a.Inner?.MinX ?? int.MaxValue, a.Outer?.MinX ?? int.MaxValue) + 1,
                Math.Max(a.Inner?.MaxY ?? int.MinValue, a.Outer?.MaxY ?? int.MinValue) - Math.Min(a.Inner?.MinY ?? int.MaxValue, a.Outer?.MinY ?? int.MaxValue) + 1));
        }
        return result;
    }

    /// <summary>
    /// 判断血条像素是否覆盖指定连通域方向上的 [dMin, dMax] 环带（±0.07 rad 角度窗内血条像素 ≥5）。
    /// 用于遮挡分支：被血条遮挡一侧尖的箭头，另一侧尖 + 血条覆盖即判为箭头。
    /// </summary>
    private static bool HasHpInBand(List<int> hpPts, int w, double cx, double cy, RedArrowComp c, double dMin, double dMax)
    {
        int cnt = 0;
        foreach (var p in hpPts)
        {
            int x = p % w, y = p / w;
            double dx = x - cx, dy = y - cy;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < dMin || d > dMax) continue;
            if (Math.Abs(AngleDiff(Math.Atan2(dy, dx), c.Phi)) <= 0.07) cnt++;
        }
        return cnt >= 5;
    }

    /// <summary>
    /// 角度差归一化到 (-π, π]
    /// </summary>
    private static double AngleDiff(double a, double b)
    {
        double d = a - b;
        while (d > Math.PI) d -= 2 * Math.PI;
        while (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }
}
