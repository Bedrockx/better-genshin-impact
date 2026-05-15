namespace BetterGenshinImpact.GameTask.AutoFriendship.Model;

/// <summary>
/// 掉落信息
/// </summary>
public class DropInfo
{
    /// <summary>经验值数量</summary>
    public int Exp { get; set; }

    /// <summary>摩拉数量</summary>
    public int Mora { get; set; }

    /// <summary>是否有掉落</summary>
    public bool HasDrop => Exp > 0 || Mora > 0;

    /// <summary>
    /// 重置掉落信息
    /// </summary>
    public void Reset()
    {
        Exp = 0;
        Mora = 0;
    }

    /// <summary>
    /// 累加掉落
    /// </summary>
    public void Add(int exp, int mora)
    {
        Exp += exp;
        Mora += mora;
    }
}
