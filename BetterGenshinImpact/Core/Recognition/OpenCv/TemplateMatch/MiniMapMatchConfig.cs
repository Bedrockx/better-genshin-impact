using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

public static class MiniMapMatchConfig
{
    /// <summary>
    /// 原始小地图尺寸
    /// </summary>
    public const int OriginalSize = 156;

    /// <summary>
    /// 粗匹配时的地图尺寸
    /// </summary>
    public const int RoughSize = 52;

    /// <summary>
    /// 精确匹配时的地图尺寸
    /// </summary>
    public const int ExactSize = 260;

    public const int RoughZoom = 5;
    public const int ExactZoom = 1;
    public const int RoughSearchRadius = 50;
    public const int ExactSearchRadius = 20;
    /// <summary>
    /// 置信度阈值组（rank0/1/2）。每次读取内存配置单例并校验兜底，使 UI 调参实时生效。
    /// 配置为 null（启动早期）或非法（个数/范围）时回落默认 {0.99,0.97,0.95}（Requirement 1.2/1.5/7.1）。
    /// 返回长度恒为 3 的新数组，调用点 IsSuccess 用 Math.Clamp(rank,0,Length-1) 索引（边界行为不变）。
    /// </summary>
    public static float[] ConfidenceThresholds
    {
        get
        {
            var cfg = ConfigService.Config?.MiniMapMatchTuningConfig;
            if (cfg == null)
            {
                return new[]
                {
                    MiniMapMatchTuningConfig.DefaultRank0Threshold,
                    MiniMapMatchTuningConfig.DefaultRank1Threshold,
                    MiniMapMatchTuningConfig.DefaultRank2Threshold
                };
            }

            var (thresholds, fellBack) = MiniMapMatchTuningValidator.ValidateThresholds(
                cfg.Rank0ConfidenceThreshold, cfg.Rank1ConfidenceThreshold, cfg.Rank2ConfidenceThreshold);
            if (fellBack)
            {
                MiniMapTuningWarn.OnceConfidence(
                    cfg.Rank0ConfidenceThreshold, cfg.Rank1ConfidenceThreshold, cfg.Rank2ConfidenceThreshold);
            }
            return thresholds;
        }
    }
    
}