using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;
using GC = System.GC;
using OpenCvSharp;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木
/// </summary>
public partial class AutoWoodTask : ISoloTask
{
    public string Name => "自动伐木";

    private AutoWoodAssets _assets;

    private bool _first = true;

    private WoodStatisticsPrinter _printer;

    private readonly Login3rdParty _login3RdParty;

    // private VK _zKey = VK.VK_Z;

    private readonly WoodTaskParam _taskParam;

    private CancellationToken _ct;
    
    private EnterAndExitWonderlandJob _enterAndExitWonderlandJob;
    
    // 静态字段，用于记录全局的木材类型和数量
    public static Dictionary<string, int> GlobalResultDict = new Dictionary<string, int>();

    public AutoWoodTask(WoodTaskParam taskParam)
    {
        this._taskParam = taskParam;
        _login3RdParty = new();
        AutoWoodAssets.DestroyInstance();
    }

    public async Task Start(CancellationToken ct)
    {
        _assets = AutoWoodAssets.Instance;
        _printer = new WoodStatisticsPrinter(_assets);
        _enterAndExitWonderlandJob = new EnterAndExitWonderlandJob();
        var runTimeWatch = new Stopwatch();
        _ct = ct;
        _printer.Ct = _ct;

        try
        {
            Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS | Kernel32.EXECUTION_STATE.ES_SYSTEM_REQUIRED | Kernel32.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
            Logger.LogInformation("→ {Text} 设置伐木总次数：{Cnt}，设置木材数量上限：{MaxCnt}", "自动伐木，启动！", _taskParam.WoodRoundNum, _taskParam.WoodDailyMaxCount);

            _login3RdParty.RefreshAvailabled();
            if (_login3RdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
            {
                Logger.LogInformation("自动伐木启用B服模式");
            }

            // SettingsContainer settingsContainer = new();
            //
            // if (settingsContainer.OverrideController?.KeyboardMap?.ActionElementMap.Where(item => item.ActionId == ActionId.Gadget).FirstOrDefault()?.ElementIdentifierId is ElementIdentifierId key)
            // {
            //     if (key != ElementIdentifierId.Z)
            //     {
            //         _zKey = key.ToVK();
            //         Logger.LogInformation($"自动伐木检测到用户改键 {ElementIdentifierId.Z.ToName()} 改为 {key.ToName()}");
            //         if (key == ElementIdentifierId.LeftShift || key == ElementIdentifierId.RightShift)
            //         {
            //             Logger.LogInformation($"用户改键 {key.ToName()} 可能不受模拟支持，若使用正常则忽略");
            //         }
            //     }
            // }

            SystemControl.ActivateWindow();
            
            GlobalResultDict = new Dictionary<string, int>();
            
            // 伐木开始计时
            runTimeWatch.Start();
            for (var i = 0; i < _taskParam.WoodRoundNum; i++)
            {
                if (TaskContext.Instance().Config.AutoWoodConfig.WoodCountOcrEnabled)
                {
                    if (_printer.WoodStatisticsAlwaysEmpty())
                    {
                        Logger.LogWarning("连续{Cnt}次获取木材数量为0。判定附近没有能响应「王树瑞佑」的树木！或者已达每日数量上限", _printer.NothingCount);
                        break;
                    }

                    if (_printer.ReachedWoodMaxCount)
                    {
                        break;
                    }
                    
                    if (i == 1 && _printer.NothingCount == 1)
                    {
                        TaskContext.Instance().Config.AutoWoodConfig.WoodCountOcrEnabled = false;
                        Logger.LogWarning("首次伐木就未识别到木材数据，已经自动关闭【识别并累计木材数】的功能，请重新启动【自动伐木】功能！");
                        break;
                    }

                    if (i == 1)
                    {
                        _printer.FirstWoodTrue = true;
                        Logger.LogInformation("首次伐木识别到伐木数据，已启用识别并累计木材数功能！");
                    }
                }

                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                if (_ct.IsCancellationRequested)
                {
                    break;
                }

                await Felling(_taskParam, i + 1 == _taskParam.WoodRoundNum);
                VisionContext.Instance().DrawContent.ClearAll();
                Sleep(500, _ct);
            }

            return;
        }
        finally
        {
            // 伐木结束计时
            runTimeWatch.Stop();
            Kernel32.SetThreadExecutionState(Kernel32.EXECUTION_STATE.ES_CONTINUOUS);
            var elapsedTime = runTimeWatch.Elapsed;
            Logger.LogInformation(@"本次伐木总耗时：{Time:hh\:mm\:ss}", elapsedTime);
        }
    }

    private partial class WoodStatisticsPrinter(AutoWoodAssets assert)
    {
        public bool ReachedWoodMaxCount;
        public int NothingCount;
        public int NothingWoodCount;
        public bool FirstWoodTrue = false;
        
        // private static readonly List<string> ExistWoods =
        // [
        //      "椴木","枫木","萃华木","垂香木", "竹节","桦木","杉木","松木","却砂木", "证悟木", "梦见木",  "御伽木",
        //     "业果木", "辉木", "白梣木", "炬木", "香柏木","悬铃木","燃爆木", "白栗栎木"  , "灰灰楼林木" , "桃椰子木"
        //     , "柽木" , "刺葵木","孔雀木"
        // ];
        
        private static readonly Dictionary<string, string> WoodNames = new Dictionary<string, string>
        {
            {"椴木", "Linden"}, {"枫木", "Maple"}, {"萃华木", "Cuihua"}, {"垂香木", "Fragrant"}, {"竹节", "Bamboo"},
            {"桦木", "Birch"}, {"杉木", "Fir"}, {"松木", "Pine"}, {"却砂木", "Sandbearer"}, {"证悟木", "Adhigama"},
            {"梦见木", "Yumemiru"}, {"御伽木", "Otogi"}, {"业果木", "Karmaphala"}, {"辉木", "Bright"}, {"白梣木", "Ash"},
            {"炬木", "Torch"}, {"香柏木", "Cypress"}, {"悬铃木", "Mallow"}, {"燃爆木", "Flammabomb"}, {"白栗栎木", "WhiteChestnutOak"},
            {"灰灰楼林木", "AshenAratiku"}, {"桃椰子木", "PeachPalm"}, {"榛木", "Chestnut"}, {"柽木", "Athel"}, {"刺葵木", "Mountain"}, {"孔雀木", "Aralia"},
             {"桤木", "Alnus"}, {"银冷杉木", "SilverFir"},{"夏栎木", "Summer"}
        };
        //{"夏栎木", "Summer"},
        
        private static readonly int[] WoodNumbers = { 12, 15, 18, 21, 24, 27, 30, 3, 6, 9,1, 2, 4,5,10};//7,8
        
        private readonly Vanara.PInvoke.RECT _gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        
        private readonly AutoWoodConfig _config = TaskContext.Instance().Config.AutoWoodConfig;

        public CancellationToken Ct { get; set; }
        
        private int _woodCountAll = 0;

        public bool WoodStatisticsAlwaysEmpty()
        {
            return NothingCount >= 3;
        }

        //连通性检测木材显示的稳定性
        private async Task<bool> WoodTextAreaJudgment()
        {
            var stableCount = 0; // 用于跟踪色块数量稳定次数的计数器
            var previousNumLabels = 0; // 保存上一次的连通区域数量
            var tolerance = 3; // 色块数量稳定容忍度
            
            var ms = _config.AfterZSleepDelay;

            while (ms > 0)
            {
                var woodCountRect = CaptureToRectArea().DeriveCrop(assert.WoodCountUpperRect);
                var woodNum = new Scalar(236, 229, 216);
                var mask = OpenCvCommonHelper.Threshold(woodCountRect.SrcMat, woodNum);
                var labels = new Mat();
                var stats = new Mat();
                var centroids = new Mat();

                var numLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
                    connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

                labels.Dispose();
                stats.Dispose();
                centroids.Dispose();
                woodCountRect.Dispose();

                // Logger.LogWarning("识别伐木: 共{Cnt}个连通区域", numLabels);//测试LOG

                if (numLabels > 20)
                {
                    if (Math.Abs(numLabels - previousNumLabels) <= tolerance)
                    {
                        stableCount++;
                    }
                    else
                    {
                        stableCount = 0;
                        previousNumLabels = numLabels;
                    }

                    if (stableCount >= 6) // 只有在色块数量稳定超过6次并且大于20时才返回true
                    {
                        return true;
                    }  
                }

                ms -= 48; // 颜色识别要点时间，不用太精准
                await Task.Delay(50, Ct); 
            }

            return false;
        }
        
        public async Task<bool> PrintWoodStatistics(WoodTaskParam taskParam)
        {
           var woodColor = await WoodTextAreaJudgment(); 
            if (!woodColor)
            {
                NothingCount++;
                
               return FirstWoodTrue && NothingCount <= 2;
            }

            NothingCount = 0;
            NothingWoodCount = 0;
            
            var woodText = GetWoodStatisticsText();
            
            _woodCountAll =  woodText + _woodCountAll ;

            if (_woodCountAll == 0)
            {
                return false;
            }
            
            //上限控制
            var woodLimitType = false;
            Logger.LogInformation("设置的木材数量上限类型：{MaxWoodType}", _config.MaxWoodType);
            
            if (_config.MaxWoodType == "任意一种木材上限")
            {
               woodLimitType = taskParam.WoodDailyMaxCount <= GlobalResultDict.Values.Max();
               var mostWoodType = GlobalResultDict.OrderByDescending(kvp => kvp.Value).First().Key;
               Logger.LogInformation("木材 {mostWoodType} 目前最多，数量已达到：{Max}/{MaxCnt}", mostWoodType,GlobalResultDict.Values.Max() ,taskParam.WoodDailyMaxCount);
            }
            else if (_config.MaxWoodType == "总数上限")
            {
                woodLimitType = taskParam.WoodDailyMaxCount <= GlobalResultDict.Values.Sum();
                Logger.LogInformation("已达到设置的伐木总数：{Max}/{MaxCnt}", GlobalResultDict.Values.Sum(), taskParam.WoodDailyMaxCount);
            }
            else if (_config.MaxWoodType == "指定木材上限")
            {
               var singleWoodLimitCount = GlobalResultDict.Keys
                                            .Where(v => v == _config.SingleWoodLimit)
                                            .Select(k => GlobalResultDict[k])
                                            .Sum();

                woodLimitType = taskParam.WoodDailyMaxCount <= singleWoodLimitCount;

                Logger.LogInformation("指定木材 {SingleWoodLimit} 数量已达到：{Max}/{MaxCnt}", 
                    _config.SingleWoodLimit, singleWoodLimitCount, taskParam.WoodDailyMaxCount);
            }
            
            if (_config.MaxWoodType == "指定木材上限" && !GlobalResultDict.ContainsKey(_config.SingleWoodLimit))
            {
                woodLimitType = true;
                Logger.LogInformation("首次指定木材 {SingleWoodLimit} 不存在，退出伐木任务", _config.SingleWoodLimit);
            }
            
            if (_config.MaxWoodType == "指定木材上限" && NothingWoodCount >= 2)
            {
                ReachedWoodMaxCount = true;
                Logger.LogInformation("指定木材 {SingleWoodLimit} 两次未识别到木材，退出伐木任务", _config.SingleWoodLimit);
            }
            
            if (woodLimitType)
            {
                ReachedWoodMaxCount = true;
                return false;
            }
            
            ReachedWoodMaxCount = false;
            return true;
        }

        private int GetWoodStatisticsText()
        {
            // 创建一个计时器，循环识别文本，直到超时
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 3500)
            {
                // 识别木材数字模板
                var recognizedText = WoodTextAreaConfirm();
                // Logger.LogInformation("识别到的木材数量：{Text}", recognizedText);
                if (recognizedText > 0)
                {
                    stopwatch.Stop(); 
                    return recognizedText;
                }
            }
            
            Logger.LogWarning("木材数量识别超时");
            stopwatch.Stop(); // 停止计时
            return 0;
        }
        
        private int WoodTextAreaConfirm()
        {
            var woodCountDict = new Dictionary<int, int>(); 
            var woodTypeDict = new Dictionary<int, string>(); 
            var contentRegion = CaptureToRectArea();
            
            var thresholdNum = 0.6;
            var thresholdWood = 0.6;
            
            //保留后续可能有用
            if (_gameScreenSize.Width > 2560)
            {
                Logger.LogInformation("屏幕分辨率较高，推荐1080P，如识别失败请 {text}","尝试调低分辨率");
                thresholdNum = 0.6;
                thresholdWood = 0.6; 
            }else if (_gameScreenSize.Width > 1920)
            {
                Logger.LogInformation("屏幕分辨率较高，推荐1080P，如识别失败请 {text}","尝试调低分辨率");
                thresholdNum = 0.6;
                thresholdWood = 0.6; 
            }
            
            // 识别数量
             for (var i = 1; i < 6; i++)//行数
             { 
                 foreach (var woodNumber in WoodNumbers)
                 {
                     if (woodNumber == 1) thresholdNum = 0.7;// 1的识别阈值稍微高一点
                     var woodNumberStr = assert.InitializeWoodCountRecognitionObject(woodNumber, i,thresholdNum); // 初始化识别对象
                     var woodCountRo = contentRegion.Find(woodNumberStr); // 识别木材数量
                     if (!woodCountRo.IsEmpty())
                     {
                         woodCountDict[i] = woodNumber; // 将木材数量和行号关联起来
                         
                         // 识别木材类型
                         foreach (var wood in WoodNames)
                         {
                             var woodStr = assert.InitializeWoodCountRecognitionObjectM(wood.Value, i, thresholdWood);
                             var woodRo = contentRegion.Find(woodStr);
                             if (!woodRo.IsEmpty())
                             {
                                 woodTypeDict[i] = wood.Key; // 将木材类型和行号关联起来
                                 break;
                             }
                         }
                         
                         break;
                     }
                 }
                 
                 if (!woodCountDict.ContainsKey(i))
                 {
                     contentRegion = CaptureToRectArea();
                 }
             }
             
            // Logger.LogInformation("识别到的木材数量：{woodCountDict}", woodCountDict);
            // Logger.LogInformation("识别到的木材类型：{woodTypeDict}", woodTypeDict);
            
            var hasError = false; // 初始化标志位
            
           // 合并结果到字典中
            var resultDict = new Dictionary<string, int>();
            foreach (var kvp in woodTypeDict)
            {
                int woodTypeRow = kvp.Key;
                string woodType = kvp.Value;
                if (woodCountDict.ContainsKey(woodTypeRow) && !string.IsNullOrEmpty(woodType))
                {
                    if (resultDict.ContainsKey(woodType))
                    {
                        resultDict[woodType] += woodCountDict[woodTypeRow]; // 如果木材类型已经存在，累加数量
                    }
                    else
                    {
                        resultDict[woodType] = woodCountDict[woodTypeRow]; // 如果木材类型不存在，初始化数量
                    }
                }
                else
                {
                    hasError = true; // 设置错误标志位
                }
            }

            // 将当前结果合并到全局结果字典中
            foreach (var kvp in resultDict)
            {
                var woodType = kvp.Key;
                var count = kvp.Value;
                if (GlobalResultDict.ContainsKey(woodType))
                {
                    GlobalResultDict[woodType] += count; // 如果木材类型已经存在，累加数量
                }
                else
                {
                    GlobalResultDict[woodType] = count; // 如果木材类型不存在，初始化数量
                }
            }

            // 如果本次伐木不含指定木材，则错误计数器加1
            NothingWoodCount = resultDict.ContainsKey(_config.SingleWoodLimit) ? 0 : NothingWoodCount + 1;
        
            // 输出全局结果
            Logger.LogInformation("本次伐木统计数据：{resultDict}", resultDict);
            Logger.LogInformation("全局已累计木材类型和数量：{globalResultDict}", GlobalResultDict);
            
            contentRegion.Dispose();

            return hasError ? 0 : resultDict.Values.Sum();
        }
    }

