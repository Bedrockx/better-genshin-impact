namespace BgiCoordinatorServer.Services;

/// <summary>
/// 联机锄地共享战斗"配额(quorum)结束同步"决策纯函数（服务端侧，与客户端同语义）。
/// multiplayer-shared-fight-end-quorum-sync spec / design §5。
/// </summary>
public static class SharedFightEndQuorumDecisions
{
    /// <summary>配额是否达成：doneCount ≥ ⌈participantCount × ratio⌉。</summary>
    public static bool IsQuorumReached(int doneCount, int participantCount, double ratio)
    {
        if (participantCount <= 0) return false;
        if (doneCount <= 0) return false;
        var r = ClampRatio(ratio);
        var threshold = (int)System.Math.Ceiling(participantCount * r);
        if (threshold < 1) threshold = 1;
        return doneCount >= threshold;
    }

    /// <summary>比例钳制到 [0,1]；NaN → 0.5。</summary>
    public static double ClampRatio(double ratio)
        => double.IsNaN(ratio) ? 0.5 : System.Math.Clamp(ratio, 0.0, 1.0);
}
