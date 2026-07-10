using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 内置线路目录扫描服务，负责扫描和验证 Assets 目录下的线路文件夹
/// </summary>
public class RouteDirectoryScanner
{
    private static readonly ILogger Logger = App.GetLogger<RouteDirectoryScanner>();
    private readonly string _assetsPath;

    public RouteDirectoryScanner()
    {
        _assetsPath = Path.Combine(Global.Absolute("GameTask"), "AutoHoeing", "Assets");
    }

    /// <summary>
    /// 扫描内置线路目录，返回所有有效的线路文件夹信息
    /// </summary>
    /// <returns>有效线路文件夹列表</returns>
    public List<BuiltinRouteFolder> ScanBuiltinRoutes()
    {
        var folders = new List<BuiltinRouteFolder>();

        if (!Directory.Exists(_assetsPath))
        {
            Logger.LogWarning("[内置线路扫描] Assets 目录不存在: {Path}", _assetsPath);
            return folders;
        }

        try
        {
            // 首先添加 DebugRoutes 作为默认选项（如果存在）
            var debugRoutesPath = Path.Combine(_assetsPath, "DebugRoutes");
            if (Directory.Exists(debugRoutesPath))
            {
                var debugRouteFiles = Directory.GetFiles(debugRoutesPath, "*.json");
                if (debugRouteFiles.Length > 0)
                {
                    folders.Add(new BuiltinRouteFolder
                    {
                        FolderName = "DebugRoutes",
                        FullPath = debugRoutesPath,
                        RouteCount = debugRouteFiles.Length
                    });
                    Logger.LogInformation("[内置线路扫描] 添加默认 DebugRoutes 目录，包含 {Count} 个路线文件", debugRouteFiles.Length);
                }
            }

            // 然后扫描其他子目录
            var subdirs = Directory.GetDirectories(_assetsPath);
            foreach (var dir in subdirs)
            {
                var folderName = Path.GetFileName(dir);

                // 跳过已经添加的 DebugRoutes 目录
                if (folderName.Equals("DebugRoutes", StringComparison.OrdinalIgnoreCase))
                    continue;

                // hoeing-variant-route-empty-json-crash-and-discovery-fix / EB-B：
                // 递归统计（含变体子文件夹 A变体/B变体/... 内的 JSON），
                // 使"只含变体子文件夹、无顶层 JSON"的纯变体目录也被识别为有效目录，
                // 用户无需再往顶层放占位 JSON。真正的空目录（递归也无 JSON）仍跳过。
                var routeFiles = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);
                if (routeFiles.Length == 0)
                {
                    Logger.LogDebug("[内置线路扫描] 跳过空文件夹: {Folder}", folderName);
                    continue;
                }

                folders.Add(new BuiltinRouteFolder
                {
                    FolderName = folderName,
                    FullPath = dir,
                    RouteCount = routeFiles.Length
                });
            }

            Logger.LogInformation("[内置线路扫描] 发现 {Count} 个有效线路文件夹", folders.Count);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex, "[内置线路扫描] 无权限访问目录: {Path}", _assetsPath);
        }
        catch (IOException ex)
        {
            Logger.LogError(ex, "[内置线路扫描] 读取目录时发生 IO 错误: {Path}", _assetsPath);
        }

        return folders;
    }

    /// <summary>
    /// 列出某内置线路文件夹下递归扫描到的所有 *.json 线路文件，返回稳定相对标识列表（R1.2）。
    /// 标识 = 相对该文件夹的相对路径（含变体子文件夹），用 '/' 归一分隔符，按序数升序排序保证稳定（R1.1）。
    /// 文件夹不存在 / 无 json → 返回空列表，不抛异常（R1.3）。
    /// 不修改 ScanBuiltinRoutes（§Unchanged Behavior 第 6 条），仅新增本方法。
    /// </summary>
    /// <param name="folderFullPath">BuiltinRouteFolder.FullPath</param>
    public List<RouteFileItem> ListRouteFiles(string folderFullPath)
    {
        var items = new List<RouteFileItem>();
        if (string.IsNullOrEmpty(folderFullPath) || !Directory.Exists(folderFullPath))
        {
            return items;   // 空列表，不抛（R1.3）
        }

        try
        {
            var files = Directory.GetFiles(folderFullPath, "*.json", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(folderFullPath, f).Replace('\\', '/');
                items.Add(new RouteFileItem { RelativeId = rel, FullPath = f });
            }
            items.Sort((a, b) => string.CompareOrdinal(a.RelativeId, b.RelativeId)); // 稳定排序
        }
        catch (Exception ex)
        {
            // 扫描失败不应阻断弹窗（可恢复异常）：记录并返回已收集项
            Logger.LogWarning(ex, "[线路清单] 扫描文件夹失败: {Path}", folderFullPath);
        }

        return items;
    }
}

/// <summary>
/// 文件夹内单条线路文件标识。
/// </summary>
public class RouteFileItem
{
    /// <summary>相对该文件夹的相对路径（'/' 分隔），唯一且稳定，作为映射键。</summary>
    public string RelativeId { get; set; } = "";

    /// <summary>线路文件绝对路径（仅 UI 展示/调试用，不入持久化）。</summary>
    public string FullPath { get; set; } = "";
}

/// <summary>
/// 内置线路文件夹信息
/// </summary>
public class BuiltinRouteFolder
{
    /// <summary>
    /// 文件夹名称（用作显示和标识）
    /// </summary>
    public string FolderName { get; set; } = "";

    /// <summary>
    /// 文件夹完整路径
    /// </summary>
    public string FullPath { get; set; } = "";

    /// <summary>
    /// 文件夹中的路线文件数量
    /// </summary>
    public int RouteCount { get; set; }
}
