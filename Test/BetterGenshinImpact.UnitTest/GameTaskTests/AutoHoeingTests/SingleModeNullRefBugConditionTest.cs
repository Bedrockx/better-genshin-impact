using BetterGenshinImpact.GameTask.AutoHoeing;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoHoeingTests;

/// <summary>
/// Bug Condition Exploration Test - Property 1: 单机模式 NullReferenceException
///
/// **Validates: Requirements 1.1, 1.2**
///
/// 根因：AutoHoeingTask.cs 第 1486 行，注释与 if 语句写在同一行：
///
///   // 步骤1：联机模式路线列表同步（...）        if (_config.MultiplayerEnabled &amp;&amp; _coordinatorClientRef != null)
///
/// C# 单行注释从 // 延伸至行尾，if 语句被完全注释掉，其后的代码块变为无条件执行。
/// 单机模式下 _coordinatorClientRef 为 null，访问 _coordinatorClientRef.HostPlayerUid 时
/// 抛出 NullReferenceException，"锄地一条龙"单机启动立即崩溃。
///
/// 此测试在未修复代码上预期 FAIL（失败即证明 bug 存在）。
/// 不要修复代码，只记录反例。
/// </summary>
public class SingleModeNullRefBugConditionTest
{
    /// <summary>
    /// 静态代码审查：确认 if 语句被注释掉（Bug 条件 1.2）
    ///
    /// 通过读取源文件内容，验证第 1486 行确实将注释与 if 写在同一行，
    /// 导致 if 语句被 C# 单行注释吞掉。
    ///
    /// 预期（未修复）：该行包含 "if (_config.MultiplayerEnabled" 但整行以 // 开头（被注释）
    /// 预期（修复后）：if 语句独占一行，不在注释行内
    ///
    /// **Validates: Requirements 1.2**
    /// </summary>
    [Fact]
    public void BugCondition_IfStatementCommentedOut_Line1486_ShouldBeOnSeparateLine()
    {
        // Arrange: 定位源文件
        // 从测试程序集位置向上查找项目根目录
        var assemblyDir = AppContext.BaseDirectory;
        var repoRoot = FindRepoRoot(assemblyDir);
        Assert.True(repoRoot != null, "无法找到仓库根目录（BetterGenshinImpact.sln 所在目录）");

        var filePath = Path.Combine(repoRoot!, "BetterGenshinImpact", "GameTask", "AutoHoeing", "AutoHoeingTask.cs");
        Assert.True(File.Exists(filePath), $"找不到源文件：{filePath}");

        // Act: 读取第 1486 行（1-indexed）
        var lines = File.ReadAllLines(filePath);
        Assert.True(lines.Length >= 1486, $"文件行数不足 1486 行，实际 {lines.Length} 行");

        var line1486 = lines[1485]; // 0-indexed

        // Assert: 验证 bug 条件
        // 未修复代码：该行同时包含注释前缀和 if 语句（if 被注释掉）
        // 修复后代码：if 语句独占一行，不在注释行内
        bool ifIsCommentedOut = line1486.TrimStart().StartsWith("//")
                                && line1486.Contains("if (_config.MultiplayerEnabled");

        // 此断言在未修复代码上 FAIL（因为 if 确实被注释掉了）
        // 反例：line1486 = "        // 步骤1：...        if (_config.MultiplayerEnabled && _coordinatorClientRef != null)"
        Assert.False(ifIsCommentedOut,
            $"[反例] 第 1486 行将 if 语句与注释写在同一行，if 被 C# 单行注释吞掉，" +
            $"单机模式下 _coordinatorClientRef 为 null 时无条件进入联机块，" +
            $"访问 _coordinatorClientRef.HostPlayerUid 抛出 NullReferenceException。\n" +
            $"实际行内容：{line1486.Trim()}");
    }

    /// <summary>
    /// 静态代码审查：确认 if 条件块无条件执行（Bug 条件 1.1）
    ///
    /// 验证第 1487 行（if 块的开括号 {）紧跟在被注释的 if 行之后，
    /// 证明代码块在 if 被注释后变为无条件执行。
    ///
    /// 预期（未修复）：第 1487 行为 "{"，且第 1486 行的 if 被注释 → 无条件执行
    /// 预期（修复后）：第 1487 行为 if 语句，第 1488 行为 "{"
    ///
    /// **Validates: Requirements 1.1**
    /// </summary>
    [Fact]
    public void BugCondition_CodeBlockUnconditional_WhenIfCommentedOut()
    {
        // Arrange
        var assemblyDir = AppContext.BaseDirectory;
        var repoRoot = FindRepoRoot(assemblyDir);
        Assert.True(repoRoot != null, "无法找到仓库根目录");

        var filePath = Path.Combine(repoRoot!, "BetterGenshinImpact", "GameTask", "AutoHoeing", "AutoHoeingTask.cs");
        Assert.True(File.Exists(filePath), $"找不到源文件：{filePath}");

        var lines = File.ReadAllLines(filePath);
        Assert.True(lines.Length >= 1487, $"文件行数不足 1487 行");

        var line1486 = lines[1485]; // 0-indexed，第 1486 行
        var line1487 = lines[1486]; // 0-indexed，第 1487 行

        // 未修复代码：
        //   line1486 = "        // 步骤1：...        if (...)"  ← if 被注释
        //   line1487 = "        {"                              ← 无条件执行的代码块
        bool line1486HasCommentedIf = line1486.TrimStart().StartsWith("//")
                                      && line1486.Contains("if (_config.MultiplayerEnabled");
        bool line1487IsOpenBrace = line1487.Trim() == "{";

        // 此断言在未修复代码上 FAIL
        // 反例：if 被注释（line1486）+ 代码块无条件执行（line1487 = "{"）
        // → 单机模式下 _coordinatorClientRef 为 null 时必然 NullReferenceException
        Assert.False(line1486HasCommentedIf && line1487IsOpenBrace,
            $"[反例] if 语句被注释（第 1486 行），代码块（第 1487 行 = '{{' ）无条件执行。\n" +
            $"单机模式下 _coordinatorClientRef = null，访问其成员必然抛出 NullReferenceException。\n" +
            $"第 1486 行：{line1486.Trim()}\n" +
            $"第 1487 行：{line1487.Trim()}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 从给定目录向上查找包含 BetterGenshinImpact.sln 的仓库根目录
    /// </summary>
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
