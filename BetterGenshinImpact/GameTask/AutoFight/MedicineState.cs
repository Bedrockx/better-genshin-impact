using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 战斗吃药状态管理
/// 统一管理吃药计数器和复活检测标志位的生命周期
/// </summary>
public class MedicineState
{
    /// <summary>回复药使用次数</summary>
    public int HealCount { get; private set; }
    
    /// <summary>复活药使用次数</summary>
    public int ReviveCount { get; private set; }
    
    /// <summary>总使用次数</summary>
    public int TotalCount => HealCount + ReviveCount;

    /// <summary>
    /// 判断是否超过吃药上限
    /// </summary>
    public bool IsHealOverLimit(int maxHealCount) => HealCount >= maxHealCount;
    
    /// <summary>
    /// 判断是否超过复活上限
    /// </summary>
    public bool IsReviveOverLimit(int maxReviveCount = 3) => ReviveCount >= maxReviveCount;

    /// <summary>记录回复药使用</summary>
    public void IncrementHeal() => HealCount++;
    
    /// <summary>记录复活药使用</summary>
    public void IncrementRevive() => ReviveCount++;

    /// <summary>重置所有计数器</summary>
    public void Reset()
    {
        HealCount = 0;
        ReviveCount = 0;
    }

    /// <summary>
    /// 进入吃药作用域，设置 IsTpForRecover = true
    /// 应配合 try-finally 使用 ExitMedicineScope
    /// </summary>
    public void EnterMedicineScope()
    {
        AutoFightTask.IsTpForRecover = true;
    }

    /// <summary>
    /// 退出吃药作用域
    /// shouldEnableReviveCheck: true 表示吃药超额后启用外部复活检测
    /// </summary>
    public void ExitMedicineScope(bool shouldEnableReviveCheck = false)
    {
        if (shouldEnableReviveCheck)
        {
            AutoFightTask.IsTpForRecover = false;
            // 不在这里设置 AutoEatCount，让 ThrowWhenDefeated 自然累积
            // 战斗中超额退出后，ThrowWhenDefeated 会通过 AutoEatCount < 2 分支处理复活弹窗
            // AutoEatCount 累积到 2 后才会去七天神像（不会中断战斗）
        }
    }
}
