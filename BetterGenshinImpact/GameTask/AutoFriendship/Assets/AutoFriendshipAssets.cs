using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFriendship.Assets;

/// <summary>
/// 好感任务图像资源定义
/// </summary>
public class AutoFriendshipAssets : BaseAssets<AutoFriendshipAssets>
{
    /// <summary>
    /// 突发任务触发区域 - 通常在屏幕中上部
    /// </summary>
    public Rect TaskTriggerRect { get; private set; }

    /// <summary>
    /// 战斗结果检测区域 - 通常在屏幕中央
    /// </summary>
    public Rect CombatResultRect { get; private set; }

    /// <summary>
    /// 掉落检测区域 - 通常在屏幕中下部
    /// </summary>
    public Rect DropRect { get; private set; }

    /// <summary>
    /// 对话选项检测区域 - 通常在屏幕中下部
    /// </summary>
    public Rect DialogueRect { get; private set; }

    /// <summary>
    /// 经验值掉落区域
    /// </summary>
    public Rect ExpDropRect { get; private set; }

    /// <summary>
    /// 摩拉掉落区域
    /// </summary>
    public Rect MoraDropRect { get; private set; }

    /// <summary>
    /// 原住民检测区域
    /// </summary>
    public Rect NativeDetectionRect { get; private set; }

    private AutoFriendshipAssets() : base()
    {
        Initialization();
    }

    private void Initialization()
    {
        // 突发任务触发区域 - 屏幕中心偏上位置
        TaskTriggerRect = new Rect(
            (int)(CaptureRect.Width * 0.3),
            (int)(CaptureRect.Height * 0.2),
            (int)(CaptureRect.Width * 0.4),
            (int)(CaptureRect.Height * 0.3));

        // 战斗结果检测区域 - 屏幕中心
        CombatResultRect = new Rect(
            (int)(CaptureRect.Width * 0.2),
            (int)(CaptureRect.Height * 0.35),
            (int)(CaptureRect.Width * 0.6),
            (int)(CaptureRect.Height * 0.3));

        // 掉落检测区域 - 屏幕中下部
        DropRect = new Rect(
            (int)(CaptureRect.Width * 0.35),
            (int)(CaptureRect.Height * 0.45),
            (int)(CaptureRect.Width * 0.3),
            (int)(CaptureRect.Height * 0.25));

        // 对话选项检测区域 - 屏幕中下部
        DialogueRect = new Rect(
            (int)(CaptureRect.Width * 0.25),
            (int)(CaptureRect.Height * 0.4),
            (int)(CaptureRect.Width * 0.5),
            (int)(CaptureRect.Height * 0.35));

        // 经验值掉落区域 - 屏幕中下偏左
        ExpDropRect = new Rect(
            (int)(CaptureRect.Width * 0.1),
            (int)(CaptureRect.Height * 0.5),
            (int)(CaptureRect.Width * 0.2),
            (int)(CaptureRect.Height * 0.2));

        // 摩拉掉落区域 - 屏幕中下偏右
        MoraDropRect = new Rect(
            (int)(CaptureRect.Width * 0.7),
            (int)(CaptureRect.Height * 0.5),
            (int)(CaptureRect.Width * 0.2),
            (int)(CaptureRect.Height * 0.2));

        // 原住民检测区域 - 全屏
        NativeDetectionRect = new Rect(0, 0, CaptureRect.Width, CaptureRect.Height);
    }
}
