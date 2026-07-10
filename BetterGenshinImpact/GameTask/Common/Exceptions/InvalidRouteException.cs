namespace BetterGenshinImpact.GameTask.Common.Exceptions;

/// <summary>
/// 路线 JSON 在加载阶段被检测到结构非法时抛出（route-variant-sync-by-logical-id spec / R1.6）。
/// 含义：作者编辑路线时犯了不可恢复的错误（如同一路线内 SyncPointId 重复），
/// 必须在加载阶段拒绝、明确告知作者修正，而不是在执行阶段产生隐性同步漂移。
/// </summary>
public class InvalidRouteException : System.Exception
{
    public InvalidRouteException(string message) : base(message)
    {
    }

    public InvalidRouteException(string message, System.Exception inner) : base(message, inner)
    {
    }
}
