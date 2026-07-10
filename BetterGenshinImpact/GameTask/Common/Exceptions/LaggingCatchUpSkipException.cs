namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

/// <summary>
/// 落后成员逐段追赶的跳段信号异常（hoeing-multiplayer-lagging-member-catchup spec / BUG-D 修复）。
/// 继承 RetryException，使 PathExecutor 现有 catch(RetryException) 能捕获；
/// 在 catch 块最前面以 is 判别后走【非异常】跳段分支（置 SkipToNextSegment + break），
/// 不消费 escalation、不走 _syncPointReached 分流、不上报 Reviving——纯 Normal 语义跳段。
/// 仅作落后追赶跳段信号，绝不在异常恢复/复苏/集体卡死链路抛出。
/// </summary>
public class LaggingCatchUpSkipException : RetryException
{
    public LaggingCatchUpSkipException(string message) : base(message) { }
}
