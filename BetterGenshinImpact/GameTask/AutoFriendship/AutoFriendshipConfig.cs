using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFriendship.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFriendship;

/// <summary>
/// 好感任务自动完成配置
/// </summary>
public partial class AutoFriendshipConfig : ObservableObject
{
    /// <summary>
    /// 执行次数（0表示无限）
    /// </summary>
    [ObservableProperty]
    private int _runTimes = 1;

    /// <summary>
    /// 敌人类型
    /// </summary>
    [ObservableProperty]
    private EnemyType _enemyType = EnemyType.Fatui;

    /// <summary>
    /// OCR 识别超时时间（秒）
    /// </summary>
    [ObservableProperty]
    private int _ocrTimeoutSeconds = 10;

    /// <summary>
    /// 战斗超时时间（秒）
    /// </summary>
    [ObservableProperty]
    private int _fightTimeoutSeconds = 60;

    /// <summary>
    /// 禁用拾取
    /// </summary>
    [ObservableProperty]
    private bool _disablePickup = false;

    /// <summary>
    /// 禁用异步战斗
    /// </summary>
    [ObservableProperty]
    private bool _disableAsyncFight = false;

    /// <summary>
    /// 清理丘丘人超时时间（秒），设为0则不清理，仅对盗宝团有效
    /// </summary>
    [ObservableProperty]
    private int _qiuQiuRenTimeoutSeconds = 10;

    /// <summary>
    /// 队伍名称（为空表示使用当前队伍）
    /// </summary>
    [ObservableProperty]
    private string _partyName = "";

    /// <summary>
    /// 使用千分号
    /// </summary>
    [ObservableProperty]
    private bool _use1000Stars = false;

    /// <summary>
    /// 循环直到没有经验值或摩拉
    /// </summary>
    [ObservableProperty]
    private bool _loopTillNoExpOrMora = false;

    /// <summary>
    /// 愚人众路径配置
    /// </summary>
    [ObservableProperty]
    private EnemyPathConfig _fatuiPathConfig = new();

    /// <summary>
    /// 盗宝团路径配置
    /// </summary>
    [ObservableProperty]
    private EnemyPathConfig _hilichurlBrigadePathConfig = new();

    /// <summary>
    /// 鳄鱼路径配置
    /// </summary>
    [ObservableProperty]
    private EnemyPathConfig _crocodilePathConfig = new();

    /// <summary>
    /// 蕈兽路径配置
    /// </summary>
    [ObservableProperty]
    private EnemyPathConfig _fungusPathConfig = new();

    /// <summary>
    /// 雷萤术士路径配置
    /// </summary>
    [ObservableProperty]
    private EnemyPathConfig _electroMagePathConfig = new();

    /// <summary>
    /// 愚人众 OCR 关键词
    /// </summary>
    [ObservableProperty]
    private OcrKeywords _fatuiOcrKeywords = OcrKeywords.GetDefault(EnemyType.Fatui);

    /// <summary>
    /// 盗宝团 OCR 关键词
    /// </summary>
    [ObservableProperty]
    private OcrKeywords _hilichurlBrigadeOcrKeywords = OcrKeywords.GetDefault(EnemyType.HilichurlBrigade);

    /// <summary>
    /// 鳄鱼 OCR 关键词
    /// </summary>
    [ObservableProperty]
    private OcrKeywords _crocodileOcrKeywords = OcrKeywords.GetDefault(EnemyType.Crocodile);

    /// <summary>
    /// 蕈兽 OCR 关键词
    /// </summary>
    [ObservableProperty]
    private OcrKeywords _fungusOcrKeywords = OcrKeywords.GetDefault(EnemyType.Fungus);

    /// <summary>
    /// 雷萤术士 OCR 关键词
    /// </summary>
    [ObservableProperty]
    private OcrKeywords _electroMageOcrKeywords = OcrKeywords.GetDefault(EnemyType.ElectroMage);

    /// <summary>
    /// 获取当前敌人类型的路径配置
    /// </summary>
    public EnemyPathConfig GetCurrentPathConfig()
    {
        return EnemyType switch
        {
            EnemyType.Fatui => FatuiPathConfig,
            EnemyType.HilichurlBrigade => HilichurlBrigadePathConfig,
            EnemyType.Crocodile => CrocodilePathConfig,
            EnemyType.Fungus => FungusPathConfig,
            EnemyType.ElectroMage => ElectroMagePathConfig,
            _ => new EnemyPathConfig()
        };
    }

    /// <summary>
    /// 获取当前敌人类型的 OCR 关键词
    /// </summary>
    public OcrKeywords GetCurrentOcrKeywords()
    {
        return EnemyType switch
        {
            EnemyType.Fatui => FatuiOcrKeywords,
            EnemyType.HilichurlBrigade => HilichurlBrigadeOcrKeywords,
            EnemyType.Crocodile => CrocodileOcrKeywords,
            EnemyType.Fungus => FungusOcrKeywords,
            EnemyType.ElectroMage => ElectroMageOcrKeywords,
            _ => new OcrKeywords()
        };
    }

    /// <summary>
    /// 获取敌人类型显示名称
    /// </summary>
    public static string GetEnemyTypeDisplayName(EnemyType type)
    {
        return type switch
        {
            EnemyType.Fatui => "愚人众",
            EnemyType.HilichurlBrigade => "盗宝团",
            EnemyType.Crocodile => "鳄鱼",
            EnemyType.Fungus => "蕈兽",
            EnemyType.ElectroMage => "雷萤术士",
            _ => "未知"
        };
    }
}
