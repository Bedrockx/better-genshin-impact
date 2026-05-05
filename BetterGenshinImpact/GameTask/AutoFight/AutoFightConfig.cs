using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 自动战斗配置
/// </summary>
[Serializable]
public partial class AutoFightConfig : ObservableObject
{
    [ObservableProperty] private string _strategyName = "根据队伍自动选择";
    
    /// <summary>
    /// 战斗策略国家列表（策略文件名检测）
    /// </summary>
    [ObservableProperty]
    private string[] _countryNamesList = { "自动", "挪德卡莱","纳塔", "枫丹", "须弥", "稻妻", "璃月", "蒙德", "精英", "小怪" };
    
    /// <summary>
    /// 自动战斗策略所属国家
    /// </summary>
    [ObservableProperty] private string?[] _countryName = ["自动"];

    /// <summary>
    /// 英文逗号分割 强制指定队伍角色
    /// </summary>
    [ObservableProperty] private string _teamNames = "";
    
    /// <summary> 战斗策略自动EQ </summary>
    [ObservableProperty] private bool _autoCombatEq = false;

    /// <summary>
    /// 检测战斗结束
    /// </summary>
    [ObservableProperty]
    private bool _fightFinishDetectEnabled = true;
    /// <summary>
    /// 根据技能CD优化出招人员
    /// 根据填入人或人和cd，来决定当此人元素战技cd未结束时，跳过此人出招，来优化战斗流程，可填入人名或人名数字（用逗号分隔），
    /// 多种用分号分隔，例如:白术;钟离,12;，如果人名，则用内置cd检查，如果是人名和数字，则把数字当做出招cd(秒)。
    /// </summary>
    [ObservableProperty] private string _actionSchedulerByCd = "";
    /// <summary>
    /// 只拾取精英掉落
    /// Closed ：关闭功能
    /// AllowAutoPickupForNonElite: 非精英允许自动拾取：战斗过程中掉落脚下的可以自动拾取，但不会执行万叶拾取和拾取配置逻辑。
    /// DisableAutoPickupForNonElite: 非精英关闭拾取：战斗过程中掉落到脚下的也不会自动拾取。
    /// </summary>
    [ObservableProperty] private string _onlyPickEliteDropsMode = "Closed";
    [Serializable]
    public partial class FightFinishDetectConfig : ObservableObject
    {
        /// <summary>
        /// 判断战斗结束读条颜色，不同帧率可能下会有些不同，默认为95,235,255
        /// </summary>
        [ObservableProperty]
        private string _battleEndProgressBarColor = "";

        /// <summary>
        /// 对于上方颜色地偏差值，即±某个值，例如 6或6,6,6，前者表示所有偏差值都一样，后者则可以分别设置
        /// </summary>
        [ObservableProperty]
        private string _battleEndProgressBarColorTolerance = "";
        
        
        /// <summary>
        /// 快速检查战斗结束，在一轮脚本中，可以每隔一定秒数（默认为5）或指定角色操作后，去检查（在每个角色完成该轮脚本时）。
        /// </summary>
        [ObservableProperty]
        private bool _fastCheckEnabled = false;
        
        /// <summary>
        /// 旋转寻找敌人位置
        /// </summary>
        [ObservableProperty]
        private bool _rotateFindEnemyEnabled = false;
        
        /// <summary>
        /// 快速检查战斗结束的参数，可填入数字和人名，多种用分号分隔，例如:15,白术;钟离;，如果是数字（小于等于0则不会根据时间去检查），则指定检查间隔，如果是人名，则该角色执行一轮操作后进行检查。同时每轮结束后检查不变。
        /// </summary>
        [ObservableProperty]
        private string _fastCheckParams = "";
        
        /// <summary>
        /// 检查战斗结束的延时，即角色，默认为1.5秒。也可以指定特定角色之后延时多少时间检查。格式如：2.5;白术,1.5;钟离,1.0;
        /// </summary>
        [ObservableProperty]
        private string _checkEndDelay = "0.4;钟离,1.4;";

        /// <summary>
        /// 按下切换队伍后去检查屏幕色块的延迟，默认为0.45秒。若频繁误判可以适当提高这个值。确保这个延迟不会真的把队伍配置界面切出来。
        /// </summary>
        [ObservableProperty]
        private string _beforeDetectDelay = "0.4";
        
