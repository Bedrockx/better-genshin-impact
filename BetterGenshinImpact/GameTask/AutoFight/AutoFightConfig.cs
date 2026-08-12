using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoFight;





/// <summary>
/// 自动战斗配置
/// </summary>
[Serializable]
public partial class AutoFightConfig : ObservableObject
{
    [ObservableProperty] private string _strategyName = "根据队伍自动选择";

    /// <summary>
    /// 英文逗号分割 强制指定队伍角色
    /// </summary>
    [ObservableProperty] private string _teamNames = "";

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
        /// 快速检查战斗结束（默认关闭）：完成一轮动作后，如果满足条件，则触发一次战斗结束检查。
        /// </summary>
        [ObservableProperty]
        private bool _fastCheckEnabled = false;
        
        /// <summary>
        /// 旋转寻找敌人位置
        /// </summary>
        [ObservableProperty]
        private bool _rotateFindEnemyEnabled = false;
        
        /// <summary>
        /// 快速检查战斗结束的参数，填写数字（秒）时距离上次检查超过该时间则触发检查，填写人名时对应角色动作后触发检查。多项时使用分号分隔，格式如5或5;白术;
        /// </summary>
        [ObservableProperty]
        private string _fastCheckParams = "";
        
        /// <summary>
        /// 切人后再执行战斗结束检查：将触发战斗结束检查的时机调整为切人后，无需等待上一个动作后摇。目前仅 JSON 策略下生效。
        /// </summary>
        [ObservableProperty]
        private bool _checkAfterSwitchAvatar = false;
        
        /// <summary>
        /// 触发战斗结束检查时，先等待该延时以确保角色动作后摇结束。也可为角色单独指定延时，格式如0.4或0.4;钟离,1.5
        /// </summary>
        [ObservableProperty]
        private string _checkEndDelay = "0.4;钟离,1.4;";

        /// <summary>
        /// 按下切换队伍后去检查屏幕色块的延时，默认为0.45秒。若出现无法结束战斗可以适当提高这个值，比如0.75。但不要太大，确保这个延时不会真的把队伍配置界面切出来。
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
        /// 是有元素爆发前检查战斗结束
        /// </summary>
        [ObservableProperty]
        private bool _checkBeforeBurst = false;

        /// <summary>
        /// 敌人可见时跳过战斗结束检查：检测到敌人血条时跳过战斗结束检查。与旋转寻找敌人位置互斥。
        /// </summary>
        [ObservableProperty]
        private bool _skipFightEndCheckWhenEnemyVisible = false;

        /// <summary>
        /// 开战后一段时间阻断战斗结束检查（秒）：默认0不阻断；大于0时，开战后该时间内的战斗结束检查直接视为战斗未结束。
        /// </summary>
        [ObservableProperty]
        private double _blockCheckBeforeBattleSeconds = 0;

        /// <summary>
        /// 派蒙辅助检测：按L后当派蒙头像可见时提前跳出战斗结束检测
        /// </summary>
        [ObservableProperty]
        private bool _paimonEndCheckEnabled = false;

        /// <summary>
        /// 派蒙辅助检测延时（秒），默认为0.2秒
        /// </summary>
        [ObservableProperty]
        private double _paimonEndCheckDelay = 0.2;

        /// <summary>
        /// 与"敌人可见时跳过战斗结束检查"互斥：开启旋转寻找敌人时关闭跳过检查，
        /// 避免跳过分支不清零旋转计数导致战斗被误判结束。
        /// </summary>
        partial void OnRotateFindEnemyEnabledChanged(bool value)
        {
            if (value) SkipFightEndCheckWhenEnemyVisible = false;
        }

