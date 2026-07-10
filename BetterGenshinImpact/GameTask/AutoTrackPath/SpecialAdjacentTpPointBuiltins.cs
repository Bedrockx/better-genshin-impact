using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 内置特殊相邻传送点清单 —— 硬编码静态只读列表，编译进 dll。
/// 这是"系统自带、开箱即用"的清单：程序一编译就有，永不丢失、永不被任何文件拷贝覆盖，
/// 也不存在"铺默认文件覆盖用户运行时文件"的风险（对比用户 JSON 走 User 目录纯数据）。
/// 运行时与用户 JSON 清单合并后统一判定（见 TpTask.GetSpecialAdjacentTpPointList）。
/// 详见 .kiro/specs/teleport-adjacent-point-misclick-zoom-whitelist-fix/design.md。
/// </summary>
public static class SpecialAdjacentTpPointBuiltins
{
    /// <summary>
    /// 内置清单（当前唯一一条，直到开发完成）。无专属 Zoom → 命中时用全局默认 2.5。
    /// </summary>
    public static readonly IReadOnlyList<SpecialAdjacentTpPoint> List = new List<SpecialAdjacentTpPoint>
    {
        new() { X = -796.32, Y = 1037.22 }, 
        new() { X = 1184.09, Y = 622.63 },// Zoom = null → 全局默认 2.5
    };
}
