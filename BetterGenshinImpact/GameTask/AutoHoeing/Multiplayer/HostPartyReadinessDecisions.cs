#nullable enable
using System;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer;

public enum HostPartyDecisionKind
{
    /// <summary>F2 信号 B 命中：游戏世界已满员且无陌生人，应 ESC + 等主界面 + return f2Count</summary>
    StartHoeing,

    /// <summary>F2 信号 B 检出陌生人闯入：应触发 KickStrangersAsync 并 continue</summary>
    KickStrangers,

    /// <summary>主界面分支 + 上一轮在 F2（被加载弹回）：应 Delay(2000) + WaitForMainUi + OpenCoOpScreen</summary>
    ReopenF2WithLoadDelay,

    /// <summary>主界面分支 + 上一轮也在主界面：F2 没开成功，应直接 OpenCoOpScreen</summary>
    ReopenF2NoDelay,

    /// <summary>F2 中且未满员：常规循环（按 Y / 踢陌生人扫描节流逻辑由调用方处理）</summary>
    WaitInF2,
}

public readonly record struct HostPartyReadinessInput(
    bool InMainUi,
    int SignalRCount,
    int F2Count,
    int ExpectedCount,
    bool IsInF2Screen)
{
    /// <summary>仅在 InMainUi=false 时 F2Count 才有意义；InMainUi=true 时调用方不应读取该字段</summary>
}

public readonly record struct HostPartyDecision(HostPartyDecisionKind Kind, int ReturnedCount)
{
    public static HostPartyDecision Of(HostPartyDecisionKind kind) => new(kind, 0);
    public static HostPartyDecision StartHoeing(int count) => new(HostPartyDecisionKind.StartHoeing, count);
}

/// <summary>
/// 房主等待循环的纯决策函数。给定一帧观测输入，返回"应当走哪条分支"。
/// 不持有任何外部依赖（client / logger / 截图），便于 PBT 直接撒输入跑性质。
/// </summary>
public static class HostPartyReadinessDecisions
{
    public static HostPartyDecision Decide(HostPartyReadinessInput x)
    {
        if (x.ExpectedCount <= 0)
        {
            // 防御性：期望人数无意义时不开锄，留主循环兜底
            return HostPartyDecision.Of(HostPartyDecisionKind.WaitInF2);
        }

        if (x.InMainUi)
        {
            // 关键修复：不再因 SignalRCount >= ExpectedCount 而直接 StartHoeing。
            // 主界面下一律切回 F2 复核，让信号 B 用游戏世界实际人数做最终判定。
            // SignalRCount 是否达标不再影响"开锄"决策，只影响 isInF2Screen
            // 控制的"是否需要 Delay(2000) 等加载稳定"。
            return x.IsInF2Screen
                ? HostPartyDecision.Of(HostPartyDecisionKind.ReopenF2WithLoadDelay)
                : HostPartyDecision.Of(HostPartyDecisionKind.ReopenF2NoDelay);
        }

        // InMainUi=false：F2 信号 B 主路径（task 3 不会再动这部分）
        if (x.F2Count >= x.ExpectedCount)
        {
            // 交叉校验：游戏内人数 > BGI 房间人数 → 陌生人闯入
            // 注意：bgiCount=SignalRCount，沿用 InMainUi=false 时的当前值
            var bgiCount = x.SignalRCount;
            if (x.F2Count > bgiCount && bgiCount > 0)
            {
                return HostPartyDecision.Of(HostPartyDecisionKind.KickStrangers);
            }
            return HostPartyDecision.StartHoeing(x.F2Count);
        }

        return HostPartyDecision.Of(HostPartyDecisionKind.WaitInF2);
    }
}
