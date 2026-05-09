using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using System.Net.NetworkInformation;
using BetterGenshinImpact.GameTask.Common.Job;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using System.Linq;
using BetterGenshinImpact.Core.Recognition;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.Core.Simulator.Extensions;

namespace BetterGenshinImpact.GameTask.Common;

public class TaskControl
{
    public static ILogger Logger { get; } = App.GetLogger<TaskControl>();

    public static readonly SemaphoreSlim TaskSemaphore = new(1, 1);
    
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static DateTime _lastCheckTimeEnter = DateTime.MinValue;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(TaskContext.Instance().Config.OtherConfig.NetworkDetectionInterval);
    private static readonly TimeSpan CheckIntervalWin = TimeSpan.FromSeconds(30);
    private static readonly Ping PingSender = new Ping();
    private static readonly bool NetworkDetectionConfig = TaskContext.Instance().Config.OtherConfig.NetworkDetectionConfig;
    private static int _networkFailureCount = 0;
    
    private static RecognitionObject GetConfirmRa(bool isOcrMatch = false,params string[] targetText)
    {
        var screenArea = CaptureToRectArea();
        var x = (int)(screenArea.Width * 0.3);
        var y = (int)(screenArea.Height * 0.1);
        var width = (int)(screenArea.Width * 0.65);
        var height = (int)(screenArea.Height * 0.87);
        
        return isOcrMatch ? RecognitionObject.OcrMatch(x, y, width, height, targetText) : 
            RecognitionObject.Ocr(x, y, width, height);
    }
    
    public static bool IsSuspendedByNetwork { get; set; } = false;
    
    public static bool IsSuspendedByWindow { get; set; } = false;
    
    private static bool _isBless = false;

    private static Task CheckNetworkStatusAsync()
    {
        if (DateTime.UtcNow - _lastCheckTime < CheckInterval)
        {
            if (DateTime.UtcNow - _lastCheckTimeEnter > CheckIntervalWin)
            { 
                _lastCheckTimeEnter = DateTime.UtcNow;
                using var qq = CaptureToRectArea();
                using var okRa = qq.Find(AutoFightAssets.Instance.ConfirmRaZ);
                using var enterRa = qq.Find(AutoWoodAssets.Instance.ExitSwitchRo);
                //如果现在是4点到4点5分内
                if (DateTime.UtcNow.Hour == 4 && DateTime.UtcNow.Minute >= 0 && DateTime.UtcNow.Minute < 3)
                {
                    if ((Bv.IsInBlessingOfTheWelkinMoon(qq)) && !_isBless)   
                    {
                        try
                        {
                            Logger.LogInformation("空月任务4点检测执行");
                            _isBless = true;
                            new BlessingOfTheWelkinMoonTask().Start(CancellationToken.None).Wait(10000);
                        }
                        catch (TaskCanceledException)
                        {
                            Logger.LogWarning("空月任务执行取消");
                        }
                        catch (TimeoutException)
                        {
                            Logger.LogWarning("空月任务执行超时");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "空月任务执行失败");
                        }
                        finally
                        {
                            Logger.LogDebug("空月任务4点检测执行完毕");
                        }
                    }
                }
                
                if (okRa.IsExist()|| enterRa.IsExist())
                {
                    var enter = qq.FindMulti(GetConfirmRa());
                    using var enterDone = enter.FirstOrDefault(t =>
                        Regex.IsMatch(t.Text, "连接已断开") || Regex.IsMatch(t.Text, "点击进入"));
                    if (enterDone != null)
                    {
                        IsSuspendedByWindow = true;
                        Logger.LogWarning("点击: {enterDone.Text}",enterDone.Text);
                        if(enterRa.IsExist())enterDone.Click();
                    }
                    else
                    {
                        return Task.CompletedTask;
                    }
                }
                
            }
            else
            {
                return Task.CompletedTask;
            }
        }
        
        _lastCheckTime = DateTime.UtcNow;

