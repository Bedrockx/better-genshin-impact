namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// LoadGroupSettings 缺失文件处理决策（纯函数，PBT 友好）。
/// hoeing-multiplayer-account-name-config Req 3。
/// 非路径组一且 settings/{account}.json 缺失时，是否记录 "请先在路径组一运行一次" 阻塞错误：
///   - 单机路径 (isMultiplayer == false) → true（保留现状 LogError，R3.4 / R4）。
///   - 联机路径 (isMultiplayer == true)  → false（静默以默认设置继续，R3.1 / R3.3）。
/// 无外部依赖，不持有 client/logger。
/// </summary>
public static class LoadGroupSettingsDecisions
{
    public static bool ShouldLogMissingSettingsError(bool isMultiplayer)
        => !isMultiplayer;
}
