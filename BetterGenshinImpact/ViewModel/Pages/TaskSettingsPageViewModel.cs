using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoCook;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFriendship;
using BetterGenshinImpact.GameTask.AutoFriendship.Model;
using BetterGenshinImpact.GameTask.AutoHoeing;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoWood;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.System;
using Wpf.Ui;
using Wpf.Ui.Violeta.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;
using BetterGenshinImpact.GameTask.AutoFriendship;



namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TaskSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;
    private readonly TaskTriggerDispatcher _taskDispatcher;

    private CancellationTokenSource? _cts;
    private static readonly object _locker = new();

    // [ObservableProperty]
    // private string[] _strategyList;

    [ObservableProperty]
    private bool _switchAutoGeniusInvokationEnabled;

    [ObservableProperty]
    private string _switchAutoGeniusInvokationButtonText = "启动";

    [ObservableProperty]
    private int _autoWoodRoundNum;

    [ObservableProperty]
    private int _autoWoodDailyMaxCount = 2000;

    [ObservableProperty]
    private bool _switchAutoWoodEnabled;

    [ObservableProperty]
    private string _switchAutoWoodButtonText = "启动";

    //[ObservableProperty]
    //private string[] _combatStrategyList;

    [ObservableProperty]
    private int _autoDomainRoundNum;

    [ObservableProperty]
    private bool _switchAutoDomainEnabled;

    [ObservableProperty]
    private string _switchAutoDomainButtonText = "启动";

    [ObservableProperty]
    private int _autoStygianOnslaughtRoundNum;

    [ObservableProperty]
    private bool _switchAutoStygianOnslaughtEnabled;

    [ObservableProperty]
    private string _switchAutoStygianOnslaughtButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoFightEnabled;

    [ObservableProperty]
    private string _switchAutoFightButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackButtonText = "启动";

    [ObservableProperty]
    private string _switchAutoTrackPathButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoMusicGameEnabled;

    [ObservableProperty]
    private string _switchAutoMusicGameButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoAlbumEnabled;

    [ObservableProperty]
    private string _switchAutoAlbumButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoCookEnabled;

    [ObservableProperty]
    private string _switchAutoCookButtonText = "启动";

    [ObservableProperty]
    private List<string> _domainNameList;

    public static List<string> ArtifactSalvageStarList = ["4", "3", "2", "1"];

    public static List<int> BossNumList = [1, 2, 3];

    public static List<string> AvatarIndexList { get; } = new List<string> { "", "1", "2", "3", "4" };
    public static List<string> LeyLineOutcropTypeList = ["启示之花", "藏金之花"];
    public static List<string> LeyLineOutcropCountryList = ["蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱"];

    [ObservableProperty]
    private List<string> _autoMusicLevelList = ["传说", "大师", "困难", "普通", "所有"];

    [ObservableProperty]
    private AutoFightViewModel? _autoFightViewModel;

    [ObservableProperty]
    private OneDragonFlowViewModel? _oneDragonFlowViewModel;

    [ObservableProperty]
    private bool _switchAutoFishingEnabled;

    [ObservableProperty]
    private string _switchAutoFishingButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoLeyLineOutcropEnabled;

    [ObservableProperty]
    private string _switchAutoLeyLineOutcropButtonText = "启动";

    [ObservableProperty]
    private bool _scanDropsAfterRewardEnabledUi;

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _fishingTimePolicyDict = Enum.GetValues(typeof(FishingTimePolicy))
        .Cast<FishingTimePolicy>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _enemyTypeDict = Enum.GetValues(typeof(EnemyType))
        .Cast<EnemyType>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => AutoFriendshipConfig.GetEnemyTypeDisplayName(e));

    private bool saveScreenshotOnKeyTick;
    private bool _suppressScanDropsAfterRewardPrompt;
    private int _scanDropsAfterRewardPromptVersion;
    public bool SaveScreenshotOnKeyTick
    {
        get => Config.CommonConfig.ScreenshotEnabled && saveScreenshotOnKeyTick;
        set => SetProperty(ref saveScreenshotOnKeyTick, value);
    }

    [ObservableProperty]
    private bool _switchArtifactSalvageEnabled;

    [ObservableProperty]
    private FrozenDictionary<Enum, string> _recognitionFailurePolicyDict = Enum.GetValues(typeof(RecognitionFailurePolicy))
        .Cast<RecognitionFailurePolicy>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    [ObservableProperty]
    private bool _switchGetGridIconsEnabled;
    [ObservableProperty]
    private string _switchGetGridIconsButtonText = "启动";
    [ObservableProperty]
    private FrozenDictionary<Enum, string> _gridNameDict = Enum.GetValues(typeof(GridScreenName))
        .Cast<GridScreenName>()
        .ToFrozenDictionary(
            e => (Enum)e,
            e => e.GetType()
                .GetField(e.ToString())?
                .GetCustomAttribute<DescriptionAttribute>()?
                .Description ?? e.ToString());

    [ObservableProperty]
    private string _switchGridIconsAccuracyTestButtonText = "运行模型准确率测试";

    [ObservableProperty]
    private bool _switchAutoRedeemCodeEnabled;

    [ObservableProperty]
    private string _switchAutoRedeemCodeButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoHoeingEnabled;

    [ObservableProperty]
    private string _switchAutoHoeingButtonText = "启动";

    [ObservableProperty]
    private bool _switchAutoFriendshipEnabled;

    [ObservableProperty]
    private string _switchAutoFriendshipButtonText = "启动";

    [ObservableProperty]
    private bool _hasRouteDiff;

    [ObservableProperty]
    private string _routeDiffMessage = "";

    [ObservableProperty]
    private string _multiplayerStatusText = "请先创建或加入房间";

    [ObservableProperty]
    private bool _canStartMultiplayer;

    [ObservableProperty]
    private bool _isRoomHost = true;

    /// <summary>
    /// 是否为房间成员（与 IsRoomHost 相反）
    /// </summary>
    public bool IsRoomMember => !IsRoomHost;

    /// <summary>
    /// 联机角色选择索引：0=房主，1=成员
    /// </summary>
    public int MultiplayerRoleIndex
    {
        get => Config.AutoHoeingConfig.MultiplayerRole == "member" ? 1 : 0;
        set
        {
            if (value == 0)
            {
                Config.AutoHoeingConfig.MultiplayerRole = "host";
                IsRoomHost = true;
            }
            else
            {
                Config.AutoHoeingConfig.MultiplayerRole = "member";
                IsRoomHost = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRoomMember));
        }
    }

    [ObservableProperty]
    private bool _canSkipPartyWait = false;

    [ObservableProperty]
    private bool _isWaitingForParty = false; // 是否正在等待组队（进入F2页面）

    [ObservableProperty]
    private string _skipPartyWaitHotkeyText = "快捷键：未配置（可在快捷键设置中配置）";

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models.PlayerInfo> _roomPlayers = new();

    [ObservableProperty]
    private string _roomPlayerCount = "0 人";

    [ObservableProperty]
    private string _roomPlayerSummary = "房间玩家 (0人)";

    [ObservableProperty]
    private string _currentRoomCode = "";

    private BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.CoordinatorClient? _coordinatorClient;

    /// <summary>
    /// 内置线路列表
    /// </summary>
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<BuiltinRouteViewModel> _builtinRoutes = new();

    /// <summary>
    /// 锄地一条龙是否可见（隐藏功能，点击独立任务标题10次解锁/锁定）
    /// </summary>
    [ObservableProperty]
    private bool _autoHoeingVisible = true;

    /// <summary>
    /// 好感任务自动完成是否可见
    /// </summary>
    [ObservableProperty]
    private bool _autoFriendshipVisible = true;

    /// <summary>
    /// 全局解锁状态，供配置组等其他地方查询
    /// </summary>
    public static bool AutoHoeingUnlocked { get; private set; }

    private int _autoHoeingUnlockClickCount;

    // /// <summary>
    // /// 独立任务标题点击计数，达到10次切换锄地一条龙的显示/隐藏状态
    // /// </summary>
    // [RelayCommand]
    // private void OnSoloTaskTitleClick()
    // {
    //     _autoHoeingUnlockClickCount++;
    //     if (_autoHoeingUnlockClickCount >= 10)
    //     {
    //         _autoHoeingUnlockClickCount = 0;
    //         var newState = !AutoHoeingVisible;
    //         AutoHoeingVisible = newState;
    //         AutoHoeingUnlocked = newState;
    //         Config.CommonConfig.AutoHoeingUnlocked = newState;
    //         Wpf.Ui.Violeta.Controls.Toast.Success(newState ? "锄地一条龙已解锁" : "锄地一条龙已锁定");
    //         WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "AutoHoeingUnlocked", !newState, newState));
    //     }
    // }

    public TaskSettingsPageViewModel(IConfigService configService, INavigationService navigationService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _taskDispatcher = taskTriggerDispatcher;
        NormalizeLeyLineOutcropType();
        _scanDropsAfterRewardEnabledUi = Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled;

        // 从持久化配置恢复锄地一条龙解锁状态
        // _autoHoeingVisible = Config.CommonConfig.AutoHoeingUnlocked;
        // AutoHoeingUnlocked = Config.CommonConfig.AutoHoeingUnlocked;
        _autoHoeingVisible = true;
        AutoHoeingUnlocked = true;

        //_strategyList = LoadCustomScript(Global.Absolute(@"User\AutoGeniusInvokation"));

        //_combatStrategyList = ["根据队伍自动选择", .. LoadCustomScript(Global.Absolute(@"User\AutoFight"))];

        _domainNameList = ["", .. MapLazyAssets.Instance.DomainNameList];
        _autoFightViewModel = new AutoFightViewModel(Config);
        _oneDragonFlowViewModel = new OneDragonFlowViewModel();

        // 初始化快捷键文本
        UpdateSkipPartyWaitHotkeyText();

        // 监听快捷键配置变化
        Config.HotKeyConfig.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(Config.HotKeyConfig.SkipPartyWaitHotkey))
            {
                UpdateSkipPartyWaitHotkeyText();
            }
        };

        // 监听锄地配置变化
        Config.AutoHoeingConfig.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(Config.AutoHoeingConfig.UseFixedDebugRoutes) ||
                e.PropertyName == nameof(Config.AutoHoeingConfig.FixedDebugRoutePath))
            {
                UpdateBuiltinRouteButtonStates();
            }
        };

        // 启动定时器检查等待状态
        StartWaitingStatusChecker();

        // 注册联机锄地快捷键消息
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "TriggerSkipPartyWait")
            {
                if (CanSkipPartyWait && SkipPartyWaitCommand.CanExecute(null))
                {
                    SkipPartyWaitCommand.Execute(null);
                }
            }
        });

        // 初始化内置线路
        InitializeBuiltinRoutes();
    }

    /// <summary>
    /// 初始化内置线路列表
    /// </summary>
    public void InitializeBuiltinRoutes()
    {
        var scanner = new BetterGenshinImpact.GameTask.AutoHoeing.Services.RouteDirectoryScanner();
        var folders = scanner.ScanBuiltinRoutes();

        BuiltinRoutes.Clear();
        foreach (var folder in folders)
        {
            BuiltinRoutes.Add(new BuiltinRouteViewModel
            {
                FolderName = folder.FolderName,
                FullPath = folder.FullPath,
                RouteCount = folder.RouteCount,
                IsSelected = false, // 初始状态不选中，后续通过UpdateBuiltinRouteButtonStates设置
                IsEnabled = false   // 初始状态不可用，后续通过UpdateBuiltinRouteButtonStates设置
            });
        }
        
        // 更新按钮状态
        UpdateBuiltinRouteButtonStates();
    }

    /// <summary>
    /// 选择内置线路
    /// </summary>
    [RelayCommand]
    private void SelectBuiltinRoute(BuiltinRouteViewModel route)
    {
        // 取消所有其他选择
        foreach (var r in BuiltinRoutes)
        {
            r.IsSelected = false;
        }

        // 选中当前路线
        route.IsSelected = true;
        Config.AutoHoeingConfig.SelectedBuiltinRoute = route.FolderName;
    }

    /// <summary>
    /// 处理调试路径输入框变化
    /// </summary>
    public void OnDebugRoutePathChanged()
    {
        UpdateBuiltinRouteButtonStates();
    }

    /// <summary>
    /// 更新内置线路按钮状态
    /// </summary>
    private void UpdateBuiltinRouteButtonStates()
    {
        var hasManualPath = !string.IsNullOrWhiteSpace(Config.AutoHoeingConfig.FixedDebugRoutePath);
        var useFixedDebugRoutes = Config.AutoHoeingConfig.UseFixedDebugRoutes;

        // 只有在启用固定调试线路且没有手动路径时，按钮才可用
        var buttonsEnabled = useFixedDebugRoutes && !hasManualPath;

        foreach (var route in BuiltinRoutes)
        {
            route.IsEnabled = buttonsEnabled;
        }

        // 手动路径清空且启用固定调试线路时恢复之前的选择
        if (buttonsEnabled && !string.IsNullOrWhiteSpace(Config.AutoHoeingConfig.SelectedBuiltinRoute))
        {
            var selectedRoute = BuiltinRoutes.FirstOrDefault(r => r.FolderName == Config.AutoHoeingConfig.SelectedBuiltinRoute);
            if (selectedRoute != null)
            {
                selectedRoute.IsSelected = true;
            }
        }
        
        // 如果手动路径非空或未启用固定调试线路，清除所有选择
        if (!buttonsEnabled)
        {
            foreach (var route in BuiltinRoutes)
            {
                route.IsSelected = false;
            }
        }
    }

    partial void OnScanDropsAfterRewardEnabledUiChanged(bool value)
    {
        if (_suppressScanDropsAfterRewardPrompt)
        {
            return;
        }

        if (!value)
        {
            Interlocked.Increment(ref _scanDropsAfterRewardPromptVersion);
            Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled = false;
            return;
        }

        var version = Interlocked.Increment(ref _scanDropsAfterRewardPromptVersion);
        _ = ConfirmScanDropsAfterRewardRiskAsync(version);
    }

    private async Task ConfirmScanDropsAfterRewardRiskAsync(int version)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "风险提示",
            Content = "开启“领取奖励后扫描掉落物光柱”后，角色会在领奖完成后主动移动拾取。部分地脉花点位或特定配队下，可能因为移动范围较大而卡住。\n\n如果你愿意接受这个风险，请继续开启；否则将保持关闭。",
            PrimaryButtonText = "接受风险并开启",
            CloseButtonText = "不接受，保持关闭",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var result = await messageBox.ShowDialogAsync();
        var accepted = result == Wpf.Ui.Controls.MessageBoxResult.Primary;

        if (version != _scanDropsAfterRewardPromptVersion)
        {
            return;
        }

        _suppressScanDropsAfterRewardPrompt = true;
        try
        {
            ScanDropsAfterRewardEnabledUi = accepted;
            Config.AutoLeyLineOutcropConfig.ScanDropsAfterRewardEnabled = accepted;
        }
        finally
        {
            _suppressScanDropsAfterRewardPrompt = false;
        }
    }

    private void NormalizeLeyLineOutcropType()
    {
        var type = Config.AutoLeyLineOutcropConfig.LeyLineOutcropType;
        if (type == "蓝花（经验书）")
        {
            Config.AutoLeyLineOutcropConfig.LeyLineOutcropType = "启示之花";
            return;
        }

        if (type == "黄花（摩拉）")
        {
            Config.AutoLeyLineOutcropConfig.LeyLineOutcropType = "藏金之花";
            return;
        }

        if (string.IsNullOrWhiteSpace(type) || !LeyLineOutcropTypeList.Contains(type))
        {
            Config.AutoLeyLineOutcropConfig.LeyLineOutcropType = LeyLineOutcropTypeList[0];
        }
    }


    [RelayCommand]
    private async Task OnSOneDragonFlow()
    {
        if (OneDragonFlowViewModel == null || OneDragonFlowViewModel.SelectedConfig == null)
        {
            OneDragonFlowViewModel.OnNavigatedTo();
            if (OneDragonFlowViewModel == null || OneDragonFlowViewModel.SelectedConfig == null)
            {
                Toast.Warning("未设置任务!");
                return;
            }
        }
        await OneDragonFlowViewModel.OnOneKeyExecute();
    }

    [RelayCommand]
    private async Task OnStopSoloTask()
    {
        CancellationContext.Instance.Cancel();
        SwitchAutoGeniusInvokationEnabled = false;
        SwitchAutoWoodEnabled = false;
        SwitchAutoDomainEnabled = false;
        SwitchAutoFightEnabled = false;
        SwitchAutoMusicGameEnabled = false;
        SwitchAutoAlbumEnabled = false;
        SwitchAutoCookEnabled = false;
        SwitchAutoFishingEnabled = false;
        SwitchAutoLeyLineOutcropEnabled = false;
        SwitchArtifactSalvageEnabled = false;
        SwitchAutoRedeemCodeEnabled = false;
        SwitchAutoStygianOnslaughtEnabled = false;
        SwitchGetGridIconsEnabled = false;
        await Task.Delay(800);
    }

    [RelayCommand]
    private void OnStrategyDropDownOpened(string type)
    {
        AutoFightViewModel?.OnStrategyDropDownOpened(type);
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public async Task OnSwitchAutoGeniusInvokation()
    {
        if (GetTcgStrategy(out var content))
        {
            return;
        }

        SwitchAutoGeniusInvokationEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoGeniusInvokationTask(new GeniusInvokationTaskParam(content)));
        SwitchAutoGeniusInvokationEnabled = false;
    }

    public bool GetTcgStrategy(out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(Config.AutoGeniusInvokationConfig.StrategyName))
        {
            Toast.Warning("请先选择策略");
            return true;
        }

        var path = Global.Absolute(@"User\AutoGeniusInvokation\" + Config.AutoGeniusInvokationConfig.StrategyName + ".txt");

        if (!File.Exists(path))
        {
            Toast.Error("策略文件不存在");
            return true;
        }

        content = File.ReadAllText(path);
        return false;
    }

    [RelayCommand]
    public async Task OnGoToAutoGeniusInvokationUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/tcg.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoWood()
    {
        SwitchAutoWoodEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoWoodTask(new WoodTaskParam(AutoWoodRoundNum, AutoWoodDailyMaxCount)));
        SwitchAutoWoodEnabled = false;
    }

    [RelayCommand]
    public async Task OnGoToAutoWoodUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/felling.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoFight()
    {
        if (GetFightStrategy(out var path))
        {
            return;
        }

        var param = new AutoFightParam(path, Config.AutoFightConfig);

        SwitchAutoFightEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFightTask(param));
        SwitchAutoFightEnabled = false;
    }

    [RelayCommand]
    public async Task OnGoToAutoFightUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoDomain()
    {
        if (GetFightStrategy(out var path))
        {
            return;
        }
        
        Config.AutoDomainEnable = true;
        SwitchAutoDomainEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoDomainTask(new AutoDomainParam(AutoDomainRoundNum, path)));
        SwitchAutoDomainEnabled = false;
        Config.AutoDomainEnable = false;
    }

    public bool GetFightStrategy(out string path)
    {
        return GetFightStrategy(Config.AutoFightConfig.StrategyName, out path);
    }

    public bool GetFightStrategy(string strategyName, out string path)
    {
        if (string.IsNullOrEmpty(strategyName))
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("请先在下拉列表配置中选择战斗策略！"); });
            path = string.Empty;
            return true;
        }

        path = Global.Absolute(@"User\AutoFight\" + strategyName + ".txt");
        if ("根据队伍自动选择".Equals(strategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            UIDispatcherHelper.Invoke(() => { Toast.Error("当前选择的自动战斗策略文件不存在"); });
            return true;
        }

        return false;
    }

    [RelayCommand]
    public async Task OnGoToAutoDomainUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/domain.html"));
    }

    [RelayCommand]
    public async Task OnSwitchAutoHoeing()
    {
        SwitchAutoHoeingEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoHoeingTask());
        SwitchAutoHoeingEnabled = false;
    }

    [RelayCommand]
    public async Task OnSwitchAutoFriendship()
    {
        SwitchAutoFriendshipEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFriendshipTask(Config.AutoFriendshipConfig, null, null, Config.AutoFightConfig));
        SwitchAutoFriendshipEnabled = false;
    }

    [RelayCommand]
    private async Task OnCreateRoom()
    {
        var config = Config.AutoHoeingConfig;
        if (string.IsNullOrEmpty(config.CoordinatorServerUrl))
        {
            Toast.Warning("请先填写服务器地址");
            return;
        }
        // 清理旧连接
        UnsubscribeClientEvents();
        if (_coordinatorClient != null)
            await _coordinatorClient.DisposeAsync();

        var client = new BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.CoordinatorClient();
        var connected = await client.ConnectAsync(config.CoordinatorServerUrl, CancellationToken.None);
        if (!connected)
        {
            Toast.Error("连接服务器失败，请检查服务器地址");
            return;
        }
        var whitelist = string.IsNullOrEmpty(config.RoomWhitelist)
            ? null
            : new System.Collections.Generic.List<string>(config.RoomWhitelist.Split(new[] { ',', '，' }, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries));
        var roomCode = await client.CreateRoomAsync(config.PlayerName, whitelist, config.PlayerUid, config.ExpectedPlayerCount);
        if (roomCode != null)
        {
            _coordinatorClient = client;
            SubscribeClientEvents(client);
            config.CurrentRoomCode = roomCode;
            CurrentRoomCode = roomCode;
            MultiplayerStatusText = $"房间已创建：{roomCode}";
            CanStartMultiplayer = true;
            IsRoomHost = true;
            CanSkipPartyWait = true;
            RoomPlayers.Clear();
            RoomPlayers.Add(new BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models.PlayerInfo
            {
                PlayerName = string.IsNullOrEmpty(config.PlayerName) ? "房主" : config.PlayerName
            });
            RoomPlayerCount = "1 人";
            var myName = string.IsNullOrEmpty(config.PlayerName) ? "房主" : config.PlayerName;
            RoomPlayerSummary = $"房间玩家 (1人): {myName}";
            Toast.Success($"房间创建成功，房间码：{roomCode}");
        }
    }

    [RelayCommand]
    private async Task OnJoinRoom()
    {
        var config = Config.AutoHoeingConfig;
        if (string.IsNullOrEmpty(config.CurrentRoomCode))
        {
            Toast.Warning("请先输入房间码");
            return;
        }
        if (string.IsNullOrEmpty(config.CoordinatorServerUrl))
        {
            Toast.Warning("请先填写服务器地址");
            return;
        }
        // 清理旧连接
        UnsubscribeClientEvents();
        if (_coordinatorClient != null)
            await _coordinatorClient.DisposeAsync();

        var client = new BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.CoordinatorClient();
        var connected = await client.ConnectAsync(config.CoordinatorServerUrl, CancellationToken.None);
        if (!connected)
        {
            Toast.Error("连接服务器失败，请检查服务器地址");
            return;
        }
        var success = await client.JoinRoomAsync(config.CurrentRoomCode, config.PlayerName, config.PlayerUid);
        if (success)
        {
            _coordinatorClient = client;
            SubscribeClientEvents(client);
            CurrentRoomCode = config.CurrentRoomCode;
            MultiplayerStatusText = $"已加入房间：{config.CurrentRoomCode}";
            CanStartMultiplayer = true;
            IsRoomHost = false;
            CanSkipPartyWait = false;
            RoomPlayerCount = "加入成功";
            Toast.Success($"成功加入房间 {config.CurrentRoomCode}");
        }
        else
        {
            Toast.Error("加入房间失败，请检查房间码");
        }
    }

    [RelayCommand]
    private async Task OnStartMultiplayerHoeing()
    {
        SwitchAutoHoeingEnabled = true;
        await new TaskRunner().RunSoloTaskAsync(new AutoHoeingTask());
        SwitchAutoHoeingEnabled = false;
    }

    [RelayCommand]
    private void OnSkipPartyWait()
    {
        if (!BetterGenshinImpact.GameTask.AutoHoeing.AutoHoeingTask.IsWaitingForParty)
        {
            Toast.Warning("当前未在等待组队状态");
            return;
        }
        BetterGenshinImpact.GameTask.AutoHoeing.AutoHoeingTask.SkipPartyWait = true;
        Toast.Success("已发送立即开始信号");
    }

    private void UpdateSkipPartyWaitHotkeyText()
    {
        var hotkey = Config.HotKeyConfig.SkipPartyWaitHotkey;
        if (string.IsNullOrEmpty(hotkey))
        {
            SkipPartyWaitHotkeyText = "快捷键：未配置（可在快捷键设置中配置）";
        }
        else
        {
            SkipPartyWaitHotkeyText = $"快捷键：{hotkey}";
        }
    }

    private System.Threading.Timer? _waitingStatusTimer;

    private void StartWaitingStatusChecker()
    {
        _waitingStatusTimer = new System.Threading.Timer(_ =>
        {
            var isWaiting = BetterGenshinImpact.GameTask.AutoHoeing.AutoHoeingTask.IsWaitingForParty;
            if (IsWaitingForParty != isWaiting)
            {
                UIDispatcherHelper.Invoke(() =>
                {
                    IsWaitingForParty = isWaiting;
                    CanSkipPartyWait = IsRoomHost && isWaiting;
                });
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    [RelayCommand]
    private async Task OnCloseRoom()
    {
        var config = Config.AutoHoeingConfig;
        if (string.IsNullOrEmpty(config.CurrentRoomCode) && _coordinatorClient == null)
        {
            Toast.Warning("当前没有房间");
            return;
        }
        if (_coordinatorClient != null)
        {
            UnsubscribeClientEvents();
            await _coordinatorClient.CloseRoomAsync();
            await _coordinatorClient.DisposeAsync();
            _coordinatorClient = null;
        }
        config.CurrentRoomCode = "";
        CurrentRoomCode = "";
        CanStartMultiplayer = false;
        IsRoomHost = true;
        CanSkipPartyWait = false;
        MultiplayerStatusText = "房间已关闭";
        RoomPlayers.Clear();
        RoomPlayerCount = "0 人";
        RoomPlayerSummary = "房间玩家 (0人)";
        Toast.Success("房间已关闭");
    }

    [RelayCommand]
    private void OnCopyRoomCode()
    {
        var code = CurrentRoomCode;
        if (string.IsNullOrEmpty(code))
            code = Config.AutoHoeingConfig.CurrentRoomCode;
        if (string.IsNullOrEmpty(code))
        {
            Toast.Warning("房间码为空");
            return;
        }
        // 剪贴板可能被其他进程占用，重试 3 次
        for (int i = 0; i < 3; i++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(code, true);
                Toast.Success($"已复制房间码：{code}");
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
        Toast.Warning("复制失败，剪贴板被占用，请手动复制");
    }
    
    [RelayCommand]
    private void Openutorial()
    {
        var tutorialPath = Path.Combine(AppContext.BaseDirectory, "联机锄地使用教程.md");
        if (File.Exists(tutorialPath))
        {
            Process.Start(new ProcessStartInfo(tutorialPath) { UseShellExecute = true });
        }
        else
        {
            Toast.Warning("未找到文件");
        }
    }

    [RelayCommand]
    private async Task OnBrowseRooms()
    {
        var config = Config.AutoHoeingConfig;
        if (string.IsNullOrEmpty(config.CoordinatorServerUrl))
        {
            Toast.Warning("请先填写服务器地址");
            return;
        }
        var client = new BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.CoordinatorClient();
        var connected = await client.ConnectAsync(config.CoordinatorServerUrl, CancellationToken.None);
        if (!connected)
        {
            Toast.Error("连接服务器失败");
            return;
        }
        try
        {
            var rooms = await client.GetOnlineRoomsAsync();
            var dialog = new BetterGenshinImpact.View.Dialogs.RoomBrowserDialog(rooms, async () =>
            {
                return await client.GetOnlineRoomsAsync();
            });
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();

            if (!string.IsNullOrEmpty(dialog.SelectedRoomCode))
            {
                config.CurrentRoomCode = dialog.SelectedRoomCode;

                // 清理旧连接，复用浏览器的连接加入房间
                UnsubscribeClientEvents();
                if (_coordinatorClient != null)
                    await _coordinatorClient.DisposeAsync();

                var success = await client.JoinRoomAsync(dialog.SelectedRoomCode, config.PlayerName, config.PlayerUid);
                if (success)
                {
                    _coordinatorClient = client;
                    SubscribeClientEvents(client);
                    CurrentRoomCode = dialog.SelectedRoomCode;
                    MultiplayerStatusText = $"已加入房间：{dialog.SelectedRoomCode}";
                    CanStartMultiplayer = true;
                    IsRoomHost = false;
                    CanSkipPartyWait = false;
                    RoomPlayerCount = "加入成功";
                    Toast.Success($"成功加入房间 {dialog.SelectedRoomCode}");
                    return; // 不 dispose client，已保存
                }
                else
                {
                    Toast.Error("加入房间失败");
                }
            }
        }
        finally
        {
            if (_coordinatorClient != client) // 只在未保存时 dispose
                await client.DisposeAsync();
        }
    }

    private void OnPlayerListUpdated(System.Collections.Generic.List<BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models.PlayerInfo> players)
    {
        UIDispatcherHelper.Invoke(() =>
        {
            RoomPlayers.Clear();
            foreach (var p in players)
                RoomPlayers.Add(p);
            RoomPlayerCount = $"{players.Count} 人";
            RoomPlayerSummary = players.Count == 0
                ? "房间玩家 (0人)"
                : $"房间玩家 ({players.Count}人): {string.Join(", ", players.Select(p => p.PlayerName))}";
        });
    }

    private void SubscribeClientEvents(BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.CoordinatorClient client)
    {
        client.PlayerListUpdated += OnPlayerListUpdated;
    }

    private void UnsubscribeClientEvents()
    {
        if (_coordinatorClient != null)
            _coordinatorClient.PlayerListUpdated -= OnPlayerListUpdated;
    }

    [RelayCommand]
    private async Task OnSwitchAutoStygianOnslaught()
    {
        if (GetFightStrategy(Config.AutoStygianOnslaughtConfig.StrategyName, out var path))
        {
            return;
        }

        SwitchAutoStygianOnslaughtEnabled = true;
        AutoStygianOnslaughtParam param = new AutoStygianOnslaughtParam();
        param.SetAutoStygianOnslaughtConfig(Config.AutoStygianOnslaughtConfig);
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoStygianOnslaughtTask(param, path));
        SwitchAutoStygianOnslaughtEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoStygianOnslaughtUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/stygian.html"));
    }

    [RelayCommand]
    public async Task OnGoToAutoLeyLineOutcropUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/leyline.html"));
    }


    [RelayCommand]
    public void OnOpenFightFolder()
    {
        AutoFightViewModel?.OnOpenFightFolder();
    }

    [Obsolete]
    [RelayCommand]
    public void OnSwitchAutoTrack()
    {
        // try
        // {
        //     lock (_locker)
        //     {
        //         if (SwitchAutoTrackButtonText == "启动")
        //         {
        //             _cts?.Cancel();
        //             _cts = new CancellationTokenSource();
        //             var param = new AutoTrackParam(_cts);
        //             _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoTrack, param);
        //             SwitchAutoTrackButtonText = "停止";
        //         }
        //         else
        //         {
        //             _cts?.Cancel();
        //             SwitchAutoTrackButtonText = "启动";
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     ThemedMessageBox.Error(ex.Message);
        // }
    }

    [RelayCommand]
    public async Task OnGoToAutoTrackUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/track.html"));
    }

    [Obsolete]
    [RelayCommand]
    public void OnSwitchAutoTrackPath()
    {
        // try
        // {
        //     lock (_locker)
        //     {
        //         if (SwitchAutoTrackPathButtonText == "启动")
        //         {
        //             _cts?.Cancel();
        //             _cts = new CancellationTokenSource();
        //             var param = new AutoTrackPathParam(_cts);
        //             _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoTrackPath, param);
        //             SwitchAutoTrackPathButtonText = "停止";
        //         }
        //         else
        //         {
        //             _cts?.Cancel();
        //             SwitchAutoTrackPathButtonText = "启动";
        //         }
        //     }
        // }
        // catch (Exception ex)
        // {
        //     ThemedMessageBox.Error(ex.Message);
        // }
    }

    [RelayCommand]
    private async Task OnGoToAutoTrackPathUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/track.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoMusicGame()
    {
        SwitchAutoMusicGameEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoMusicGameTask(new AutoMusicGameParam()));
        SwitchAutoMusicGameEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoMusicGameUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/music.html"));
    }

    [RelayCommand]
    private async Task OnSwitchAutoAlbum()
    {
        SwitchAutoAlbumEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoAlbumTask(new AutoMusicGameParam()));
        SwitchAutoAlbumEnabled = false;
    }

    [RelayCommand]
    private async Task OnSwitchAutoCook()
    {
        SwitchAutoCookEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoCookTask());
        SwitchAutoCookEnabled = false;
    }

    [RelayCommand]
    private async Task OnSwitchAutoFishing()
    {
        SwitchAutoFishingEnabled = true;
        var param = AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig, SaveScreenshotOnKeyTick);
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoFishingTask(param));
        SwitchAutoFishingEnabled = false;
    }

    [RelayCommand]
    private async Task OnSwitchAutoLeyLineOutcrop()
    {
        SwitchAutoLeyLineOutcropEnabled = true;
        AutoLeyLineOutcropParam autoLeyLineOutcropParam = new AutoLeyLineOutcropParam();
        autoLeyLineOutcropParam.SetAutoLeyLineOutcropConfig(Config.AutoLeyLineOutcropConfig);
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoLeyLineOutcropTask(autoLeyLineOutcropParam));
        SwitchAutoLeyLineOutcropEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToAutoFishingUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/fish.html"));
    }

    [RelayCommand]
    private async Task OnGoToTorchPreviousVersionsAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://pytorch.org/get-started/previous-versions"));
    }

    [RelayCommand]
    private void OnOpenLocalScriptRepo()
    {
        AutoFightViewModel?.OnOpenLocalScriptRepo();
    }

    [RelayCommand]
    private async Task OnSwitchArtifactSalvage()
    {
        SwitchArtifactSalvageEnabled = true;
        await new TaskRunner()
            .RunSoloTaskAsync(new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(
                int.Parse(Config.AutoArtifactSalvageConfig.MaxArtifactStar),
                Config.AutoArtifactSalvageConfig.JavaScript,
                Config.AutoArtifactSalvageConfig.ArtifactSetFilter,
                Config.AutoArtifactSalvageConfig.MaxNumToCheck,
                Config.AutoArtifactSalvageConfig.RecognitionFailurePolicy
                )));
        SwitchArtifactSalvageEnabled = false;
    }

    [RelayCommand]
    private async Task OnGoToArtifactSalvageUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/feats/task/artifactSalvage.html"));
    }

    [RelayCommand]
    private async Task OnOpenArtifactSalvageTestOCRWindow()
    {
        ArtifactOcrDialog ocrDialog = new ArtifactOcrDialog(0.70, 0.112, 0.275, 0.50, "圣遗物分解", this.Config.AutoArtifactSalvageConfig.JavaScript);
        if (await ocrDialog.CaptureAsync()) { ocrDialog.ShowDialog(); }
    }

    [RelayCommand]
    private async Task OnCopyArtifactSalvageJavaScriptFromRepository()
    {
        var list = ScriptControlViewModel.LoadAllJsScriptProjects();
        var stackPanel = ScriptControlViewModel.CreateJsScriptSelectionPanel(list, typeof(RadioButton));

        var result = PromptDialog.Prompt("请选择需要复制的JS脚本", "请选择需要复制的JS脚本", stackPanel, new Size(500, 600));
        if (!string.IsNullOrEmpty(result))
        {
            string? selectedFolderName = null;
            foreach (var child in ((Wpf.Ui.Controls.StackPanel)stackPanel.Content).Children)
            {
                if (child is RadioButton { IsChecked: true } radioButton && radioButton.Tag is string folderName)
                {
                    selectedFolderName = folderName;
                }
            }
            if (selectedFolderName == null)
            {
                return;
            }

            ScriptProject scriptProject = new ScriptProject(selectedFolderName);
            string jsCode = await scriptProject.LoadCode();

            var multilineTextBox = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = jsCode,
                IsReadOnly = true
            };
            var p = new PromptDialog($"{scriptProject.Manifest.Name}\r\n{scriptProject.Manifest.ShortDescription}\r\n\r\n将覆盖现有的JavaScript，是否继续？", $"预览 - {scriptProject.FolderName}", multilineTextBox, null);
            p.Height = 600;
            p.MaxWidth = 800;
            p.ShowDialog();

            if (p.DialogResult != true)
            {
                return;
            }

            this.Config.AutoArtifactSalvageConfig.JavaScript = jsCode;
        }
    }

    [RelayCommand]
    private async Task OnSwitchGetGridIcons()
    {
        try
        {
            SwitchGetGridIconsEnabled = true;
            await new TaskRunner().RunSoloTaskAsync(new GetGridIconsTask(Config.GetGridIconsConfig.GridName, Config.GetGridIconsConfig.StarAsSuffix, Config.GetGridIconsConfig.MaxNumToGet));
        }
        finally
        {
            SwitchGetGridIconsEnabled = false;
        }
    }

    [RelayCommand]
    private void OnGoToGetGridIconsFolder()
    {
        var path = Global.Absolute(@"log\gridIcons\");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start("explorer.exe", path);
    }

    [RelayCommand]
    private async Task OnGoToGetGridIconsUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/dev/getGridIcons.html"));
    }

    [RelayCommand]
    private async Task OnSwitchGridIconsModelAccuracyTest()
    {
        try
        {
            SwitchGetGridIconsEnabled = true;
            await new TaskRunner().RunSoloTaskAsync(new GridIconsAccuracyTestTask(Config.GetGridIconsConfig.GridName, Config.GetGridIconsConfig.MaxNumToGet));
        }
        finally
        {
            SwitchGetGridIconsEnabled = false;
        }
    }

    [RelayCommand]
    private async Task OnSwitchAutoRedeemCode()
    {
        var multilineTextBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "请在此输入兑换码，每行一条记录"
        };
        var p = new PromptDialog(
            "输入兑换码",
            "自动使用兑换码",
            multilineTextBox,
            null);
        p.Height = 500;
        p.ShowDialog();
        if (p.DialogResult == true && !string.IsNullOrWhiteSpace(multilineTextBox.Text))
        {
            char[] separators = ['\r', '\n'];
            var codes = multilineTextBox.Text.Split(separators, StringSplitOptions.RemoveEmptyEntries)

           .Select(code => code.Trim())
           .Where(code => !string.IsNullOrEmpty(code))
           .ToList();

            if (codes.Count == 0)
            {
                Toast.Warning("没有有效的兑换码");
                return;
            }

            SwitchAutoRedeemCodeEnabled = true;
            await new TaskRunner()
                .RunSoloTaskAsync(new UseRedemptionCodeTask(codes));
            SwitchAutoRedeemCodeEnabled = false;
        }
    }

    /// <summary>
    /// 重置兑换码每日检查状态（镜像自 FeedWindowViewModel.ResetCheckStatus）。
    /// 行为：清空 AutoRedeemCodeConfig.LastRedeemCodeCheckDates 字典，下次一条龙启动会重新触发检查。
    /// 与 FeedWindowViewModel.ResetCheckStatus 完全等价但不抽公共方法（避免跨 VM 耦合）。
    /// </summary>
    [RelayCommand]
    private void ResetRedeemCodeCheckStatus()
    {
        TaskContext.Instance().Config.AutoRedeemCodeConfig.LastRedeemCodeCheckDates.Clear();
        Toast.Success("已重置检查状态，下次一条龙将重新检查兑换码");
    }
}

/// <summary>
/// 内置线路 UI 视图模型
/// </summary>
public partial class BuiltinRouteViewModel : ObservableObject
{
    /// <summary>
    /// 文件夹名称
    /// </summary>
    [ObservableProperty]
    private string _folderName = "";

    /// <summary>
    /// 文件夹完整路径
    /// </summary>
    [ObservableProperty]
    private string _fullPath = "";

    /// <summary>
    /// 路线文件数量
    /// </summary>
    [ObservableProperty]
    private int _routeCount;

    /// <summary>
    /// 是否被选中
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 是否启用（手动路径非空时禁用）
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;
}
