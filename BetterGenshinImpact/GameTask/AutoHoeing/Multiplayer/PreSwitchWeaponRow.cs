#nullable enable
namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

/// <summary>
/// 开锄前换武器单行配置：启用标志 + OcrSwitchWeaponTask 的 6 个参数。
/// 纯数据 POCO，无行为；存取与判定逻辑见 PreSwitchWeaponDecisions。
/// 字段默认值与 OcrSwitchWeaponConfig 出厂值对齐（Element="物"、QuickMode=true、PageScrollCount="2"），
/// 保证「无已存配置」时的默认一致性。
/// </summary>
public sealed class PreSwitchWeaponRow
{
    /// <summary>该行启用意图（勾选状态）。行是否真正执行还需 Character/Weapon 均非空，见 PreSwitchWeaponDecisions.IsRowEnabled。</summary>
    public bool Enabled { get; set; }

    /// <summary>目标角色中文名或别名。</summary>
    public string Character { get; set; } = "";

    /// <summary>目标武器中文名或简称。</summary>
    public string Weapon { get; set; } = "";

    /// <summary>元素筛选，「物」=不筛选。</summary>
    public string Element { get; set; } = "物";

    /// <summary>快速模式开关，默认 true。</summary>
    public bool QuickMode { get; set; } = true;

    /// <summary>武器格子行列，如「73」=第7行第3列，空=不指定。</summary>
    public string GridPosition { get; set; } = "";

    /// <summary>最大滑页次数（字符串，运行期由 OcrSwitchWeaponTask 钳制）。</summary>
    public string PageScrollCount { get; set; } = "2";
}
