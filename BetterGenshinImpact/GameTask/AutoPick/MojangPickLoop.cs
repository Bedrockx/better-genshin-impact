using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 莫版拾取外层循环：截图检测 F 键 -> 内层识别 -> 交互/滚轮。
/// 独立于 AutoPickTrigger 原有 OCR 拾取逻辑，仅在 MojangMatchEnabled 时由 AutoPickTrigger 调用。
/// </summary>
public sealed class MojangPickLoop
{
    /// <summary>每个物品项间隔（1080p）</summary>
    private const int ItemStepY = 72;

    /// <summary>列表顶部 y（1080p），向上扫描的下限</summary>
    private const int ListTopY = 347;

    /// <summary>列表底部 y（1080p）</summary>
    private const int ListBottomY = 733;

    /// <summary>允许检测的区域 y 上边缘上限（733-72）</summary>
    private const int ListBottomMinusItem = ListBottomY - ItemStepY;

    /// <summary>向下最多检测的个数</summary>
    private const int MaxScanCount = 5;

    /// <summary>满背包检查间隔毫秒</summary>
    private const int BagFullCheckIntervalMs = 2500;

    /// <summary>黑名单/无结果日志节流间隔毫秒</summary>
    private const long BlockedLogIntervalMs = 1000;

    /// <summary>满背包提示文字区域（1080p 坐标）</summary>
    private const int BagFullTextX = 560;
    private const int BagFullTextY = 450;
    private const int BagFullTextW = 800;
    private const int BagFullTextH = 170;

    private readonly ILogger _logger = App.GetLogger<MojangPickLoop>();

    /// <summary>二次确认失败标记：存在时下一轮转入滚轮分支并消费。</summary>
    private bool _skipConfirm;

    private readonly MojangPickFilter _filter = new();

    /// <summary>颜色展示顺序，与 MojangMatch 中 Refs 一致。</summary>
    private static readonly string[] ColorNames = ["灰", "绿", "蓝", "紫", "白"];

    private long _lastBagFullCheckMs;

    private long _lastBlockedLogMs;

    /// <summary>重新加载黑名单。</summary>
    public void InitFilter()
    {
        _filter.Init();
    }

    /// <summary>
    /// 同步当前配置组的拾取名单到过滤器（每帧调用，组切换时自动切换；
    /// 无配置组上下文时仅使用全局黑名单）。
    /// </summary>
    private void SyncGroupFilter()
    {
        var project = TaskContext.Instance().CurrentScriptProject;
        var groupName = project?.GroupInfo?.Name;
        var groupConfig = project?.GroupInfo?.Config.AutoPickConfig;

        var previousGroupName = _filter.CurrentGroupName;
        _filter.SetCurrentGroup(groupName, groupConfig);

        // 配置组切换时（含进入配置组）输出一次生效的额外名单
        if (groupName is not null && previousGroupName != groupName)
        {
            LogGroupListApplied(groupName, groupConfig);
        }
    }

    /// <summary>配置组拾取名单生效时输出日志（仅在进入/切换配置组时输出一次）。</summary>
    private void LogGroupListApplied(string groupName, AutoPickGroupConfig? groupConfig)
    {
        var white = groupConfig is { WhiteList.Count: > 0 } ? string.Join("、", groupConfig.WhiteList) : "无";
        var black = groupConfig is { BlackList.Count: > 0 } ? string.Join("、", groupConfig.BlackList) : "无";
        _logger.LogInformation(
            "启动自动拾取（配置组：{Group}），额外白名单：{White}，额外黑名单：{Black}", groupName, white, black);
    }

