using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Preservation Property Test - Property 2: 联机模式行为不变
///
/// **Validates: Requirements 3.1, 3.2, 3.3**
///
/// 通过静态代码审查验证联机模式相关代码结构（房主/成员分支、路线上传、等待逻辑）
/// 在修复前后保持不变。
///
/// 此测试在未修复代码上预期 PASS（确认联机模式基线行为）。
/// 修复后同样应 PASS（确认无回归）。
/// </summary>
public class SingleModeNullRefPreservationTest
{
    private static readonly string? RepoRoot = FindRepoRoot(AppContext.BaseDirectory);
    private static readonly string FilePath = Path.Combine(
        RepoRoot ?? "", "BetterGenshinImpact", "GameTask", "AutoHoeing", "AutoHoeingTask.cs");

    private static string[]? _lines;

    private static string[] GetLines()
    {
        if (_lines == null)
        {
            Assert.True(RepoRoot != null, "无法找到仓库根目录（BetterGenshinImpact.sln 所在目录）");
            Assert.True(File.Exists(FilePath), $"找不到源文件：{FilePath}");
            _lines = File.ReadAllLines(FilePath);
        }
        return _lines;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3.1 联机路线同步逻辑结构完整性
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 ProcessRoutesByGroup 方法中存在联机模式条件块（步骤1注释）
    ///
    /// 无论 if 语句是否被注释，步骤1的注释文字和代码块本身应始终存在于源文件中。
    /// 这确认联机同步逻辑代码结构未被删除或重构。
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_MultiplayerSyncBlock_CommentAndCodeExist()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // 步骤1注释应存在（无论是否与 if 同行）
        Assert.Contains("步骤1：联机模式路线列表同步", content);

        // 联机同步块的核心条件表达式应存在于文件中
        Assert.Contains("_config.MultiplayerEnabled && _coordinatorClientRef != null", content);
    }

    /// <summary>
    /// 验证房主身份判断逻辑（UID 匹配 + MultiplayerRole 兜底）结构完整
    ///
    /// 房主判断逻辑：优先使用 UID 匹配，仅在 PlayerUid 为空时使用 MultiplayerRole 兜底。
    /// 此结构应在修复前后保持不变。
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void Preservation_HostRoleDetermination_UidMatchWithFallback()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // UID 匹配逻辑：_coordinatorClientRef.HostPlayerUid == _config.PlayerUid
        Assert.Contains("_coordinatorClientRef.HostPlayerUid == _config.PlayerUid", content);

        // MultiplayerRole 兜底逻辑
        Assert.Contains("_config.MultiplayerRole == \"host\"", content);

        // PlayerUid 非空检查（决定使用哪种判断方式）
        Assert.Contains("string.IsNullOrEmpty(_config.PlayerUid)", content);
    }

    /// <summary>
    /// 验证房主分支：CD 过滤后上传路线列表（SetHostRouteListAsync）逻辑存在
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_HostBranch_SetHostRouteListAsyncExists()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // 房主上传路线列表
        Assert.Contains("SetHostRouteListAsync", content);

        // 房主分支注释标识
        Assert.Contains("房主：CD 过滤后上传最终路线文件名列表", content);
    }

    /// <summary>
    /// 验证成员分支：等待房主路线列表就绪事件（HostRouteListReady）逻辑存在
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_MemberBranch_HostRouteListReadyEventExists()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // 成员等待事件
        Assert.Contains("HostRouteListReady", content);

        // 成员直接拉取兜底（multiplayer-member-skip-round-stuck-roundend-sync-fix：
        // 原 GetHostRouteListAsync + IsHostRouteListUploadedAsync 两次独立查询已合并为
        // 一次原子 GetHostRouteListStatusAsync，消除 TOCTOU）
        Assert.Contains("GetHostRouteListStatusAsync", content);

        // 成员分支注释标识
        Assert.Contains("成员：等待房主路线列表就绪事件", content);
    }

    /// <summary>
    /// 验证成员分支：90秒超时等待逻辑存在
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Fact]
    public void Preservation_MemberBranch_TimeoutWaitLogicExists()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // 90秒超时
        Assert.Contains("TimeSpan.FromSeconds(90)", content);

        // 超时日志（multiplayer-host-empty-route-member-wait-timeout-fix 后文案含"房主未上传"）
        Assert.Contains("等待房主上传路线列表超时（90秒", content);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3.2 房主/成员角色判断结构完整性（行级验证）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 isHost 变量赋值行存在，且使用三元表达式（UID 优先 + 兜底）
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void Preservation_IsHostAssignment_TernaryExpressionStructure()
    {
        var lines = GetLines();

        // 找到 isHost 赋值行
        var isHostLine = lines
            .Select((line, idx) => (line, idx))
            .FirstOrDefault(t => t.line.Contains("bool isHost =") && t.line.Contains("IsNullOrEmpty"));

        Assert.True(isHostLine.line != null,
            "未找到 'bool isHost = !string.IsNullOrEmpty(...)' 赋值行，房主判断逻辑可能被修改");

        // 验证三元表达式结构：下一行应包含 HostPlayerUid == _config.PlayerUid
        var nextLine = lines[isHostLine.idx + 1];
        Assert.Contains("HostPlayerUid == _config.PlayerUid", nextLine);

        // 再下一行应包含 MultiplayerRole 兜底
        var fallbackLine = lines[isHostLine.idx + 2];
        Assert.Contains("MultiplayerRole", fallbackLine);
    }

    /// <summary>
    /// 验证 isHost 条件判断行（含 HostPlayerUid 空检查兜底）存在
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Fact]
    public void Preservation_IsHostCondition_WithEmptyHostUidFallback()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // if (isHost || string.IsNullOrEmpty(_coordinatorClientRef.HostPlayerUid))
        Assert.Contains("if (isHost || string.IsNullOrEmpty(_coordinatorClientRef.HostPlayerUid))", content);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3.3 单机模式路线过滤逻辑结构完整性
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 ProcessRoutesByGroup 中按组过滤路线的逻辑存在
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Fact]
    public void Preservation_SingleMode_GroupFilterLogicExists()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // 按组过滤：r.Group == targetGroup && r.Selected
        Assert.Contains("r.Group == targetGroup && r.Selected", content);

        // GroupIndex 配置项引用
        Assert.Contains("_config.GroupIndex", content);
    }

    /// <summary>
    /// 验证 CD 检查逻辑（IsOnCooldown）在 ProcessRoutesByGroup 中存在
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Fact]
    public void Preservation_SingleMode_CdCheckLogicExists()
    {
        var lines = GetLines();
        var content = string.Join("\n", lines);

        // CD 检查
        Assert.Contains("_cdManager.IsOnCooldown", content);

        // StartRouteIndex 条件（跳过 CD 检查的条件）
        Assert.Contains("_config.StartRouteIndex > 0", content);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────────────────

    private static string? FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BetterGenshinImpact.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
