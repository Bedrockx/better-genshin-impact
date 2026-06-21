using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace BetterGenshinImpact.GameTask.AutoSwitchRoles;

/// <summary>
/// 配对界面切换角色任务的资源加载器。
/// 负责 ResourceDir 解析（固定不可配）、读图/读文、缺失策略（致命 vs 可恢复）。
/// 运行时复用 JS 脚本目录 User/JsScript/AutoSwitchRoles/ 下的资源，只读不写。
/// </summary>
public class AutoSwitchRolesResources
{
    private readonly ILogger _logger;

    /// <summary>资源根目录，固定为 AppContext.BaseDirectory/User/JsScript/AutoSwitchRoles。</summary>
    public string ResourceDir { get; }

    /// <summary>别名 → 正式名映射（由 combat_avatar.json 构建）。</summary>
    public Dictionary<string, string> AliasMap { get; private set; } = new();

    public AutoSwitchRolesResources(ILogger logger)
    {
        _logger = logger;
        ResourceDir = Path.Combine(AppContext.BaseDirectory, "User", "JsScript", "AutoSwitchRoles");
    }

    /// <summary>
    /// 读 combat_avatar.json → AliasMap。缺失/解析失败 = 致命（LogError + 返回 false，由 Start 终止）。R4.7 / R4.9
    /// </summary>
    public bool LoadAliasMap()
    {
        var path = Path.Combine(ResourceDir, "combat_avatar.json");
        if (!File.Exists(path))
        {
            _logger.LogError("配对界面切换角色资源缺失（致命）：{Path}，请确认 JS 脚本资源已随构建打包到该路径", path);
            return false;
        }

        try
        {
            AliasMap = AutoSwitchRolesDecisions.BuildAliasMap(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex)
        {
            // 别名表是核心依赖，解析失败属不可恢复的前置错误，记 LogError 后终止本次任务（不抛未处理异常）。
            _logger.LogError(ex, "解析 combat_avatar.json 失败（致命）");
            return false;
        }
    }

    /// <summary>
    /// 读 attribute.txt → FilterConfig。缺失/失败 = 可恢复（LogWarning + 返回空字典，按 JS 走无筛选分支）。R4.8 / R4.9
    /// </summary>
    public Dictionary<string, (string? Element, string? Weapon)> LoadFilterConfig()
    {
        var path = Path.Combine(ResourceDir, "attribute.txt");
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("attribute.txt 缺失，跳过筛选: {Path}", path);
                return new();
            }

            return AutoSwitchRolesDecisions.ParseAttribute(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            // 可恢复：筛选是优化项，读取失败按 JS 走无筛选分支（不抛未处理异常）。
            _logger.LogWarning(ex, "读取筛选配置失败，跳过筛选");
            return new();
        }
    }

    /// <summary>
    /// 读模板图。缺失/失败返回 null（调用方按 JS 对应分支降级）+ LogWarning。R4.5 / R4.9
    /// relPath 形如 "Assets/RecognitionObject/队伍配置.png"。
    /// </summary>
    public Mat? TryReadTemplate(string relPath)
    {
        var path = Path.Combine(ResourceDir, relPath);
        if (!File.Exists(path))
        {
            _logger.LogWarning("模板缺失，跳过: {Path}", path);
            return null;
        }

        try
        {
            return Cv2.ImRead(path, ImreadModes.Color);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取模板失败: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// 读角色头像图。文件不存在返回 null（用于"遇首个不存在即停止遍历"，不记日志）。R4.6 / R5
    /// fileNameNoExt 形如 "纳西妲01"。
    /// </summary>
    public Mat? TryReadCharacterImage(string fileNameNoExt)
    {
        var path = Path.Combine(ResourceDir, "Assets", "characterimage", fileNameNoExt + ".png");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Cv2.ImRead(path, ImreadModes.Color);
        }
        catch (Exception ex)
        {
            // 头像存在但读取失败属可恢复：记 Warning 后视为未命中（返回 null），不阻断其余号位。
            _logger.LogWarning(ex, "读取角色头像失败: {Path}", path);
            return null;
        }
    }
}