        /// <summary>
        /// 与"旋转寻找敌人位置"互斥：开启跳过战斗结束检查时关闭旋转寻找敌人。
        /// </summary>
        partial void OnSkipFightEndCheckWhenEnemyVisibleChanged(bool value)
        {
            if (value) RotateFindEnemyEnabled = false;
        }
    }
    /// <summary>
    /// 战斗结束相关配置
    /// </summary>   
    [ObservableProperty]
    private FightFinishDetectConfig _finishDetectConfig = new();
    
    /// <summary>
    /// 战斗结束后光柱扫描掉落物
    /// </summary>
    [ObservableProperty]
    private bool _pickDropsAfterFightEnabled = true;

    /// <summary>
    /// 战斗结束后光柱扫描掉落物的持续秒数
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
    private bool _qinDoublePickUp = false;
    
    [ObservableProperty]
    private string _guardianAvatar = string.Empty;
    
    [ObservableProperty]
    private bool _guardianCombatSkip = false;
    
    [ObservableProperty]
    private bool _skipModel = false;
    
    [ObservableProperty]
    private bool _guardianAvatarHold = false;
    
    [ObservableProperty]
    private bool _burstEnabled = false;
    
    /// <summary>
    /// 战斗结束后，如果不存在万叶，则切换至存在万叶的队伍（基于开启万叶拾取情况下）
    /// </summary>
    [ObservableProperty]
    private string _kazuhaPartyName = "";
    
    [ObservableProperty]
    private bool _swimmingEnabled = true;

    /// <summary>
    /// 基于经验值判断是否执行战后拾取（检测到精英怪经验值图标时才拾取）
    /// </summary>
    [ObservableProperty]
    private bool _expBasedPickupEnabled = false;

    /// <summary>
    /// 战斗超时，单位秒
    /// </summary>
    [ObservableProperty]
    private int _timeout = 120;

    /// <summary>
    /// 战斗中持续索敌：战斗过程中情况允许时持续尝试面朝敌人
    /// </summary>
    [ObservableProperty]
    private bool _enableCombatTargeting = false;

    /// <summary>
    /// 脱锁等待时间（秒）：敌人不可见时等待一定时间后开始旋转索敌
    /// </summary>
    [ObservableProperty]
    private double _lockLostWaitTime = 0.5;

    /// <summary>
    /// 启用恰斯卡特化逻辑：勾选后启用恰斯卡飞行索敌特化（长按 E 骑乘蓄力），
    /// 关闭后恰斯卡长按 E 走通用逻辑，且下方恰斯卡独有配置项不显示。默认开启
    /// </summary>
    [ObservableProperty]
    private bool _chascaSpecializationEnabled = true;

    /// <summary>
    /// 恰斯卡稳定时间（秒）：距离上一次事件（进入第二步/识别到伤害数字/旋转/子弹变化/喷射动画）
    /// 超过该时间仍无法识别到目标时，执行一次水平向右旋转索敌
    /// </summary>
    [ObservableProperty]
    private double _chascaStableTime = 0.5;

    /// <summary>
    /// 恰斯卡起飞后不旋转（秒）：传奇血条存在时，起飞后前该秒数不执行旋转索敌（开局观察期）。
    /// 默认 1 秒；设置为 0 时起飞后立即按稳定时间旋转
    /// </summary>
    [ObservableProperty]
    private double _chascaNoRotateBeforeSeconds = 1;

    /// <summary>
    /// 恰斯卡下压力度：水平旋转索敌时叠加的垂直下压系数（1=参考恰斯卡 charge 平均 x/y 比例）
    /// </summary>
    [ObservableProperty]
    private double _chascaPressStrength = 1;

    /// <summary>
    /// 恰斯卡初始旋转力度（像素/次）：飞行索敌首次水平旋转的水平位移量，之后根据实测旋转角度自适应校准
    /// </summary>
    [ObservableProperty]
    private double _chascaInitialRotateX = 1000;

    /// <summary>
    /// 恰斯卡子弹识别阈值（0-1）：子弹槽位元素特征评分需超过该值才判定为对应元素，否则视为空
    /// </summary>
    [ObservableProperty]
    private double _chascaBulletThreshold = 0.8;

    /// <summary>
    /// 恰斯卡序列槽数量（1-5）：保存的历史子弹序列数量，每帧识别结果与全部历史序列比较判定子弹是否变化，
    /// 序列变化时替换最旧的历史序列。默认 2（与旧版本一致）
    /// </summary>
    [ObservableProperty]
    private int _chascaSequenceSlotCount = 2;

    /// <summary>
    /// 恰斯卡平滑转动：勾选后取代原有"无血条连续25°/帧"与"传奇血条间歇50°大旋转"两种旋转，
    /// 无目标分支超过稳定时间后由独立异步循环持续小步旋转（间隔较小、角度较小，转速随视角-时间序列自适应调节）
    /// </summary>
    [ObservableProperty]
    private bool _chascaSmoothRotateEnabled = false;

    /// <summary>
    /// 恰斯卡平滑转动预期转速（度/秒）：平滑旋转时以视角-时间序列实测转速与该值比对，按比例自适应调节步进力度
    /// </summary>
    [ObservableProperty]
    private double _chascaSmoothRotateSpeed = 80;

    /// <summary>
    /// 恰斯卡单次旋转角度（度）：未勾选平滑转动时，传奇血条存在（有目标）时单次旋转该角度；
    /// 无目标（无血条连续旋转）时使用该值的一半。默认 50 度（无目标时 25 度，与旧版本一致）
    /// </summary>
    [ObservableProperty]
    private double _chascaRotateStepAngle = 50;

    /// <summary>
    /// 恰斯卡朝向目标转动力度X（水平系数）：血条/伤害数字可见时，朝目标偏移的水平力度系数。
    /// 默认 0.2625（桑多涅逻辑 0.35 的四分之三）
    /// </summary>
    [ObservableProperty]
    private double _chascaAimForceX = 0.2625;

    /// <summary>
    /// 恰斯卡朝向目标转动力度Y（垂直系数）：血条/伤害数字可见时，朝目标偏移的垂直力度系数。
    /// 默认 0.1875（桑多涅逻辑 0.25 的四分之三）
    /// </summary>
    [ObservableProperty]
    private double _chascaAimForceY = 0.1875;

    /// <summary>
    /// 恰斯卡子弹喷射下压力度（像素）：识别到子弹喷射时执行一次快速下压（垂直向下移动）的力度，
    /// 触发内置 1 秒冷却（硬编码）。默认 100
    /// </summary>
    [ObservableProperty]
    private double _chascaSprayPressForce = 100;

    /// <summary>
    /// 恰斯卡回转角度（度）：识别到子弹变化停止平滑旋转时回转的角度。默认 15
    /// </summary>
    [ObservableProperty]
    private double _chascaRollbackAngle = 15;

    /// <summary>
    /// 恰斯卡敌人下方检测触发帧数：平滑旋转期间，所有红色箭头持续处于屏幕正下方45度
    /// （正下±22.5度）范围内时计数加一，否则减一（下限0）；超过该值时清零并执行一次强力下压。
    /// 默认 20（默认配置 80°/s、50ms/帧 下约旋转 90 度）
    /// </summary>
    [ObservableProperty]
    private int _chascaDownArrowPressThreshold = 20;

    /// <summary>
    /// 索敌识别间隔（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _targetingDetectionInterval = 50;

    /// <summary>
    /// 伤害数字识别模式
    /// </summary>
    [ObservableProperty]
    private DamageNumberRecognitionMode _damageNumberRecognitionMode = DamageNumberRecognitionMode.Color;

    /// <summary>
    /// 绘制识别结果位置：在遮罩窗口上显示血条、伤害数字等识别结果的边框
    /// </summary>
    [ObservableProperty]
    private bool _drawRecognitionResults = true;

}

/// <summary>
/// 伤害数字识别模式
/// </summary>
public enum DamageNumberRecognitionMode
{
    Disabled,
    Ocr,
    Color
}
