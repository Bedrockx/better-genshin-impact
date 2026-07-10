namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 联机回血"直接放 Q"判定纯函数（PBT 友好，无外部依赖）。
/// 详见 .kiro/specs/hoeing-multiplayer-recover-avatar-direct-q-no-detection/design.md
/// §Fix Implementation / Correctness Properties。
/// </summary>
public static class MultiplayerRecoverBurstDecisions
{
    /// <summary>
    /// 联机 ∧ RecoverAvatarIndex 已配置（非 null/空/空白）→ 跳过 Q 就绪检测直接放 Q。
    /// 单机恒返回 false（保证单机路径零感知）；未配置恒返回 false（保持现状兜底）。
    /// </summary>
    public static bool ShouldSkipQDetectionAndDirectBurst(bool isInMultiGame, string? recoverAvatarIndex)
        => isInMultiGame && !string.IsNullOrWhiteSpace(recoverAvatarIndex);
}