        var isSuspend = false; 
        try
        {
            var reply = PingSender.Send(TaskContext.Instance().Config.OtherConfig.NetworkDetectionUrl);
            isSuspend = reply.Status != IPStatus.Success;
            if (IsSuspendedByNetwork || IsSuspendedByWindow)
            {
                Logger.LogWarning(IsSuspendedByWindow ? "窗口弹窗状态恢复中..." : "网络恢复中...");
                if (NetworkRecovery.Start(CancellationToken.None).Wait(10000))
                {
                    isSuspend = false;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "网络状态检查：错误");
            isSuspend = true;
        }
        finally
        {
            if (isSuspend)
            {
                _networkFailureCount++;
                if (_networkFailureCount >= 3)
                {
                    try
                    {
                        var reply2 = PingSender.Send("www.qq.com");
                        if (reply2.Status != IPStatus.Success)
                        {
                            IsSuspendedByNetwork = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "网络状态检查：错误");
                        IsSuspendedByNetwork = true;
                    }
                }
            }
            else
            {
                _networkFailureCount = 0;
                IsSuspendedByNetwork = false;
                // var now = DateTime.UtcNow; // 声明并初始化 now 变量
                //
                // var targetStartTime = new DateTime(now.Year, now.Month, now.Day, 3, 59, 0); // 设置为当天的凌晨3点59分
                // var targetEndTime = new DateTime(now.Year, now.Month, now.Day, 4, 0, 0); // 设置为当天的凌晨4点
                //
                // if (now - _startTime > TimeSpan.FromDays(1) || (now >= targetStartTime && now < targetEndTime))
                // {
                //     throw new RetryException("超过1天未启动游戏，尝试重启游戏");
                // }
            }
        }
        return Task.CompletedTask;
    }

    public static void CheckAndSleep(int millisecondsTimeout)
    {
        TrySuspend();
        CheckAndActivateGameWindow();
        Thread.Sleep(millisecondsTimeout);
    }

    public static void Sleep(int millisecondsTimeout)
    {
        NewRetry.Do(() =>
        {
            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
    }

    private static bool IsKeyPressed(User32.VK key)
    {
        // 获取按键状态
        var state = User32.GetAsyncKeyState((int)key);

        // 检查高位是否为 1（表示按键被按下）
        return (state & 0x8000) != 0;
    }

    public static void TrySuspend()
    {
        if (NetworkDetectionConfig)Task.Run(CheckNetworkStatusAsync);
        var first = true;
        //此处为了记录最开始的暂停状态
        var isSuspend = RunnerContext.Instance.IsSuspend || IsSuspendedByNetwork;
        while (RunnerContext.Instance.IsSuspend || IsSuspendedByNetwork)
        {
            if (RunnerContext.Instance.IsSuspend) IsSuspendedByNetwork = false; NetworkRecovery.RecoveryNetworkDone = true;
            if (first)
            {
                RunnerContext.Instance.StopAutoPick();
                //使快捷键本身释放
                Thread.Sleep(300);
                foreach (User32.VK key in Enum.GetValues(typeof(User32.VK)))
                {
                    // 检查键是否被按下
                    if (IsKeyPressed(key)) // 强制转换 VK 枚举为 int
                    {
                        Logger.LogWarning($"解除{key}的按下状态.");
                        Simulation.SendInput.Keyboard.KeyUp(key);
                    }
                }

                Logger.LogWarning(IsSuspendedByNetwork ? "网络检测失败触发暂停，等待解除" : "快捷键触发暂停，等待解除");
                foreach (var item in RunnerContext.Instance.SuspendableDictionary)
                {
                    item.Value.Suspend();
                }

                first = false;
            }

            if (IsSuspendedByNetwork)
            {
                CheckNetworkStatusAsync().Wait(1000, CancellationToken.None);
            }

            Thread.Sleep(1000);
        }

        //从暂停中解除
        if (isSuspend)
        {
            Logger.LogWarning("暂停已经解除");
            RunnerContext.Instance.ResumeAutoPick();
            foreach (var item in RunnerContext.Instance.SuspendableDictionary)
            {
                item.Value.Resume();
            }
        }
    }

    private static void CheckAndActivateGameWindow()
    {
        if (IsSuspendedByNetwork)
        {
            Logger.LogInformation("网络恢复中，暂停尝试恢复窗口");
            return;
        }
        
        if (!TaskContext.Instance().Config.OtherConfig.RestoreFocusOnLostEnabled)
        {
            if (!SystemControl.IsGenshinImpactActiveByProcess())
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogWarning($"当前获取焦点的窗口为: {name}，不是原神，暂停");
                throw new RetryException("当前获取焦点的窗口不是原神");
            }
        }

        var count = 0;
        //未激活则尝试恢复窗口
        while (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            if (count >= 10 && count % 10 == 0)
            {
                Logger.LogInformation("多次尝试未恢复，尝试最小化后激活窗口！");
                SystemControl.MinimizeAndActivateWindow(TaskContext.Instance().GameHandle);
            }
            else
            {
                var name = SystemControl.GetActiveByProcess();
                Logger.LogInformation("当前获取焦点的窗口为: {Name}，不是原神，尝试恢复窗口", name);
                SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
            }

            count++;
            Thread.Sleep(1000);
        }
    }

    public static void Sleep(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct.IsCancellationRequested)
            {
                throw new NormalEndException("取消自动任务");
            }
            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        Thread.Sleep(millisecondsTimeout);
        if (ct.IsCancellationRequested)
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    public static async Task Delay(int millisecondsTimeout, CancellationToken ct)
    {
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }

        if (millisecondsTimeout <= 0)
        {
            return;
        }

        NewRetry.Do(() =>
        {
            if (ct is { IsCancellationRequested: true })
            {
                throw new NormalEndException("取消自动任务");
            }
            TrySuspend();
            CheckAndActivateGameWindow();
        }, TimeSpan.FromSeconds(1), 100);
        await Task.Delay(millisecondsTimeout, ct);
        if (ct is { IsCancellationRequested: true })
        {
            throw new NormalEndException("取消自动任务");
        }
    }

