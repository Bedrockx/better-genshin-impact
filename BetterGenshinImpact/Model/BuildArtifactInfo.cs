using System;

namespace BetterGenshinImpact.Model;

public class BuildArtifactInfo
{
    /// <summary>版本号（来自 run 的 display_title，如 "BetterGI 0.60.1+lcb.21.5-QuickyEnd-fix1"）</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>构建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>产物下载 URL</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>产物名称</summary>
    public string ArtifactName { get; set; } = string.Empty;

    /// <summary>工作流运行 ID</summary>
    public long RunId { get; set; }

    /// <summary>版本类型标签（正式版/修复版/测试版）</summary>
    public string VersionTag { get; set; } = "正式版";

    /// <summary>
    /// 从版本名称中解析版本类型。
    /// 版本格式: BetterGI 0.60.1+lcb.21.5-QuickyEnd-fix1
    /// fix = 修复版, test = 测试版, 其他 = 正式版
    /// </summary>
    public static string ParseVersionTag(string displayTitle)
    {
        // 取最后一个 '-' 后面的部分
        var lastDash = displayTitle.LastIndexOf('-');
        if (lastDash >= 0)
        {
            var suffix = displayTitle[(lastDash + 1)..].ToLowerInvariant();
            if (suffix.StartsWith("fix"))
                return "修复版";
            if (suffix.StartsWith("test"))
                return "测试版";
        }
        return "正式版";
    }
}
