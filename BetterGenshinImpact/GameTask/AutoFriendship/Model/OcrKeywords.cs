using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFriendship.Model;

/// <summary>
/// OCR 关键词配置
/// </summary>
public class OcrKeywords
{
    /// <summary>任务触发关键词</summary>
    public List<string> TaskTriggerKeywords { get; set; } = [];

    /// <summary>成功关键词</summary>
    public List<string> SuccessKeywords { get; set; } = [];

    /// <summary>失败关键词</summary>
    public List<string> FailureKeywords { get; set; } = [];

    /// <summary>离开区域关键词</summary>
    public List<string> LeftAreaKeywords { get; set; } = [];

    /// <summary>经验值关键词</summary>
    public List<string> ExpKeywords { get; set; } = [];

    /// <summary>摩拉关键词</summary>
    public List<string> MoraKeywords { get; set; } = [];

    /// <summary>
    /// 获取默认的关键词配置
    /// </summary>
    public static OcrKeywords GetDefault(EnemyType enemyType)
    {
        return enemyType switch
        {
            EnemyType.Fatui => new OcrKeywords
            {
                TaskTriggerKeywords = ["支援", "突发事件", "愚人众"],
                SuccessKeywords = ["完成", "成功", "领取"],
                FailureKeywords = ["失败", "未完成", "未领取"],
                LeftAreaKeywords = ["离开", "区域"],
                ExpKeywords = ["经验", "好感度"],
                MoraKeywords = ["摩拉", "奖励"]
            },
            EnemyType.HilichurlBrigade => new OcrKeywords
            {
                TaskTriggerKeywords = ["支援", "突发事件", "盗宝团"],
                SuccessKeywords = ["完成", "成功", "领取"],
                FailureKeywords = ["失败", "未完成", "未领取"],
                LeftAreaKeywords = ["离开", "区域"],
                ExpKeywords = ["经验", "好感度"],
                MoraKeywords = ["摩拉", "奖励"]
            },
            EnemyType.Crocodile => new OcrKeywords
            {
                TaskTriggerKeywords = ["支援", "突发事件", "鳄鱼"],
                SuccessKeywords = ["完成", "成功", "领取"],
                FailureKeywords = ["失败", "未完成", "未领取"],
                LeftAreaKeywords = ["离开", "区域"],
                ExpKeywords = ["经验", "好感度"],
                MoraKeywords = ["摩拉", "奖励"]
            },
            EnemyType.Fungus => new OcrKeywords
            {
                TaskTriggerKeywords = ["支援", "突发事件", "蕈兽"],
                SuccessKeywords = ["完成", "成功", "领取"],
                FailureKeywords = ["失败", "未完成", "未领取"],
                LeftAreaKeywords = ["离开", "区域"],
                ExpKeywords = ["经验", "好感度"],
                MoraKeywords = ["摩拉", "奖励"]
            },
            EnemyType.ElectroMage => new OcrKeywords
            {
                TaskTriggerKeywords = ["支援", "突发事件", "雷萤术士"],
                SuccessKeywords = ["完成", "成功", "领取"],
                FailureKeywords = ["失败", "未完成", "未领取"],
                LeftAreaKeywords = ["离开", "区域"],
                ExpKeywords = ["经验", "好感度"],
                MoraKeywords = ["摩拉", "奖励"]
            },
            _ => new OcrKeywords()
        };
    }
}