        /// <summary>
        /// 旋转寻找敌人位置的旋转因子，默认为12（范围1-13），越大越快。
        /// </summary>
        [ObservableProperty]
        private int _rotaryFactor = 12;
        
        /// <summary>
        /// 是否是第一次检查和面敌。
        /// </summary>
        [ObservableProperty]
        private bool _isFirstCheck = false;
        
        /// <summary>
        /// GoDistance 寻敌移动距离
        /// </summary>
        [ObservableProperty]
        private int _goDistance = 500;
        
        /// <summary>
        /// 是有元素爆发前检查战斗结束
        /// </summary>
        [ObservableProperty]
        private bool _checkBeforeBurst = false;
        
        /// 旋转寻找敌人模式
        [ObservableProperty]
        private bool _rotationMode = true;
        
        //检查结束方式
        [ObservableProperty]
        private bool _endModel = true;
        
        //快速检查方式的延时，默认为0.15秒
        [ObservableProperty] 
        private double _fastCheckDelay = 0.1;
        
        [ObservableProperty]
        private int _retryDis = 0;

        //开战前等待时间，默认为3秒，确保引战
        [ObservableProperty] 
        private int _fightWaitNotEndTime = 0;

        //派蒙结束检查模式
        [ObservableProperty] 
        private bool _paimonEndModel = false;
        
        //派蒙模式下的二次检查
        [ObservableProperty]
        private bool _doubleEndEnbled = false;
        
        //二次检查的延时，默认为750毫秒
        [ObservableProperty]
        private int _doubleEndDelay = 750;
    }
    /// <summary>
    /// 战斗结束相关配置
    /// </summary>   
    [ObservableProperty]
    private FightFinishDetectConfig _finishDetectConfig = new();
    
    /// <summary>
    /// 检测战斗结束，默认为每轮脚本后检查
    /// </summary>
    [ObservableProperty]
    private bool _pickDropsAfterFightEnabled = false;
    /// <summary>
    /// 检测战斗结束，默认为每轮脚本后检查
    /// </summary>
    [ObservableProperty]
    private int _pickDropsAfterFightSeconds = 15;

    /// <summary>
    /// 拾取战斗人次阈值,当战斗人次小于一定次数，就结束战斗情况下，不触发拾取掉落物和万叶拾取后拾取，只有不小于2时才生效。
    /// </summary>
    [ObservableProperty]
    private int? _battleThresholdForLoot;
    /// <summary>
    /// 战斗结束后，如果存在枫原万叶，则使用该角色捡材料
    /// </summary>
    [ObservableProperty]
    private bool _kazuhaPickupEnabled = true;
    
    [ObservableProperty]
    private string _guardianAvatar = string.Empty;
    
    [ObservableProperty]
    private bool _guardianCombatSkip = false;
    
    [ObservableProperty]
    private bool _guardianAvatarHold = false;
     [ObservableProperty]
     private bool _burstEnabled = false;
     
     [ObservableProperty]
     private bool _expKazuhaPickup = false;
    
     [ObservableProperty]
     private bool _qinDoublePickUp = false;
     
    [ObservableProperty]
    private bool _swimmingEnabled = true;

    /// <summary>
    /// 战斗结束后，如果不存在万叶，则切换至存在万叶的队伍（基于开启万叶拾取情况下）
    /// </summary>
    [ObservableProperty]
    private string _kazuhaPartyName = "";

    /// <summary>
    /// 战斗超时，单位秒
    /// </summary>
    [ObservableProperty]
    private int _timeout = 120;
    
    [ObservableProperty]
    private bool _takeMedicineEnabled = false;
    
    [ObservableProperty]
    private int _medicineInterval = 1500;
    
    [ObservableProperty]
    private int _checkInterval = 200;
    
    [ObservableProperty]
    private int _recoverMaxCount = 5;
    
    [ObservableProperty]
    private bool _endBloodCheackEnabled = false;
    
    [ObservableProperty]
    private bool _qRecoverAvatar = false;
    
    [ObservableProperty]
    private string _useEqList = "1,2,3,4";
    
    [ObservableProperty]
    private string _useSkillList = "1,2,3,4";
    
    [ObservableProperty]
    private int _kazuhaTime = 1500;
}
