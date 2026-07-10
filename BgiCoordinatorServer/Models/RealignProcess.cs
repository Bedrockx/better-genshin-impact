namespace BgiCoordinatorServer.Models;

/// <summary>
/// 重对齐流程状态（multiplayer-abort-and-realign spec）
/// 当检测到异常玩家时，服务器创建此流程，协调所有玩家中断并重新对齐
/// </summary>
public class RealignProcess
{
    /// <summary>目标路线索引（所有异常玩家中路线索引最小值）</summary>
    public int TargetRouteIndex { get; set; }
    
    /// <summary>异常玩家UID列表</summary>
    public List<string> AbnormalPlayerUids { get; set; } = new();
    
    /// <summary>中断原因</summary>
    public string Reason { get; set; } = "";
    
    /// <summary>已响应 RealignReady 的玩家UID集合</summary>
    public HashSet<string> ReadyPlayers { get; set; } = new();
    
    /// <summary>广播时间</summary>
    public DateTime BroadcastTime { get; set; }
    
    /// <summary>是否已完成（已广播 StartRoute）</summary>
    public bool IsCompleted { get; set; }
}
