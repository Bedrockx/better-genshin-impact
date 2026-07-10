using System;
using Semver;

namespace BgiCoordinatorServer.Services;

/// <summary>
/// 版本一致性校验纯函数（hoeing-multiplayer-version-compatibility-check）。
/// 权威判定放服务端：JoinRoom 在服务端执行，硬阻断最可靠。
/// SemVer 解析仅用于通配识别（prerelease 是否含 alpha），不用于常规相等判定。
/// </summary>
internal static class VersionCompatibilityDecisions
{
    /// <summary>
    /// 是否为开发者通配版本：版本经 SemVer 解析后 prerelease 非空且含标识 "alpha"。
    /// 构建元数据 +...（含 -OnLine-test11）不属于 prerelease，不会触发通配（R4.8）。
    /// 空串 / 解析失败 → 非通配（R2.6）。
    /// </summary>
    public static bool IsWildcard(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        try
        {
            // SemVersion.Parse 与客户端 Global.IsNewVersion 同包同版本，口径一致
            var sv = SemVersion.Parse(version, SemVersionStyles.Any);
            // Prerelease 是点分标识符集合；任一标识符等于/含 "alpha" 即通配
            foreach (var id in sv.PrereleaseIdentifiers)
            {
                if (id.Value.Contains("alpha", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            // 解析失败按非通配处理（R2.6：不豁免常规口径，且不抛未捕获异常）
            return false;
        }
    }

    /// <summary>
    /// 判定两个参与方版本是否兼容。
    /// - 任一方通配 → 兼容（对称，R4.2/R4.3/R4.4）。
    /// - 双方非通配 → Full_Version_String ordinal 完全相等才兼容（R3.3/R3.4/R3.5）。
    /// - 空串（未上报 / Legacy_Client）非通配，且与对端非空串必不相等 → 不兼容（R2.5）。
    /// </summary>
    public static bool IsCompatible(string? a, string? b)
    {
        if (IsWildcard(a) || IsWildcard(b)) return true;
        // 常规口径：完整字符串 ordinal 全匹配（含 + 构建元数据），
        // 禁止用 SemVer precedence/CompareSortOrderTo（R3.6）。
        return string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从房间已在场玩家的上报版本集合中解析"房间基准版本"：第一个非通配且非空的版本。
    /// 通配玩家（含开发者通配房主）不作为基准，避免"房主通配 → 全员放行"的校验失效（Property 7）。
    /// 若全部为通配 / 空串 / 集合为空 → 返回 null，表示房间内尚无基准。
    /// </summary>
    public static string? ResolveBaselineVersion(IEnumerable<string?>? existingVersions)
    {
        if (existingVersions == null) return null;
        foreach (var v in existingVersions)
        {
            if (!string.IsNullOrWhiteSpace(v) && !IsWildcard(v))
                return v;
        }
        return null;
    }

    /// <summary>
    /// 房间级加入校验：判定 joiner 是否可加入房间。
    /// existingVersions = 房间已在场玩家（含房主）的上报版本集合。
    /// 规则：
    ///  - 基准 = ResolveBaselineVersion（第一个非通配非空版本）。
    ///  - 有基准：joiner 与基准 IsCompatible 才放行（通配 joiner 永远放行；非通配 joiner 须 ordinal 全等）。
    ///  - 无基准（房间全通配/空）：joiner 非空即放行（含通配，以及"成为首个非通配基准"的普通版）；
    ///    joiner 为空串（Legacy 未上报）→ 阻断（Q5）。
    /// </summary>
    public static bool CanJoin(string? joinerVersion, IEnumerable<string?>? existingVersions)
    {
        var baseline = ResolveBaselineVersion(existingVersions);
        if (baseline == null)
            return !string.IsNullOrWhiteSpace(joinerVersion);
        return IsCompatible(joinerVersion, baseline);
    }
}
