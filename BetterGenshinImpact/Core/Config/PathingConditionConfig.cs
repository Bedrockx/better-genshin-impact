using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class PathingConditionConfig : ObservableObject
{
    [ObservableProperty]
    private string _mapMatchingMethod = "TemplateMatch";
    
    // 地图追踪条件配置
    [ObservableProperty]
    private ObservableCollection<Condition> _partyConditions = [];

    [ObservableProperty]
    private ObservableCollection<Condition> _avatarConditions = [];
    
    // 只在传送传送点时复活
    [ObservableProperty]
    private bool _onlyInTeleportRecover = false;

    // 低血量回复时机
    private RecoverTiming? _recoverTiming;

    public RecoverTiming RecoverTiming
    {
        get
        {
            if (_recoverTiming is null)
            {
                // 首次读取时从旧字段自动迁移
                _recoverTiming = RecoverTimingMigration.Migrate(_onlyInTeleportRecover);
            }
            return _recoverTiming.Value;
        }
        set => SetProperty(ref _recoverTiming, value);
    }

    // 使用小道具的间隔时间(ms)
    [ObservableProperty]
    private int _useGadgetIntervalMs = 0;
    
    // 启用自动吃药功能
    [ObservableProperty]
    private bool _autoEatEnabled = false;
    
    // 启用红血不吃药仅切人
    [ObservableProperty]
    private bool _redBloodSwitchOnly = false;
    
    // 锄地国家和类型
    [ObservableProperty] private static string?[] _countryName = ["自动"];
    
    // 自动吃药次数记录
    private static volatile int _autoEatCount = 0;
    public static int AutoEatCount
    {
        get => _autoEatCount;
        set => _autoEatCount = value;
    }

    // 本轮恢复"已放弃嗑小道具、去神像中"的抑制态（autoeat-count-overloaded-sentinel-fix spec）。
    // 与 AutoEatCount 计数职责分离：取代 Avatar.cs 原第 189 行的反语义清零 AutoEatCount=0。
    // 严禁与 AutoFightTask.IsTpForRecover（吃药作用域）/ IsTeleportingToStatue（回点循环 gate）混用。
    private static volatile bool _recoverSuppressed = false;
    public static bool RecoverSuppressed
    {
        get => _recoverSuppressed;
        set => _recoverSuppressed = value;
    }
    // 最后吃药时间记录
    private static DateTime _lastEatTime = DateTime.MinValue;
    public static DateTime LastEatTime
    {
        get => _lastEatTime;
        set => _lastEatTime = value;
    }
    
    //装配营养袋重试次数
    private static int _retryAssemblyNum = 3;
    public static int RetryAssemblyNum
    {
        get => _retryAssemblyNum;
        set => _retryAssemblyNum = value;
    }
    
    // 行走位备份
    private static string _initialMainAvatarIndex = string.Empty;
    public static string InitialMainAvatarIndex
    {
        get => _initialMainAvatarIndex;
        set => _initialMainAvatarIndex = value;
    }
    
    // 新增的私有字段和属性
    private static bool? _indexRectOffset60FixBackUp = null;
    public static bool? IndexRectOffset60FixBackUp
    {
        get => _indexRectOffset60FixBackUp;
        set => _indexRectOffset60FixBackUp = value;
    }
    
    private static CombatScenes? _combatScenesGoBackUp = null;
    public static CombatScenes? CombatScenesGoBackUp
    {
        get => _combatScenesGoBackUp;
        set => _combatScenesGoBackUp = value;
    }
    
    private static PathingPartyConfig? _partyConfigBackUp = new PathingPartyConfig();
    public static PathingPartyConfig PartyConfigBackUp
    {
        get => _partyConfigBackUp?? new PathingPartyConfig();
        set => _partyConfigBackUp = value;
    }

    /// <summary>
    /// 联机模式下房主同步的战斗超时值（秒）。
    /// 有值时 AutoFightHandler 使用此值覆盖 taskParams.Timeout，不修改原始配置。
    /// 联机结束后设为 null，单机模式下始终为 null。
    /// </summary>
    private static int? _multiplayerFightTimeoutOverride;
    public static int? MultiplayerFightTimeoutOverride
    {
        get => _multiplayerFightTimeoutOverride;
        set => _multiplayerFightTimeoutOverride = value;
    }

    /// <summary>
    /// 联机锄地是否强制使用固定的"联机战斗策略"文件（纯本地值，读本机 AutoHoeingConfig 的开关）。
    /// AutoHoeingTask 进入联机分支时设为本机 _config.MultiplayerUseFixedFightStrategy、finally 复位为默认 true。
    /// AutoFightHandler 仅在 MultiplayerFightTimeoutOverride.HasValue（联机锄地运行中）时读取此值。
    /// 默认 true = 保持现有"强制固定策略"行为。
    /// </summary>
    private static bool _multiplayerUseFixedFightStrategyOverride = true;
    public static bool MultiplayerUseFixedFightStrategyOverride
    {
        get => _multiplayerUseFixedFightStrategyOverride;
        set => _multiplayerUseFixedFightStrategyOverride = value;
    }

    /// <summary>
    /// 联机锄地世界人数交叉校验：返回协调器权威 (Available, PlayerCount, IsHost) 快照的委托。
    /// 由 RouteExecutionEngine 注入"读实时协调器值的 lambda"、AutoHoeingTask finally 清空。
    /// 为 null（单机/非锄地/未注入）时 DetectedMultiGameStatus 完全走纯视觉逻辑，零感知。
    /// 委托每次调用时实时读取协调器，绝非启动快照。
    /// 类型仅用 BCL（值元组 + Func），不引用任何 Coordinator 类型，避免 AutoFight→AutoHoeing 编译依赖。
    /// </summary>
    private static Func<(bool Available, int PlayerCount, bool IsHost)>? _authoritativePlayerCountProvider;
    public static Func<(bool Available, int PlayerCount, bool IsHost)>? AuthoritativePlayerCountProvider
    {
        get => _authoritativePlayerCountProvider;
        set => _authoritativePlayerCountProvider = value;
    }

    public static PathingConditionConfig Default => new()
    {
        AvatarConditions =
        [
            new Condition
            {
                Subject = "队伍中角色",
                Object = ["绮良良", "莱依拉", "茜特菈莉", "芭芭拉", "七七"],
                Result = "循环短E"
            },

            new Condition
            {
                Subject = "队伍中角色",
                Object = ["钟离"],
                Result = "循环长E"
            },

            new Condition
            {
                Subject = "队伍中角色",
                Object = ["迪希雅"],
                Result = "作为主要行走角色"
            }
        ]
    };

    /// <summary>
    /// 找出当前应该切换的队伍名称
    /// </summary>
    /// <param name="materialName">采集物名称</param>
    /// <param name="specialActions">特殊动作</param>
    /// <returns></returns>
    public string? FilterPartyName(string? materialName, List<string?> specialActions)
    {
        if (specialActions is { Count: > 0 })
        {
            // 特殊动作匹配队伍名
            foreach (var action in specialActions)
            {
                if (string.IsNullOrEmpty(action))
                {
                    continue;
                }

                if (!ConditionDefinitions.ActionCnDic.TryGetValue(action, out var actionCn))
                {
                    continue; // 不校验
                }

                var condition = PartyConditions.FirstOrDefault(c => c.Subject == "动作" && c.Object.Contains(actionCn));
                if (condition is { Result: not null })
                {
                    return condition.Result;
                }
            }
        }

        // 采集物匹配队伍名
        Condition? materialCondition = null;
        if (!string.IsNullOrEmpty(materialName))
        {
            materialCondition = PartyConditions.FirstOrDefault(c => c.Subject == "采集物" && c.Object.Contains(materialName));
        }
        if (materialCondition is { Result: not null })
        {
            return materialCondition.Result;
        }
        else
        {
            materialCondition = PartyConditions.FirstOrDefault(c => c.Subject == "采集物" && c.Object.Contains("全部"));
            if (materialCondition is { Result: not null })
            {
                return materialCondition.Result;
            }
        }

        return null;
    }

    /// <summary>
    /// 通过条件配置生成队伍配置
    /// </summary>
    /// <returns></returns>
    public PathingPartyConfig BuildPartyConfigByCondition(CombatScenes combatScenes)
    {
        PathingPartyConfig partyConfig = PathingPartyConfig.BuildDefault();
        // 使用最优先匹配上的条件
        foreach (var avatarCondition in AvatarConditions)
        {
            if (avatarCondition.Result == "循环短E" || avatarCondition.Result == "循环长E")
            {
                foreach (var avatar in combatScenes.GetAvatars())
                {
                    if (avatarCondition is { Object: not null } && avatarCondition.Object.Contains(avatar.Name))
                    {
                        partyConfig.GuardianAvatarIndex = avatar.Index.ToString();
                        if (avatarCondition.Result == "循环长E")
                        {
                            partyConfig.GuardianElementalSkillLongPress = true;
                            partyConfig.GuardianElementalSkillSecondInterval = avatar.CombatAvatar.SkillHoldCd.ToString(CultureInfo.CurrentCulture);
                        }
                        else
                        {
                            partyConfig.GuardianElementalSkillLongPress = false;
                            partyConfig.GuardianElementalSkillSecondInterval = avatar.CombatAvatar.SkillCd.ToString(CultureInfo.CurrentCulture);
                        }
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(partyConfig.GuardianAvatarIndex))
                {
                    break;
                }
            }
        }

        foreach (var avatarCondition in AvatarConditions)
        {
            if (avatarCondition.Result == "作为主要行走角色")
            {
                foreach (var avatar in combatScenes.GetAvatars())
                {
                    if (avatarCondition is not null && avatarCondition.Object.Contains(avatar.Name))
                    {
                        partyConfig.MainAvatarIndex = avatar.Index.ToString();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(partyConfig.MainAvatarIndex))
                {
                    break;
                }
            }
        }

        // 默认元素力采集角色
        // var elementalCollectAvatars = ElementalCollectAvatarConfigs.Lists;
        // foreach (var avatar in combatScenes.Avatars)
        // {
        //     var elementalCollectAvatar = elementalCollectAvatars.FirstOrDefault(x => x.Name == avatar.Name);
        //     if (elementalCollectAvatar != null)
        //     {
        //         if (elementalCollectAvatar.ElementalType == ElementalType.Hydro)
        //         {
        //             partyConfig.HydroCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //         else if (elementalCollectAvatar.ElementalType == ElementalType.Electro)
        //         {
        //             partyConfig.ElectroCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //         else if (elementalCollectAvatar.ElementalType == ElementalType.Anemo)
        //         {
        //             partyConfig.AnemoCollectAvatarIndex = avatar.Index.ToString();
        //         }
        //     }
        // }

        return partyConfig;
    }
    

    private static PathingConditionConfig _partyConfig = new PathingConditionConfig();

    public static PathingConditionConfig GetPartyConfig()
    {
        return _partyConfig;
    }
    
    public static void GetCountryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
    
        // 替换特定字符串
        path = path.Replace("莫酱", "小怪").Replace("汐酱", "精英");
        
        var config = TaskContext.Instance().Config.AutoFightConfig;
        var countryNamesList = config.CountryNamesList;
    
        var foundCountries = countryNamesList.Where(material => path.Contains(material)).ToArray();

        if (foundCountries.Length > 0)
        {
            var partyConfigInstance = GetPartyConfig();
            partyConfigInstance.CountryName = foundCountries;            
        }
    }
    
}