    /// <summary>
    /// 模拟长按指定动作。使用 try/finally 块确保在任务被取消或发生异常时，按键也能安全释放，防止卡键。
    /// </summary>
    /// <param name="action">需要模拟的游戏动作（如元素战技、普通攻击等）</param>
    /// <param name="holdMs">长按持续的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    public static async Task SimulateHoldActionAsync(GIActions action, int holdMs, CancellationToken ct)
    {
        try
        {
            Simulation.SendInput.SimulateAction(action, KeyType.KeyDown);
            await Delay(holdMs, ct);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(action, KeyType.KeyUp);        
        }
    }

    /// <summary>
    /// 模拟长按元素战技（如万叶长E）。包含释放前摇、长按以及释放后的缓冲延时。
    /// </summary>
    /// <param name="holdMs">元素战技按住的时间（毫秒）</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="releaseLeftMouseBefore">是否在按下元素战技前先松开鼠标左键，避免输入冲突，默认 true</param>
    /// <param name="releaseLeftMouseDelayMs">松开鼠标左键后的缓冲时间（毫秒），默认 10ms</param>
    /// <param name="postKeyUpDelayMs">元素战技释放后的缓冲时间（毫秒），默认 50ms</param>
    public static async Task SimulateHoldElementalSkillAsync(
        int holdMs,
        CancellationToken ct,
        bool releaseLeftMouseBefore = true,
        int releaseLeftMouseDelayMs = 10,
        int postKeyUpDelayMs = 50)
    {
        if (releaseLeftMouseBefore)
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            await Delay(releaseLeftMouseDelayMs, ct);
        }

        await SimulateHoldActionAsync(GIActions.ElementalSkill, holdMs, ct);   
        await Delay(postKeyUpDelayMs, ct);
    }

    /// <summary>
    /// 模拟鼠标左键连续点击循环（如万叶长E后的下落攻击）。双层 try/finally 设计以确保无论在循环的哪个阶段发生取消或异常，鼠标左键都会被强制释放。
    /// </summary>
    /// <param name="repeatCount">需要循环点击的次数</param>
    /// <param name="ct">用于监控任务取消的取消令牌</param>
    /// <param name="preUpDelayMs">每次点击前，预先抬起左键后的缓冲延时（毫秒），默认 10ms</param>
    /// <param name="downHoldMs">鼠标左键按下的保持时间（毫秒），默认 35ms</param>
    /// <param name="postUpDelayMs">每次点击完成后的等待时间（毫秒），默认 50ms</param>
    public static async Task SimulateMouseLeftClickLoopAsync(
        int repeatCount,
        CancellationToken ct,
        int preUpDelayMs = 10,
        int downHoldMs = 35,
        int postUpDelayMs = 50)
    {
        try
        {
            for (var i = 0; i < repeatCount; i++)
            {
                Simulation.SendInput.Mouse.LeftButtonUp();
                await Delay(preUpDelayMs, ct);
                Simulation.SendInput.Mouse.LeftButtonDown();
                try
                {
                    await Delay(downHoldMs, ct);
                }
                finally
                {
                    Simulation.SendInput.Mouse.LeftButtonUp();
                }

                await Delay(postUpDelayMs, ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }
    }

    public static Mat CaptureGameImage(IGameCapture? gameCapture)
    {
        var image = gameCapture?.Capture();
        if (image == null)
        {
            Logger.LogWarning("截图失败!");
            // 重试3次
            for (var i = 0; i < 3; i++)
            {
                image = gameCapture?.Capture();
                if (image != null)
                {
                    return image;
                }

                Sleep(30);
            }

            throw new Exception("尝试多次后,截图失败!");
        }
        else
        {
            return image;
        }
    }

    public static Mat? CaptureGameImageNoRetry(IGameCapture? gameCapture)
    {
        return gameCapture?.Capture();
    }

    /// <summary>
    /// 自动判断当前运行上下文中截图方式，并选择合适的截图方式返回
    /// </summary>
    /// <returns></returns>
    public static ImageRegion CaptureToRectArea(bool forceNew = false)
    {
        var image = CaptureGameImage(TaskTriggerDispatcher.GlobalGameCapture);
        var content = new CaptureContent(image, 0, 0);
        return content.CaptureRectArea;
    }
}