   private async Task Felling(WoodTaskParam taskParam, bool isLast = false)
    {
        try
        {
            // 1. 按 z 触发「王树瑞佑」
            PressZ();

            // 打印伐木的统计数据（可选）
            if (TaskContext.Instance().Config.AutoWoodConfig.WoodCountOcrEnabled)
            {
                if (!await _printer.PrintWoodStatistics(taskParam))
                {
                    return;
                }
            }
            
            if (isLast)
            {
                return;
            }

            if (TaskContext.Instance().Config.AutoWoodConfig.UseWonderlandRefresh)
            {
                // 使用进出千星奇域刷新CD
                await _enterAndExitWonderlandJob.Start(_ct);
            }
            else
            {
                // 2. 按下 ESC 打开菜单 并退出游戏
                PressEsc(taskParam);

                // 3. 等待进入游戏
                EnterGame(taskParam);
            }

            // 手动 GC
            GC.Collect();
        }
        catch (Exception ex)
        {
            // 在这里处理异常，比如记录日志
            Console.WriteLine($"发生异常: {ex.Message}");
            throw new NormalEndException(ex.Message);
        }
    }

    private void PressZ()
    {
        // IMPORTANT: MUST try focus before press Z
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);

        if (_first)
        {
            var boon = false;
            for (var i = 0; i < 3; i++)
            {
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.TheBoonOfTheElderTreeRo);
                if (ra.IsEmpty())
                {
                    if (i == 2)break;
                    Logger.LogInformation("未找到「王树瑞佑」，尝试第{Cnt}次", i + 1);
                    FindBoon().Wait(20000);
                }
                else
                {
                    Logger.LogInformation("找到「王树瑞佑」,进行装备");
                    boon = true;
                    break;
                }
            }
            if (!boon)
            {
#if !TEST_WITHOUT_Z_ITEM
                throw new NormalEndException("请先装备小道具「王树瑞佑」！如果已经装备仍旧出现此提示，请重新仔细阅读文档中的《快速上手》！");
#else
                System.Threading.Thread.Sleep(2000);
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
#endif
            }
            else
            {
                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
            }
        }
        else
        {
            NewRetry.Do(() =>
            {
                Sleep(1, _ct);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.TheBoonOfTheElderTreeRo);
                if (ra.IsEmpty())
                {
#if !TEST_WITHOUT_Z_ITEM
                    throw new RetryException("未找到「王树瑞佑」");
#else
                    System.Threading.Thread.Sleep(15000);
#endif
                }

                Simulation.SendInput.SimulateAction(GIActions.QuickUseGadget);
                Debug.WriteLine("[AutoWood] Z");
                Sleep(500, _ct);
            }, TimeSpan.FromSeconds(1), 120);
        }

        Sleep(300, _ct);
        // Sleep(TaskContext.Instance().Config.AutoWoodConfig.AfterZSleepDelay, _ct);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
        Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        // if (TaskContext.Instance().Config.AutoWoodConfig.PressTwoEscEnabled)
        // {
        //     Sleep(1500, _cts);
        //     Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
        // }
        Debug.WriteLine("[AutoWood] Esc");
        Sleep(800, _ct);
        // 确认在菜单界面
        try
        {
            NewRetry.Do(() =>
            {
                Sleep(1, _ct);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.MenuBagRo);
                if (ra.IsEmpty())
                {
                    Simulation.SendInput.Keyboard.KeyPress(VK.VK_ESCAPE);
                    throw new RetryException("未检测到弹出菜单");
                }
            }, TimeSpan.FromSeconds(1.2), 5);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
            Logger.LogInformation("仍旧点击退出按钮");
        }

        // 点击退出
        GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale));

        Debug.WriteLine("[AutoWood] Click exit button");

        Sleep(500, _ct);

        // 点击退出到主界面确认
        using var contentRegion = CaptureToRectArea();
        contentRegion.Find(_assets.ConfirmRo, ra =>
        {
            ra.Click();
            Debug.WriteLine("[AutoWood] Click confirm button");
            ra.Dispose();
        });
    }

    private void EnterGame(WoodTaskParam taskParam)
    {
        if (_login3RdParty.IsAvailabled)
        {
            Sleep(1, _ct);
            _login3RdParty.Login(_ct);
        }

        var clickCnt = 0;
        for (var i = 0; i < 50; i++)
        {
            Sleep(1, _ct);

            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.EnterGameRo);
            if (!ra.IsEmpty())
            {
                clickCnt++;
                GameCaptureRegion.GameRegion1080PPosClick(960, 630);
                Debug.WriteLine("[AutoWood] Click entry");
            }
            else
            {
                if (clickCnt > 2)
                {
                    Sleep(5000, _ct);
                    break;
                }
            }

            Sleep(1000, _ct);
        }

        if (clickCnt == 0)
        {
            throw new RetryException("未检测进入游戏界面");
        }
    }

    private static RecognitionObject GetConfirmRa(params string[] targetText)
    {
        var screenArea = CaptureToRectArea();
        return RecognitionObject.OcrMatch(
            0,
            0,
            (int)(screenArea.Width),
            (int)(screenArea.Height * 0.3),
            targetText
        );
    }
    
    private async Task FindBoon()
    {
        await new ReturnMainUiTask().Start(_ct);
        //打开背包
        Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
         
        await NewRetry.WaitForElementAppear(
            GetConfirmRa("小道具"),
            () =>
            {
                using ( var ra = CaptureToRectArea())
                {
                    var boon = ra.Find(ElementAssets.Instance.BagGadgetUnchecked);
                    if (boon.IsExist())
                    {
                        boon.Click();
                    }
                }
            },
            _ct,
            6,
            500
        );

        if (!await NewRetry.WaitForElementAppear(
                GetConfirmRa("王树瑞佑"),
                () =>
                {
                    using (var ra = CaptureToRectArea())
                    {
                        var boon = ra.Find(_assets.BoonRo);
                        if (boon.IsExist())
                        {
                            boon.Click();
                        } 
                    }
                },
                _ct,
                6,
                500
            ))
        {
            await new ReturnMainUiTask().Start(_ct);
            return;
        }
        
        await NewRetry.WaitForElementDisappear(
            GetConfirmRa("小道具"),
            () =>
            {
                using (var ra = CaptureToRectArea())
                {
                    var confirm = ra.Find(ElementAssets.Instance.BtnWhiteConfirm);
                    if (confirm.IsExist())
                    {
                        confirm.Click();
                    }  
                }
            },
            _ct,
            6,
            500
        );

        await new ReturnMainUiTask().Start(_ct);
        await Delay(1000,_ct);

    }
    
}