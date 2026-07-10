using System;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 联机战斗策略文件打开 helper（设置页 + 配置组弹窗共享）。
/// <para>
/// 详见 .kiro/specs/multiplayer-hoeing-fixed-fight-strategy/design.md §3。
/// </para>
/// </summary>
public static class MultiplayerFightStrategyFileHelper
{
    private static readonly ILogger _logger =
        App.GetLogger<MultiplayerFightStrategyFileHelperLogCategory>();

    /// <summary>
    /// 联机战斗策略文件的固定绝对路径。
    /// </summary>
    public static string FixedFilePath => Global.Absolute(@"User\AutoFight\联机战斗策略.txt");

    /// <summary>
    /// 打开联机战斗策略文件供玩家编辑：
    /// 文件不存在则创建空文件（不写注释模板，避免干扰策略解析），
    /// 然后用 OS 默认关联程序打开。
    /// 任何异常通过 <see cref="Toast.Warning(string)"/> 反馈，不向上传播。
    /// </summary>
    public static void OpenForEdit()
    {
        var path = FixedFilePath;
        try
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }
            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[联机][策略] 打开联机战斗策略文件失败: {Path}", path);
            Toast.Warning("打开联机战斗策略文件失败：" + ex.Message);
        }
    }

    /// <summary>占位类型，仅用于获取 ILogger 的 category name。</summary>
    private sealed class MultiplayerFightStrategyFileHelperLogCategory { }
}
