namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 别人世界（访客）出战箭头归属硬防御（第3层）：对第1层 FindMulti 选出的"最底部箭头"
/// 做分人数 Y 下界硬检查——本机可控角色恒在右侧栏最底部，其出战箭头的 Y 必落在
/// 该人数对应下界以下（更靠下）；否则判定为上方队友行误检出的"假箭头"，拒绝采纳。
/// 纯函数、无外部依赖，便于属性测试。
/// 详见 .kiro/specs/hoeing-multiplayer-otherworld-teammate-avatar-misrecognition-fix/design.md
/// </summary>
public static class OtherWorldArrowGuard
{
    /// <summary>4 人世界：本机出战箭头在 1080p 基准下的 Y 下界（用户实测给定）。</summary>
    public const int ArrowMinY1080p_4Player = 540;

    /// <summary>2 人 / 3 人世界：本机出战箭头在 1080p 基准下的 Y 下界（用户实测给定）。</summary>
    public const int ArrowMinY1080p_2or3Player = 380;

    /// <summary>
    /// 判断第1层选出的最底部箭头是否落在本机最底部行（Y 足够靠下）。
    /// </summary>
    /// <param name="arrowY">capture-space 箭头 Y 坐标（FindMulti 选出的最底部候选的 Y）</param>
    /// <param name="playerCount">经第2层交叉校验/重识别后的世界人数</param>
    /// <param name="assetScale">TaskContext.SystemInfo.AssetScale，1080p → capture-space 缩放</param>
    /// <returns>true=箭头在本机最底部行（采纳）；false=疑似上方假箭头（拒绝走兜底）</returns>
    public static bool IsArrowAtSelfRow(int arrowY, int playerCount, double assetScale)
    {
        if (assetScale <= 0) return true; // 防御：非法 scale 不误拒，交由上层其它校验
        int min1080p = playerCount >= 4 ? ArrowMinY1080p_4Player : ArrowMinY1080p_2or3Player;
        return arrowY > min1080p * assetScale;
    }
}
