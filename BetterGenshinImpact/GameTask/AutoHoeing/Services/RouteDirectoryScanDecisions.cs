#nullable enable

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 内置线路目录发现端判定（纯函数，PBT 友好）。
/// hoeing-variant-route-empty-json-crash-and-discovery-fix / EB-B / OQ2=a / OQ4。
///
/// 参考 KazuhaCollectSyncDecisions / HostPartyReadinessDecisions 模式：
/// static class，pure function，无 I/O / 无 logger / 无配置读取。
/// I/O（枚举文件统计数量）留在 RouteDirectoryScanner.ScanBuiltinRoutes，
/// 本类只做"数字 → 布尔"决策，便于 property-based test 直接撒输入。
/// </summary>
public static class RouteDirectoryScanDecisions
{
    /// <summary>
    /// 目录是否应被识别为"有效线路目录"（出现在 UI 线路选择里）。
    /// OQ2=a：递归统计的 *.json 数 > 0 即有效（含变体子文件夹 A变体/B变体/... 内的 JSON），
    /// 使"只含变体子文件夹、无顶层 JSON"的纯变体目录也被纳入（EB-B 2.4）。
    /// recursiveJsonCount &lt;= 0（真正的空目录 / 防御性负值）→ false（EB-B 2.6 不显示）。
    /// </summary>
    public static bool IsValidRouteDirectory(int recursiveJsonCount)
        => recursiveJsonCount > 0;
}
