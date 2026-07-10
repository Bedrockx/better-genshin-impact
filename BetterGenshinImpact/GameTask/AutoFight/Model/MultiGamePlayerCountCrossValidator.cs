namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 联机锄地世界人数交叉校验（第2层）：把视觉数出的 (PlayerCount, IsHost)
/// 与协调器服务端权威值交叉校验，不一致以协调器为准覆盖。
/// coordinatorAvailable=false（单机/未连接/非锄地）→ 直接返回视觉值，Overridden=false。
/// 权威人数越界（<1 或 >4）→ 安全回退视觉值，Overridden=false（避免 MaxControlAvatarCount 查表抛异常）。
/// 否则人数/IsHost 以权威为准；任一与视觉不同则 Overridden=true。
/// 纯函数、无外部依赖，便于属性测试。
/// </summary>
public static class MultiGamePlayerCountCrossValidator
{
    public readonly record struct Result(int PlayerCount, bool IsHost, bool Overridden);

    public static Result Resolve(
        int visualPlayerCount, bool visualIsHost,
        bool coordinatorAvailable, int authoritativePlayerCount, bool authoritativeIsHost)
    {
        if (!coordinatorAvailable)
            return new Result(visualPlayerCount, visualIsHost, false);
        if (authoritativePlayerCount < 1 || authoritativePlayerCount > 4)
            return new Result(visualPlayerCount, visualIsHost, false);

        bool overridden = authoritativePlayerCount != visualPlayerCount
                          || authoritativeIsHost != visualIsHost;
        return new Result(authoritativePlayerCount, authoritativeIsHost, overridden);
    }
}