    /// <summary>
    /// 满背包检查（节流）。检测到「背包已满」提示时，OCR 物品名并自动加入黑名单。
    /// </summary>
    public void CheckBagFull(CaptureContent content)
    {
        if (Environment.TickCount64 - _lastBagFullCheckMs < BagFullCheckIntervalMs)
        {
            return;
        }

        _lastBagFullCheckMs = Environment.TickCount64;

        var region = content.CaptureRectArea;
        var itemFullRo = RecognitionAssets.Get("AutoPick", "ItemFull", region);
        if (region.Find(itemFullRo).IsEmpty())
        {
            return;
        }

        var scale = region.Width / 1920.0;
        var textRect = new Rect((int)(BagFullTextX * scale), (int)(BagFullTextY * scale), (int)(BagFullTextW * scale), (int)(BagFullTextH * scale));

        string ocrText;
        using (var textMat = new Mat(region.SrcMat, textRect))
        {
            ocrText = OcrFactory.Paddle.Ocr(textMat);
        }

        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return;
        }

        var names = MojangMatch.Instance.FindNamesByItemText(ocrText);
        if (names.Count == 0)
        {
            return;
        }

        _filter.AddToBlackList(names);
        _logger.LogWarning("检测到背包满，自动加入黑名单：{Names}", string.Join("、", names));
    }

    /// <summary>
    /// 执行一轮外层循环。
    /// </summary>
    public void Tick(CaptureContent content, Region foundRectArea, AutoPickAssets assets, double scale)
    {
        SyncGroupFilter();

        var testMode = TaskContext.Instance().Config.AutoPickConfig.TestModeEnabled;
        if (!testMode)
        {
            RemoveTestDraw();
        }

        if (foundRectArea.IsEmpty())
        {
            // 无 F 图标：清除二次确认失败标记，避免残留到后续轮次
            _skipConfirm = false;

            if (testMode)
            {
                RemoveTestDraw();
            }

            // 无 F 图标：存在滚轮图标则向下滚动一次，否则不操作
            if (HasScrollIcon(content.CaptureRectArea))
            {
                ScrollTimes(1);
            }

            return;
        }

        if (testMode)
        {
            DrawTest(foundRectArea, null);
        }

        // 有 F 图标：内层识别右侧物品（占位：识别到即视为需要交互，后续再细化判定）
        var result = MojangMatch.Instance.Match(content.CaptureRectArea.SrcMat, foundRectArea, scale);

        if (result is null)
        {
            // 未识别到物品：转入滚轮分支
            LogNoResult();
            HandleScroll(content, foundRectArea, scale);
            return;
        }

        var r = result.Value;

        if (testMode)
        {
            DrawTest(foundRectArea, r.Region);
            LogDetail(r);
        }
        else if (LogLevel >= 2)
        {
            // 调试级别：正常识别也输出完整信息（耗时/位置/匹配度）
            LogDetail(r);
        }

        if (_skipConfirm)
        {
            // 二次确认失败标记存在：消费标记并转入滚轮分支
            _skipConfirm = false;
            HandleScroll(content, foundRectArea, scale);
            return;
        }

        var config = TaskContext.Instance().Config.AutoPickConfig;
        var repeatFInterval = config.RepeatFInterval;
        var confirmInterval = config.ConfirmInterval;

        // 二次识别：由 ConfirmInterval 配置决定，与连点 F 无关
        if (confirmInterval > 0)
        {
            Thread.Sleep(confirmInterval);
            using var newCapture = TaskControl.CaptureToRectArea();
            using var newFound = newCapture.Find(assets.PickRo);
            if (newFound.IsEmpty())
            {
                return; // 二次确认时 F 键消失，本轮结束
            }

            if (!MojangMatch.Instance.ConfirmMatch(newCapture.SrcMat, newFound, scale, r.Name, r.ColorIndex))
            {
                _skipConfirm = true;
                return;
            }
        }

        if (!ShouldInteract(r.Name))
        {
            // 不可交互（黑名单命中或测试模式）：不拾取，转滚轮分支继续找下一个
            LogBlocked(r);
            HandleScroll(content, foundRectArea, scale);
            return;
        }

        if (repeatFInterval > 0)
        {
            RepeatF(r, content, foundRectArea, scale, assets, repeatFInterval);
        }
        else
        {
            Interact(r.Name, r.ItemName, assets);
        }

        Thread.Sleep(config.InteractionDelay);
    }

    /// <summary>
    /// 连点 F：从当前行开始（含当前）连续 n 个都需要交互时，间隔连点 n 次 F。
    /// 每次按 F 对应列表中的不同物品，日志按各自物品名输出。
    /// </summary>
    private void RepeatF(MojangMatchResult r, CaptureContent content, Region foundRectArea, double scale, AutoPickAssets assets, int repeatFInterval)
    {
        var scanCount = Math.Min(MaxScanCount, (ListBottomMinusItem - foundRectArea.Y) / ItemStepY);
        var items = new List<(string Name, string ItemName)> { (r.Name, r.ItemName) }; // 当前行
        for (var k = 1; k <= scanCount; k++)
        {
            using var region = new Region(foundRectArea.X, foundRectArea.Y + ItemStepY * k, foundRectArea.Width, foundRectArea.Height);
            var m = MojangMatch.Instance.Match(content.CaptureRectArea.SrcMat, region, scale);
            if (m is not { } mr || !ShouldInteract(mr.Name))
            {
                break; // 遇到不需要交互的行（识别不到或黑名单），停止计数
            }

            items.Add((mr.Name, mr.ItemName)); // 连续需要交互
        }

        if (items.Count >= 2 && LogLevel >= 1)
        {
            _logger.LogInformation("启用连点拾取，连点{N}次，间隔{Interval}毫秒", items.Count, repeatFInterval);
        }

        for (var i = 0; i < items.Count; i++)
        {
            Interact(items[i].Name, items[i].ItemName, assets);
            if (i < items.Count - 1)
            {
                Thread.Sleep(repeatFInterval);
            }
        }
    }

    /// <summary>
    /// 滚轮分支：检测滚轮图标并决定滚动策略。
    /// </summary>
    private void HandleScroll(CaptureContent content, Region foundRectArea, double scale)
    {
        if (!HasScrollIcon(content.CaptureRectArea))
        {
            // 无滚轮图标：向下滚动一次
            ScrollTimes(1);
            return;
        }

        // 有滚轮图标：向下检测 k 个位置，找需要交互的物品
        if (foundRectArea.Y + ItemStepY > ListBottomMinusItem)
        {
            // 已在底部（k=1 就超出范围）：滚一次，游戏机制会回到顶部
            ScrollTimes(1);
            return;
        }

        var scanCount = Math.Min(MaxScanCount, (ListBottomMinusItem - foundRectArea.Y) / ItemStepY);
        var hitIndex = -1;
        for (var k = 1; k <= scanCount; k++)
        {
            using var region = new Region(foundRectArea.X, foundRectArea.Y + ItemStepY * k, foundRectArea.Width, foundRectArea.Height);
            var m = MojangMatch.Instance.Match(content.CaptureRectArea.SrcMat, region, scale);
            if (m is not { } mr)
            {
                continue;
            }

            if (TaskContext.Instance().Config.AutoPickConfig.TestModeEnabled || LogLevel >= 2)
            {
                LogDetail(mr);
            }

            if (ShouldInteract(mr.Name))
            {
                hitIndex = k;
                break;
            }
        }

        if (hitIndex > 0)
        {
            ScrollTimes(hitIndex); // 第 i 个是需要交互的物品：滚 i 次
            return;
        }

        // F 行和向下 k 个都无需交互：尝试向上寻找，存在需要交互的则向上滑动
        var upCount = ScanUpward(content, foundRectArea, scale);
        if (upCount > 0)
        {
            ScrollTimesUp(upCount);
            return;
        }

        ScrollTimes(Math.Max(1, scanCount - 1)); // 都不是：至少滚一次
    }

    /// <summary>
    /// 向上扫描：从 F 上一行开始向上（直到 y 上边缘 347），
    /// 找到第一个需要交互的行，返回需要向上滚动的次数；找不到返回 0。
    /// </summary>
    private int ScanUpward(CaptureContent content, Region foundRectArea, double scale)
    {
        var k = 1;
        while (true)
        {
            var y = foundRectArea.Y - ItemStepY * k;
            if (y < ListTopY)
            {
                return 0; // 已到列表顶部，未找到
            }

            using var region = new Region(foundRectArea.X, y, foundRectArea.Width, foundRectArea.Height);
            var m = MojangMatch.Instance.Match(content.CaptureRectArea.SrcMat, region, scale);
            if (m is { } mr)
            {
                if (TaskContext.Instance().Config.AutoPickConfig.TestModeEnabled || LogLevel >= 2)
                {
                    LogDetail(mr);
                }

                if (ShouldInteract(mr.Name))
                {
                    return k; // 距当前行 k 行：向上滚动 k 次
                }
            }

            k++;
        }
    }

    /// <summary>
    /// 滚动。滚动间隔为 0 时每轮最多滚动一次；否则连续滚动 times 次并间隔等待。
    /// </summary>
    private void ScrollTimes(int times)
    {
        var config = TaskContext.Instance().Config.AutoPickConfig;

        var actualTimes = config.ScrollInterval > 0 ? times : 1;
        for (var i = 0; i < actualTimes; i++)
        {
            ScrollDownOnce(config.ScrollDistance);
            if (i < actualTimes - 1)
            {
                Thread.Sleep(config.ScrollInterval);
            }
        }

        Thread.Sleep(config.ScrollAfterInterval);
    }

    /// <summary>
    /// 向上滚动。滚动间隔为 0 时每轮最多滚动一次；否则连续滚动 times 次并间隔等待。
    /// </summary>
    private void ScrollTimesUp(int times)
    {
        var config = TaskContext.Instance().Config.AutoPickConfig;

        var actualTimes = config.ScrollInterval > 0 ? times : 1;
        for (var i = 0; i < actualTimes; i++)
        {
            ScrollUpOnce(config.ScrollDistance);
            if (i < actualTimes - 1)
            {
                Thread.Sleep(config.ScrollInterval);
            }
        }

        Thread.Sleep(config.ScrollAfterInterval);
    }

    /// <summary>
    /// 向下滚动一次。ScrollDistance 为滚轮 delta 单位（Windows WHEEL_DELTA 语义），
    /// 直接写入 mouseData，向下为负值。
    /// </summary>
    private static void ScrollDownOnce(int delta)
    {
        var inputList = new InputBuilder().AddMouseVerticalWheelScroll(-delta).ToArray();
        var sent = User32.SendInput((uint)inputList.Length, inputList, Marshal.SizeOf(typeof(User32.INPUT)));
        if (sent != (uint)inputList.Length)
        {
            throw new Exception("模拟滚轮消息发送失败");
        }
    }

    /// <summary>
    /// 向上滚动一次。滚轮向上为正值。
    /// </summary>
    private static void ScrollUpOnce(int delta)
    {
        var inputList = new InputBuilder().AddMouseVerticalWheelScroll(delta).ToArray();
        var sent = User32.SendInput((uint)inputList.Length, inputList, Marshal.SizeOf(typeof(User32.INPUT)));
        if (sent != (uint)inputList.Length)
        {
            throw new Exception("模拟滚轮消息发送失败");
        }
    }

    /// <summary>
    /// 交互（按 F）。
    /// </summary>
    private void Interact(string name, string itemName, AutoPickAssets assets)
    {
        _logger.LogInformation("交互或拾取：{Name}", name);
        Simulation.SendInput.Keyboard.KeyPress(assets.PickVk);
    }

    /// <summary>
    /// 是否应交互。测试模式下所有物品均视为不可交互。
    /// </summary>
    private bool ShouldInteract(string name)
    {
        if (TaskContext.Instance().Config.AutoPickConfig.TestModeEnabled)
        {
            return false;
        }

        return _filter.ShouldPick(name);
    }

    /// <summary>
    /// 莫版拾取日志级别（0 精简 / 1 常规 / 2 调试），运行时读取即时生效。
    /// </summary>
    private static int LogLevel => TaskContext.Instance().Config.AutoPickConfig.PickLogLevel;

    /// <summary>
    /// 无有效结果日志：常规级别节流 1s，调试级别每次输出。
    /// </summary>
    private void LogNoResult()
    {
        if (LogLevel < 1)
        {
            return;
        }

        if (LogLevel >= 2 || ThrottleBlockedLog())
        {
            _logger.LogInformation("无有效结果，不交互");
        }
    }

    /// <summary>
    /// 黑名单命中日志：常规级别节流 1s，调试级别每次输出并追加识别详情。
    /// </summary>
    private void LogBlocked(MojangMatchResult r)
    {
        if (LogLevel < 1)
        {
            return;
        }

        if (LogLevel >= 2 || ThrottleBlockedLog())
        {
            _logger.LogInformation("识别到{Name}，不交互", r.Name);
            if (LogLevel >= 2)
            {
                LogDetail(r);
            }
        }
    }

    /// <summary>
    /// 黑名单/无结果日志节流：1 秒内只输出一次。
    /// </summary>
    private bool ThrottleBlockedLog()
    {
        var now = Environment.TickCount64;
        if (now - _lastBlockedLogMs < BlockedLogIntervalMs)
        {
            return false;
        }

        _lastBlockedLogMs = now;
        return true;
    }

    /// <summary>
    /// 输出识别详情（识别结果 + 位置 + 各环节耗时 + 匹配度）。测试模式或调试级别时输出。
    /// </summary>
    private void LogDetail(MojangMatchResult r)
    {
        _logger.LogInformation(
            "莫版识别详情：{Name}({ItemName}) 颜色={Color} 分数={Score:F3} 位置=({X},{Y},{W},{H}) 判定={JudgeMs:F2}ms 灰度={GrayMs:F2}ms NCC={NccMs:F2}ms 模板={TemplateCount}",
            r.Name, r.ItemName, ColorNames[r.ColorIndex], r.Score,
            r.Region.X, r.Region.Y, r.Region.Width, r.Region.Height,
            r.JudgeMs, r.GrayMs, r.NccMs, r.TemplateCount);
    }

    /// <summary>
    /// 测试模式：在遮罩窗口绘制 F 键区域（青色）与识别的物品区域（绿色）。
    /// </summary>
    private static void DrawTest(Region fRegion, OpenCvSharp.Rect? itemRect)
    {
        var draw = VisionContext.Instance().DrawContent;
        draw.PutRect("MojangTestF", fRegion.SelfToRectDrawable("MojangTestF", System.Drawing.Pens.Cyan));
        if (itemRect is { } ir)
        {
            draw.PutRect("MojangTestItem", ir.ToRectDrawable(System.Drawing.Pens.Lime, "MojangTestItem"));
        }
    }

    /// <summary>
    /// 清除测试模式绘制的矩形。
    /// </summary>
    private static void RemoveTestDraw()
    {
        var draw = VisionContext.Instance().DrawContent;
        draw.RemoveRect("MojangTestF");
        draw.RemoveRect("MojangTestItem");
    }

    /// <summary>
    /// 滚轮图标检测（固定区域颜色判断，与 AutoPickTrigger.HasScrollIcon 一致）。
    /// </summary>
    private static bool HasScrollIcon(ImageRegion captureRectArea)
    {
        var mat = captureRectArea.SrcMat;
        var color1 = mat.At<Vec3b>(537, 1062);
        var color2 = mat.At<Vec3b>(524, 1062);
        var color3 = mat.At<Vec3b>(554, 1062);
        return color1.Item2 == 255 && color1.Item1 == 233 && color1.Item0 == 44
            && color2.Item2 == 255 && color2.Item1 == 255 && color2.Item0 == 255
            && color3.Item2 == 255 && color3.Item1 == 255 && color3.Item0 == 255;
    }
}
