#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 联机锄地"线路模式"下拉框的 UI↔布尔映射决策（纯函数，PBT 友好）。
/// spec: multiplayer-hoeing-route-mode-dropdown / design.md §Correctness Properties。
///
/// 参考 RouteDirectoryScanDecisions / KazuhaCollectSyncDecisions 模式：
/// static class，无外部依赖，便于 property-based test 直接撒输入。
/// 仅供 ScriptControlViewModel.ShowHoeingSettingsDialog 调用，不被运行时（AutoHoeingTask）引用。
/// </summary>
public static class RouteModeDecisions
{
    /// <summary>下拉选项一：固定内置联机线路（⇔ useFixedDebugRoutes = true）。</summary>
    public const string BuiltinOnline = "固定内置联机线路";

    /// <summary>下拉选项二：单机调试线路（⇔ useFixedDebugRoutes = false，用单机 groupIndex 选路跑联机）。</summary>
    public const string SoloDebug = "单机调试线路";

    /// <summary>
    /// 打开弹窗：useFixedDebugRoutes 布尔 → 下拉初始选项。
    /// true → 固定内置联机线路；false → 单机调试线路（Req 5.1 / 5.2）。
    /// 全函数：任意 bool 均有有效返回（Req 5.6 不抛、不空白）。
    /// </summary>
    public static string MapUseFixedToRouteMode(bool useFixed)
        => useFixed ? BuiltinOnline : SoloDebug;

    /// <summary>
    /// 保存：下拉选中项 → useFixedDebugRoutes 布尔。
    /// 选中固定内置联机线路 → true；其余（含 null / 未知）→ false（Req 5.3，保守落单机调试语义）。
    /// </summary>
    public static bool MapRouteModeToUseFixed(string? mode)
        => mode == BuiltinOnline;

    /// <summary>
    /// 可见性判定：是否处于"固定内置联机线路"模式。
    /// 与 MapRouteModeToUseFixed 等价，保留独立命名以提升调用处可读性（可见性 vs 持久化）。
    /// </summary>
    public static bool IsBuiltinOnline(string? mode)
        => mode == BuiltinOnline;

    /// <summary>
    /// ComboBox 初始选中回退：saved 在 options 内则用 saved，否则回退 def。
    /// 用于执行线路（groupIndex）下拉的旧 JSON 兼容（Req 3.3 / 5.6），避免脏值导致空白。
    /// 约定 def ∈ options（调用方保证）；返回值恒 ∈ options。
    /// </summary>
    public static string ResolveSelectedOrDefault(IReadOnlyList<string> options, string? saved, string def)
    {
        if (options == null || options.Count == 0) return def;
        if (saved != null && options.Contains(saved)) return saved;
        return def;
    }
}
