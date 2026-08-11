using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 角色特化动作分派（按动作名+角色名决定是否使用特化逻辑）
/// </summary>
public static class AvatarSpecialAction
{
    /// <summary>
    /// 资源缩放比例
    /// </summary>
    private static double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    /// <summary>
    /// 木偶（桑多涅）红温状态评分阈值（固定 0.5）。
    /// </summary>
    private const double OverheatThreshold = 0.5;

    /// <summary>
    /// 恰斯卡子弹框特征模型：识别子弹框是否存在（子弹框不存在时恰斯卡处于喷射状态）
    /// 8 个特征，由自训练工具导出的 JSON 硬编码（ROI 1350,360-1510,760，1080p 基准绝对坐标）
    /// </summary>
    private static readonly FeatureScorerExportData _chascaBulletBoxModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F1", Channel = "V", X = 1445, Y = 706, W = 2, H = 3,
                IsCircular = false, Range = 1, RefVal = 0.9634, Weight = 0.7582,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0166, 0.0440, 0.1112, 0.2537, 0.4803, 0.7153, 0.8723, 0.9489, 0.9806, 0.9928, 0.9973]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "V", X = 1453, Y = 708, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9701, Weight = 0.7766,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0061, 0.0163, 0.0432, 0.1093, 0.2502, 0.4756, 0.7115, 0.8702, 0.9480, 0.9802, 0.9926, 0.9973]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "V", X = 1428, Y = 738, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9726, Weight = 0.7743,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0066, 0.0177, 0.0466, 0.1173, 0.2654, 0.4954, 0.7275, 0.8789, 0.9517, 0.9817, 0.9932, 0.9975]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "V", X = 1430, Y = 731, W = 3, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9735, Weight = 0.7847,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0059, 0.0158, 0.0419, 0.1061, 0.2440, 0.4673, 0.7046, 0.8664, 0.9463, 0.9796, 0.9924, 0.9972]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1368, Y = 755, W = 2, H = 3,
                IsCircular = true, Range = 360, RefVal = 313.7753, Weight = 0.7740,
                ProbTable = [0, 0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0043, 0.0115, 0.0306, 0.0790, 0.1891, 0.3879, 0.6327, 0.8240, 0.9272, 0.9719, 0.9895, 0.9961, 0.9986, 0.9995, 0.9998]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1370, Y = 753, W = 4, H = 3,
                IsCircular = true, Range = 360, RefVal = 306.7564, Weight = 0.8170,
                ProbTable = [0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0030, 0.0081, 0.0217, 0.0569, 0.1408, 0.3082, 0.5477, 0.7670, 0.8995, 0.9605, 0.9851, 0.9945, 0.9980, 0.9992, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "V", X = 1398, Y = 755, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9506, Weight = 0.7866,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0020, 0.0055, 0.0149, 0.0394, 0.1003, 0.2326, 0.4518, 0.6914, 0.8589, 0.9430, 0.9783, 0.9919, 0.9970]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "V", X = 1421, Y = 750, W = 2, H = 2,
                IsCircular = false, Range = 1, RefVal = 0.9725, Weight = 0.7680,
                ProbTable = [0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0010, 0.0028, 0.0076, 0.0204, 0.0535, 0.1332, 0.2947, 0.5318, 0.7554, 0.8935, 0.9580, 0.9841, 0.9941, 0.9978]
            },
        }
    };

    /// <summary>
    /// 恰斯卡六槽位 × 五元素（风火水雷冰）子弹特征模型（6×5=30组）
    /// 索引：第一维为槽位 0-5；第二维对应 ChascaBulletType 的 Anemo/Pyro/Hydro/Electro/Cryo（1-5）
    /// 受子弹填充规则限制部分槽位仅部分元素有模型，缺失项为 null（识别时跳过，对应槽位判定为空）
    /// </summary>
    private static readonly FeatureScorerExportData?[,] _chascaBulletModels = new FeatureScorerExportData?[6, 5];

    /// <summary>
    /// 恰斯卡六槽位 × 五元素子弹特征模型填充（硬编码自自训练工具导出的 JSON）
    /// 受子弹填充规则限制，部分位置仅存在部分元素模型，缺失项保持 null（识别时跳过）
    /// </summary>
    static AvatarSpecialAction()
    {
        _chascaBulletModels[0, 0] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 957, Y = 131, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9476,
                    RefHist = [0.0190, 0.0088, 0.0007, 0, 0, 0, 0.0084, 0.9630],
                    ProbTable = [0.0003, 0.0007, 0.0020, 0.0054, 0.0145, 0.0385, 0.0982, 0.2284, 0.4459, 0.6863, 0.8560, 0.9417, 0.9777, 0.9917, 0.9969, 0.9989, 0.9996, 0.9998, 0.9999, 1, 1]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 900, Y = 170, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9399,
                    RefHist = [0.1059, 0, 0, 0, 0, 0, 0, 0.8941],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0052, 0.0141, 0.0375, 0.0958, 0.2236, 0.4390, 0.6803, 0.8526, 0.9402, 0.9771, 0.9915, 0.9968, 0.9988, 0.9996, 0.9998]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 958, Y = 174, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9303,
                    RefHist = [0.0375, 0, 0, 0, 0, 0.0015, 0.0145, 0.9465],
                    ProbTable = [0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0066, 0.0178, 0.0470, 0.1183, 0.2672, 0.4977, 0.7293, 0.8798, 0.9522, 0.9819, 0.9932, 0.9975, 0.9991, 0.9997, 0.9999, 1]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 902, Y = 192, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9194,
                    RefHist = [0, 0.0011, 0.0029, 0.0061, 0.0110, 0.0213, 0.0485, 0.9091],
                    ProbTable = [0.0001, 0.0003, 0.0008, 0.0021, 0.0057, 0.0154, 0.0409, 0.1038, 0.2395, 0.4612, 0.6994, 0.8635, 0.9450, 0.9790, 0.9922, 0.9971, 0.9989, 0.9996, 0.9999, 0.9999, 1]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 960, Y = 192, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9011,
                    RefHist = [0.0461, 0.0101, 0.0029, 0.0098, 0.0015, 0.0022, 0.0088, 0.9186],
                    ProbTable = [0.0004, 0.0010, 0.0027, 0.0072, 0.0194, 0.0510, 0.1276, 0.2844, 0.5193, 0.7460, 0.8887, 0.9560, 0.9833, 0.9938, 0.9977, 0.9992, 0.9997, 0.9999, 1, 1, 1]
                },
            }
        };

        _chascaBulletModels[1, 0] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1004, Y = 156, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7475,
                    RefHist = [0.9330, 0.0031, 0.0018, 0, 0, 0, 0, 0.0620],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0044, 0.0120, 0.0319, 0.0822, 0.1958, 0.3982, 0.6427, 0.8302, 0.9300, 0.9731, 0.9899, 0.9963, 0.9986, 0.9995, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1065, Y = 155, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7357,
                    RefHist = [0.9658, 0, 0, 0, 0.0092, 0.0121, 0, 0.0129],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0045, 0.0120, 0.0320, 0.0825, 0.1965, 0.3993, 0.6437, 0.8308, 0.9303, 0.9732, 0.9900, 0.9963, 0.9986, 0.9995, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1005, Y = 161, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8250,
                    RefHist = [0.9885, 0.0036, 0.0041, 0, 0, 0, 0, 0.0038],
                    ProbTable = [0, 0.0001, 0.0003, 0.0007, 0.0020, 0.0055, 0.0148, 0.0393, 0.1000, 0.2320, 0.4509, 0.6906, 0.8585, 0.9428, 0.9782, 0.9919, 0.9970, 0.9989, 0.9996, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1054, Y = 173, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.7882,
                    RefHist = [0.9161, 0.0080, 0.0016, 0, 0, 0, 0.0036, 0.0707],
                    ProbTable = [0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0057, 0.0152, 0.0404, 0.1027, 0.2372, 0.4581, 0.6968, 0.8620, 0.9444, 0.9788, 0.9921, 0.9971, 0.9989, 0.9996]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1062, Y = 171, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8304,
                    RefHist = [1, 0, 0, 0, 0, 0, 0, 0],
                    ProbTable = [0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0067, 0.0180, 0.0474, 0.1192, 0.2689, 0.5000, 0.7311, 0.8808, 0.9526, 0.9820, 0.9933, 0.9975, 0.9991, 0.9997, 0.9999, 1]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1055, Y = 203, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7383,
                    RefHist = [0.9232, 0.0300, 0, 0.0007, 0, 0, 0, 0.0461],
                    ProbTable = [0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0051, 0.0139, 0.0368, 0.0941, 0.2202, 0.4343, 0.6760, 0.8501, 0.9391, 0.9767, 0.9913, 0.9968, 0.9988, 0.9996, 0.9998, 0.9999]
                },
            }
        };

        _chascaBulletModels[1, 1] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1020, Y = 152, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.5116,
                    RefHist = [0.0654, 0.7992, 0.0327, 0, 0, 0.0091, 0.0865, 0.0072],
                    ProbTable = [0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0067, 0.0180, 0.0475, 0.1195, 0.2695, 0.5007, 0.7316, 0.8811, 0.9527, 0.9821, 0.9933, 0.9975, 0.9991, 0.9997]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1009, Y = 184, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.5937,
                    RefHist = [0.1102, 0.8112, 0.0181, 0, 0, 0.0417, 0.0107, 0.0081],
                    ProbTable = [0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0037, 0.0099, 0.0264, 0.0688, 0.1672, 0.3530, 0.5973, 0.8013, 0.9164, 0.9675, 0.9878, 0.9955, 0.9983, 0.9994, 0.9998]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1057, Y = 191, W = 3, H = 3,
                    IsCircular = true, Range = 360, RefVal = 57.0119, Weight = 0.9037,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0004, 0.0010, 0.0026, 0.0070, 0.0189, 0.0497, 0.1244, 0.2786, 0.5121, 0.7405, 0.8858, 0.9547]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1054, Y = 203, W = 3, H = 3,
                    IsCircular = true, Range = 360, RefVal = 46.1490, Weight = 0.8761,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0020, 0.0055, 0.0149, 0.0394, 0.1003, 0.2325, 0.4516, 0.6912, 0.8588, 0.9430]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1018, Y = 220, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 50.6456, Weight = 0.7558,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0010, 0.0027, 0.0074, 0.0199, 0.0524, 0.1306, 0.2900, 0.5261, 0.7511, 0.8914]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1030, Y = 237, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 53.3748, Weight = 0.8117,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0038, 0.0102, 0.0273, 0.0709, 0.1719, 0.3607, 0.6053, 0.8065, 0.9189]
                },
            }
        };

        _chascaBulletModels[1, 2] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1025, Y = 166, W = 3, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.8137, Weight = 0.7406,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0058, 0.0157, 0.0415, 0.1054, 0.2426, 0.4654, 0.7029, 0.8655, 0.9459]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1054, Y = 164, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6840,
                    RefHist = [0.0053, 0, 0.0013, 0, 0, 0.8834, 0.1032, 0.0069],
                    ProbTable = [0, 0, 0, 0.0001, 0.0004, 0.0010, 0.0026, 0.0070, 0.0188, 0.0496, 0.1243, 0.2784, 0.5119, 0.7403, 0.8857, 0.9547, 0.9828, 0.9936, 0.9976, 0.9991, 0.9997]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1024, Y = 192, W = 3, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.8058, Weight = 0.6458,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0038, 0.0104, 0.0277, 0.0720, 0.1741, 0.3643, 0.6091, 0.8090, 0.9201]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1020, Y = 193, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7597,
                    RefHist = [0.9243, 0.0021, 0.0203, 0, 0.0142, 0.0270, 0, 0.0121],
                    ProbTable = [0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0050, 0.0134, 0.0355, 0.0911, 0.2140, 0.4253, 0.6680, 0.8454, 0.9370, 0.9759, 0.9910, 0.9967, 0.9988, 0.9995, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1020, Y = 193, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6769,
                    RefHist = [0.9047, 0.0085, 0.0089, 0, 0.0063, 0.0292, 0.0057, 0.0367],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0167, 0.0442, 0.1117, 0.2547, 0.4816, 0.7164, 0.8729, 0.9491, 0.9807, 0.9928, 0.9973, 0.9990, 0.9996, 0.9999]
                },
            }
        };

        _chascaBulletModels[1, 3] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1034, Y = 146, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 297.9096, Weight = 0.8501,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0010, 0.0028, 0.0075, 0.0202, 0.0531, 0.1322, 0.2929, 0.5296, 0.7537, 0.8927, 0.9576, 0.9840]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1049, Y = 151, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 296.3803, Weight = 0.8617,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0039, 0.0106, 0.0283, 0.0733, 0.1769, 0.3687, 0.6135, 0.8119, 0.9215, 0.9696]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1001, Y = 188, W = 4, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7262,
                    RefHist = [0.0230, 0, 0, 0, 0, 0, 0.0043, 0.9727],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0011, 0.0031, 0.0084, 0.0225, 0.0588, 0.1453, 0.3160, 0.5567, 0.7734, 0.9027, 0.9619, 0.9856]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 997, Y = 207, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 300.5410, Weight = 0.8720,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0033, 0.0089, 0.0237, 0.0620, 0.1522, 0.3280, 0.5702, 0.7829, 0.9074, 0.9638]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1030, Y = 204, W = 3, H = 4,
                    IsCircular = false, Range = 1, Weight = 0.6446,
                    RefHist = [0.0152, 0.0079, 0, 0.0002, 0.9617, 0.0063, 0.0086, 0.0001],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0032, 0.0088, 0.0234, 0.0613, 0.1507, 0.3253, 0.5672, 0.7808, 0.9064, 0.9634, 0.9862]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1009, Y = 227, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 301.3971, Weight = 0.9224,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0052, 0.0139, 0.0369, 0.0943, 0.2206, 0.4349, 0.6765, 0.8504, 0.9392, 0.9768, 0.9913]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1026, Y = 231, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 300.8885, Weight = 0.8906,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0078, 0.0210, 0.0552, 0.1371, 0.3016, 0.5400, 0.7614, 0.8966, 0.9593, 0.9846]
                },
            }
        };

        _chascaBulletModels[1, 4] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1018, Y = 154, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9221,
                    RefHist = [0.0053, 0.0128, 0.0217, 0.0187, 0.0030, 0, 0.0277, 0.9108],
                    ProbTable = [0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0053, 0.0142, 0.0376, 0.0960, 0.2241, 0.4398, 0.6809, 0.8530, 0.9404, 0.9772, 0.9915, 0.9969, 0.9988, 0.9996, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1020, Y = 154, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9064,
                    RefHist = [0, 0, 0.0217, 0.0184, 0.0034, 0.0128, 0.0511, 0.8927],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0009, 0.0023, 0.0063, 0.0170, 0.0448, 0.1131, 0.2575, 0.4853, 0.7193, 0.8745, 0.9498, 0.9809, 0.9929, 0.9974, 0.9990, 0.9996, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1017, Y = 174, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7842,
                    RefHist = [0.0054, 0.0164, 0.0414, 0.8327, 0.0834, 0.0055, 0.0065, 0.0086],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0068, 0.0182, 0.0481, 0.1207, 0.2717, 0.5034, 0.7338, 0.8822, 0.9532, 0.9823, 0.9934, 0.9976, 0.9991, 0.9997, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1021, Y = 174, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8370,
                    RefHist = [0.0119, 0.0142, 0.0417, 0.8721, 0.0427, 0, 0, 0.0174],
                    ProbTable = [0, 0.0001, 0.0002, 0.0005, 0.0012, 0.0033, 0.0090, 0.0242, 0.0631, 0.1548, 0.3324, 0.5751, 0.7863, 0.9091, 0.9645, 0.9866, 0.9950, 0.9982, 0.9993, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1009, Y = 184, W = 2, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.1473, Weight = 0.7278,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0032, 0.0086, 0.0231, 0.0604, 0.1487, 0.3219, 0.5634, 0.7781, 0.9051, 0.9628, 0.9860]
                },
            }
        };

        _chascaBulletModels[2, 0] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1125, Y = 168, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6424,
                    RefHist = [0.5377, 0.0067, 0, 0, 0, 0, 0, 0.4556],
                    ProbTable = [0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0035, 0.0095, 0.0254, 0.0661, 0.1615, 0.3436, 0.5872, 0.7945, 0.9131, 0.9662, 0.9873, 0.9953, 0.9983, 0.9994]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1113, Y = 179, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7200,
                    RefHist = [0.9604, 0.0047, 0, 0, 0, 0.0112, 0.0105, 0.0131],
                    ProbTable = [0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0051, 0.0138, 0.0366, 0.0937, 0.2194, 0.4331, 0.6749, 0.8495, 0.9388, 0.9766, 0.9913, 0.9968, 0.9988, 0.9996, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1098, Y = 208, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6463,
                    RefHist = [0.0180, 0.9603, 0, 0, 0.0089, 0.0075, 0.0053, 0],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0015, 0.0041, 0.0111, 0.0296, 0.0765, 0.1838, 0.3798, 0.6247, 0.8190, 0.9248, 0.9710, 0.9891, 0.9960, 0.9985, 0.9995, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1166, Y = 203, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6490,
                    RefHist = [0.9299, 0.0440, 0, 0, 0, 0, 0, 0.0261],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0015, 0.0041, 0.0110, 0.0293, 0.0759, 0.1824, 0.3775, 0.6225, 0.8176, 0.9241, 0.9707, 0.9890, 0.9959, 0.9985, 0.9994, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1092, Y = 215, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6412,
                    RefHist = [0.9383, 0.0271, 0, 0, 0, 0.0152, 0.0006, 0.0188],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0018, 0.0048, 0.0128, 0.0342, 0.0877, 0.2072, 0.4153, 0.6588, 0.8399, 0.9345, 0.9749, 0.9906, 0.9965, 0.9987, 0.9995, 0.9998, 0.9999]
                },
            }
        };

        _chascaBulletModels[2, 1] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1130, Y = 155, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 17.4872, Weight = 0.6717,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0026, 0.0070, 0.0187, 0.0493, 0.1236, 0.2772, 0.5104, 0.7391, 0.8851, 0.9544, 0.9827]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1102, Y = 181, W = 3, H = 2,
                    IsCircular = true, Range = 360, RefVal = 22.5861, Weight = 0.6730,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0061, 0.0163, 0.0432, 0.1092, 0.2500, 0.4754, 0.7112, 0.8701, 0.9479, 0.9802]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1101, Y = 205, W = 2, H = 4,
                    IsCircular = true, Range = 360, RefVal = 58.0607, Weight = 0.9474,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0018, 0.0050, 0.0135, 0.0358, 0.0917, 0.2154, 0.4273, 0.6698, 0.8465, 0.9375, 0.9760]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1126, Y = 202, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 42.9309, Weight = 0.8134,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0035, 0.0095, 0.0255, 0.0663, 0.1618, 0.3442, 0.5879, 0.7950, 0.9134]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1098, Y = 211, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 57.4080, Weight = 0.9592,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0026, 0.0069, 0.0186, 0.0489, 0.1227, 0.2755, 0.5082, 0.7375, 0.8842, 0.9540, 0.9826]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1101, Y = 247, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 50.5667, Weight = 0.7736,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0080, 0.0213, 0.0559, 0.1387, 0.3045, 0.5434, 0.7639, 0.8979]
                },
            }
        };

        _chascaBulletModels[2, 2] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1127, Y = 186, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.6806,
                    RefHist = [0.0103, 0.0079, 0.0237, 0.9212, 0.0283, 0.0007, 0.0053, 0.0025],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0034, 0.0093, 0.0249, 0.0650, 0.1590, 0.3394, 0.5827, 0.7915, 0.9117, 0.9656, 0.9871, 0.9952]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1124, Y = 199, W = 3, H = 4,
                    IsCircular = false, Range = 1, RefVal = 0.8108, Weight = 0.7387,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0061, 0.0165, 0.0436, 0.1104, 0.2522, 0.4783, 0.7136, 0.8714, 0.9485]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1146, Y = 207, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.6503,
                    RefHist = [0.0104, 0, 0.0022, 0, 0.0049, 0.1088, 0.8587, 0.0151],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0037, 0.0100, 0.0267, 0.0693, 0.1684, 0.3549, 0.5993, 0.8026, 0.9170, 0.9678, 0.9879, 0.9955, 0.9983]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1155, Y = 204, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6628,
                    RefHist = [0.0008, 0, 0, 0, 0.0004, 0.4281, 0.5395, 0.0312],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0058, 0.0155, 0.0412, 0.1045, 0.2408, 0.4629, 0.7009, 0.8643, 0.9454, 0.9792, 0.9922, 0.9971, 0.9989]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1114, Y = 213, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6825,
                    RefHist = [0, 0.0056, 0.9387, 0.0229, 0.0130, 0.0142, 0, 0.0057],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0042, 0.0114, 0.0305, 0.0787, 0.1884, 0.3869, 0.6317, 0.8234, 0.9269, 0.9718, 0.9894, 0.9961, 0.9986, 0.9995, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1153, Y = 210, W = 2, H = 3,
                    IsCircular = false, Range = 1, RefVal = 0.7721, Weight = 0.6727,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0035, 0.0096, 0.0256, 0.0666, 0.1624, 0.3451, 0.5889, 0.7957, 0.9137, 0.9664]
                },
            }
        };

        _chascaBulletModels[2, 3] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1108, Y = 186, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 293.1448, Weight = 0.7760,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0004, 0.0010, 0.0026, 0.0070, 0.0189, 0.0498, 0.1247, 0.2792, 0.5129, 0.7411, 0.8861]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1154, Y = 181, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 285.1881, Weight = 0.8015,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0056, 0.0151, 0.0401, 0.1020, 0.2358, 0.4562, 0.6952, 0.8611, 0.9440, 0.9786]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1095, Y = 222, W = 2, H = 4,
                    IsCircular = true, Range = 360, RefVal = 288.0810, Weight = 0.7398,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0060, 0.0160, 0.0424, 0.1075, 0.2467, 0.4710, 0.7076, 0.8680]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1102, Y = 215, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7068,
                    RefHist = [0.9558, 0.0324, 0, 0, 0.0005, 0, 0, 0.0113],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0030, 0.0081, 0.0217, 0.0569, 0.1409, 0.3084, 0.5479, 0.7671, 0.8995, 0.9605, 0.9851, 0.9945, 0.9980]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1134, Y = 223, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.7302,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.1093, 0.8907],
                    ProbTable = [0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0067, 0.0181, 0.0477, 0.1197, 0.2700, 0.5013, 0.7321, 0.8813, 0.9528, 0.9821, 0.9933, 0.9975, 0.9991, 0.9997]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1101, Y = 242, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 293.8095, Weight = 0.8405,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0038, 0.0102, 0.0271, 0.0705, 0.1709, 0.3591, 0.6036, 0.8054, 0.9184, 0.9683]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1116, Y = 244, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 282.2734, Weight = 0.7461,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0166, 0.0440, 0.1111, 0.2537, 0.4802, 0.7152, 0.8722]
                },
            }
        };

        _chascaBulletModels[2, 4] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1123, Y = 181, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9092,
                    RefHist = [0.8921, 0.0214, 0.0222, 0.0075, 0.0222, 0.0086, 0.0195, 0.0065],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0017, 0.0046, 0.0125, 0.0333, 0.0857, 0.2030, 0.4091, 0.6530, 0.8365, 0.9329, 0.9742, 0.9904, 0.9964, 0.9987, 0.9995, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1115, Y = 204, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7889,
                    RefHist = [0.0052, 0.0235, 0.0042, 0.0515, 0.8684, 0.0240, 0.0190, 0.0041],
                    ProbTable = [0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0037, 0.0099, 0.0265, 0.0688, 0.1673, 0.3532, 0.5975, 0.8014, 0.9165, 0.9676, 0.9878, 0.9955, 0.9983, 0.9994, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1104, Y = 212, W = 3, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.1415, Weight = 0.7423,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0069, 0.0185, 0.0486, 0.1220, 0.2741, 0.5065, 0.7362, 0.8835, 0.9537, 0.9825]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1100, Y = 221, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7227,
                    RefHist = [0, 0.0015, 0.0037, 0.0076, 0.0593, 0.7997, 0.1282, 0],
                    ProbTable = [0, 0, 0.0001, 0.0002, 0.0006, 0.0017, 0.0046, 0.0123, 0.0328, 0.0843, 0.2001, 0.4048, 0.6490, 0.8340, 0.9318, 0.9738, 0.9902, 0.9964, 0.9987, 0.9995, 0.9998]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1115, Y = 231, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6990,
                    RefHist = [0, 0.0033, 0.0266, 0.7967, 0.1279, 0.0171, 0.0230, 0.0055],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0007, 0.0020, 0.0054, 0.0146, 0.0388, 0.0988, 0.2297, 0.4476, 0.6878, 0.8569, 0.9421, 0.9779, 0.9918, 0.9970, 0.9989, 0.9996, 0.9998]
                },
            }
        };

        _chascaBulletModels[3, 1] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1191, Y = 241, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 45.5141, Weight = 0.6602,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0010, 0.0028, 0.0076, 0.0203, 0.0533, 0.1328, 0.2940, 0.5309, 0.7547, 0.8932]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1170, Y = 264, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 47.5535, Weight = 0.7783,
                    ProbTable = [0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0035, 0.0095, 0.0254, 0.0662, 0.1616, 0.3439, 0.5876, 0.7948, 0.9132, 0.9662, 0.9873, 0.9953, 0.9983, 0.9994]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1192, Y = 255, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 55.4849, Weight = 0.6393,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0042, 0.0114, 0.0304, 0.0786, 0.1882, 0.3865, 0.6314, 0.8232]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1204, Y = 256, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6521,
                    RefHist = [0.0119, 0.0052, 0, 0, 0, 0.0076, 0.0468, 0.9285],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0050, 0.0135, 0.0357, 0.0915, 0.2150, 0.4268, 0.6693, 0.8462, 0.9373, 0.9760, 0.9910, 0.9967, 0.9988]
                },
            }
        };

        _chascaBulletModels[3, 2] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1191, Y = 263, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6463,
                    RefHist = [0.0081, 0.0199, 0, 0, 0.0311, 0.8907, 0.0363, 0.0138],
                    ProbTable = [0, 0.0001, 0.0003, 0.0009, 0.0023, 0.0064, 0.0171, 0.0452, 0.1139, 0.2590, 0.4872, 0.7208, 0.8753, 0.9502, 0.9811, 0.9930, 0.9974, 0.9990, 0.9996, 0.9999, 1]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1214, Y = 259, W = 3, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.8080, Weight = 0.7219,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0011, 0.0031, 0.0083, 0.0221, 0.0580, 0.1434, 0.3127, 0.5529, 0.7707, 0.9013, 0.9613]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1224, Y = 265, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6410,
                    RefHist = [0.0062, 0, 0, 0.0036, 0.0034, 0.9130, 0.0324, 0.0415],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0064, 0.0173, 0.0457, 0.1152, 0.2614, 0.4903, 0.7234, 0.8767, 0.9508, 0.9813, 0.9930, 0.9974, 0.9991, 0.9997, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1231, Y = 276, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6483,
                    RefHist = [0.0152, 0, 0.0036, 0.0263, 0.0309, 0.0157, 0.0301, 0.8782],
                    ProbTable = [0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0033, 0.0088, 0.0236, 0.0618, 0.1518, 0.3272, 0.5693, 0.7823, 0.9071, 0.9637, 0.9863, 0.9949, 0.9981, 0.9993, 0.9997, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1213, Y = 297, W = 3, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7555,
                    RefHist = [0, 0, 0.0228, 0.9410, 0.0175, 0.0106, 0.0065, 0.0016],
                    ProbTable = [0, 0.0001, 0.0002, 0.0006, 0.0015, 0.0041, 0.0112, 0.0298, 0.0770, 0.1848, 0.3812, 0.6261, 0.8199, 0.9252, 0.9711, 0.9892, 0.9960, 0.9985, 0.9995, 0.9998, 0.9999]
                },
            }
        };

        _chascaBulletModels[3, 3] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1208, Y = 250, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8912,
                    RefHist = [0, 0, 0, 0, 0, 1, 0, 0],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0058, 0.0157, 0.0415, 0.1054, 0.2425, 0.4653, 0.7029, 0.8654, 0.9459]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1192, Y = 271, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8313,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.0440, 0.9560],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0037, 0.0099, 0.0265, 0.0689, 0.1674, 0.3534, 0.5977, 0.8015, 0.9165, 0.9676, 0.9878, 0.9955]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1244, Y = 277, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 303.9134, Weight = 0.7746,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0026, 0.0069, 0.0186, 0.0491, 0.1231, 0.2762, 0.5092, 0.7382, 0.8846, 0.9542]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1238, Y = 272, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8872,
                    RefHist = [0, 0, 0, 0.9767, 0.0233, 0, 0, 0],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0051, 0.0138, 0.0365, 0.0934, 0.2188, 0.4323, 0.6742, 0.8491, 0.9386, 0.9765, 0.9912, 0.9968]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1212, Y = 294, W = 3, H = 4,
                    IsCircular = false, Range = 1, Weight = 0.7811,
                    RefHist = [0.0037, 0, 0, 0, 0, 0, 0.0596, 0.9366],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0033, 0.0088, 0.0236, 0.0617, 0.1516, 0.3269, 0.5690, 0.7821, 0.9070, 0.9637, 0.9863]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1213, Y = 293, W = 2, H = 4,
                    IsCircular = false, Range = 1, Weight = 0.8437,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.0469, 0.9531],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0067, 0.0180, 0.0474, 0.1190, 0.2687, 0.4996, 0.7308, 0.8806, 0.9525, 0.9820]
                },
            }
        };

        _chascaBulletModels[3, 4] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "S", X = 1188, Y = 263, W = 2, H = 3,
                    IsCircular = false, Range = 1, RefVal = 0.1251, Weight = 0.7697,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0065, 0.0174, 0.0460, 0.1158, 0.2626, 0.4918, 0.7246, 0.8773, 0.9511, 0.9814]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1204, Y = 265, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.8163,
                    RefHist = [0.0111, 0.0252, 0.0005, 0.0095, 0.0594, 0.8443, 0.0364, 0.0138],
                    ProbTable = [0, 0.0001, 0.0001, 0.0004, 0.0010, 0.0028, 0.0075, 0.0202, 0.0530, 0.1321, 0.2927, 0.5293, 0.7535, 0.8926, 0.9576, 0.9840, 0.9940, 0.9978, 0.9992, 0.9997, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1183, Y = 273, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8254,
                    RefHist = [0.0026, 0, 0, 0, 0.0059, 0.0326, 0.9068, 0.0521],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0068, 0.0183, 0.0483, 0.1213, 0.2728, 0.5049, 0.7349, 0.8828, 0.9534, 0.9824, 0.9934, 0.9976]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1192, Y = 288, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7851,
                    RefHist = [0.0045, 0, 0, 0, 0.8841, 0.0527, 0.0587, 0],
                    ProbTable = [0, 0, 0.0001, 0.0002, 0.0005, 0.0015, 0.0039, 0.0106, 0.0284, 0.0737, 0.1778, 0.3702, 0.6150, 0.8128, 0.9219, 0.9698, 0.9887, 0.9958, 0.9985, 0.9994, 0.9998]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1195, Y = 296, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9100,
                    RefHist = [0.8932, 0.0035, 0, 0, 0, 0.0649, 0.0228, 0.0157],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0061, 0.0164, 0.0433, 0.1095, 0.2505, 0.4760, 0.7117, 0.8703, 0.9480, 0.9802, 0.9926, 0.9973, 0.9990, 0.9996, 0.9999]
                },
            }
        };

        _chascaBulletModels[4, 1] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1240, Y = 353, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8586,
                    RefHist = [0.0408, 0, 0.8831, 0.0089, 0.0531, 0.0141, 0, 0],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0059, 0.0158, 0.0419, 0.1062, 0.2442, 0.4676, 0.7048, 0.8665, 0.9464, 0.9796, 0.9924, 0.9972, 0.9990, 0.9996, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1258, Y = 356, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9248,
                    RefHist = [0, 0, 0, 0, 0, 0.9161, 0.0839, 0],
                    ProbTable = [0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0033, 0.0088, 0.0236, 0.0617, 0.1516, 0.3269, 0.5690, 0.7821, 0.9070, 0.9637, 0.9863, 0.9949, 0.9981, 0.9993]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1275, Y = 350, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9095,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.0640, 0.9360],
                    ProbTable = [0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0060, 0.0162, 0.0429, 0.1086, 0.2487, 0.4737, 0.7098, 0.8693, 0.9476, 0.9801, 0.9926, 0.9973, 0.9990, 0.9996]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1261, Y = 361, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9310,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.0494, 0.9506],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0078, 0.0209, 0.0548, 0.1362, 0.3001, 0.5382, 0.7601, 0.8960, 0.9590, 0.9845, 0.9943]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1262, Y = 383, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8480,
                    RefHist = [0, 0, 0, 0.0087, 0.9329, 0.0518, 0, 0.0067],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0044, 0.0118, 0.0313, 0.0809, 0.1930, 0.3939, 0.6386, 0.8277, 0.9289, 0.9726, 0.9897, 0.9962, 0.9986]
                },
            }
        };

        _chascaBulletModels[4, 2] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1286, Y = 337, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9560,
                    RefHist = [0, 0, 0.0435, 0, 0.0567, 0.8998, 0, 0],
                    ProbTable = [0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0212, 0.0556, 0.1381, 0.3033, 0.5420, 0.7629, 0.8974, 0.9596, 0.9848, 0.9943, 0.9979, 0.9992, 0.9997, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1258, Y = 354, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8562,
                    RefHist = [0.0099, 0, 0, 0.8730, 0.0094, 0.0081, 0.0997, 0],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0057, 0.0153, 0.0406, 0.1031, 0.2382, 0.4594, 0.6979, 0.8626, 0.9447, 0.9789, 0.9921, 0.9971, 0.9989, 0.9996, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1260, Y = 353, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8798,
                    RefHist = [0, 0, 0, 0, 0.9271, 0, 0.0729, 0],
                    ProbTable = [0, 0.0001, 0.0002, 0.0005, 0.0012, 0.0033, 0.0090, 0.0240, 0.0627, 0.1539, 0.3309, 0.5734, 0.7851, 0.9085, 0.9643, 0.9866, 0.9950, 0.9982, 0.9993, 0.9998, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1290, Y = 340, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8291,
                    RefHist = [0.0312, 0.0123, 0, 0, 0, 0.9082, 0.0483, 0],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0061, 0.0164, 0.0434, 0.1097, 0.2509, 0.4765, 0.7122, 0.8706, 0.9481, 0.9803, 0.9927, 0.9973, 0.9990, 0.9996, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1263, Y = 365, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8280,
                    RefHist = [0, 0, 0, 0.8696, 0.0435, 0.0382, 0.0488, 0],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0008, 0.0022, 0.0060, 0.0162, 0.0429, 0.1087, 0.2489, 0.4739, 0.7101, 0.8694, 0.9476, 0.9801, 0.9926, 0.9973, 0.9990, 0.9996, 0.9999]
                },
            }
        };

        _chascaBulletModels[4, 3] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1264, Y = 324, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 297.8732, Weight = 0.9017,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0026, 0.0070, 0.0187, 0.0492, 0.1233, 0.2766, 0.5097, 0.7386, 0.8848, 0.9543]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1278, Y = 329, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9936,
                    RefHist = [0, 0, 0, 0, 0, 0.9333, 0.0667, 0],
                    ProbTable = [0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0068, 0.0183, 0.0482, 0.1209, 0.2722, 0.5041, 0.7342, 0.8825, 0.9533, 0.9823, 0.9934, 0.9976, 0.9991, 0.9997, 0.9999]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1277, Y = 329, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.9513,
                    RefHist = [0, 0, 0, 0, 0, 0.9497, 0.0503, 0],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0066, 0.0178, 0.0469, 0.1179, 0.2665, 0.4969, 0.7286, 0.8795, 0.9520, 0.9818, 0.9932, 0.9975]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1268, Y = 360, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8958,
                    RefHist = [0, 0, 0, 0.8830, 0.1170, 0, 0, 0],
                    ProbTable = [0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0065, 0.0174, 0.0459, 0.1157, 0.2624, 0.4917, 0.7245, 0.8773, 0.9510, 0.9814, 0.9931, 0.9974, 0.9991, 0.9997]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1259, Y = 397, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 302.4902, Weight = 0.8955,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0025, 0.0069, 0.0185, 0.0487, 0.1222, 0.2745, 0.5070, 0.7365, 0.8837, 0.9538]
                },
            }
        };

        _chascaBulletModels[4, 4] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1279, Y = 335, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.7147,
                    RefHist = [0.0057, 0.0059, 0, 0, 0.0067, 0.0649, 0.0547, 0.8621],
                    ProbTable = [0, 0, 0.0001, 0.0001, 0.0004, 0.0010, 0.0028, 0.0076, 0.0205, 0.0538, 0.1338, 0.2958, 0.5331, 0.7563, 0.8940, 0.9582, 0.9842, 0.9941, 0.9978, 0.9992, 0.9997]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1241, Y = 344, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8480,
                    RefHist = [0, 0.0073, 0, 0.0061, 0, 0, 0.0940, 0.8926],
                    ProbTable = [0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0032, 0.0085, 0.0228, 0.0598, 0.1473, 0.3196, 0.5607, 0.7763, 0.9041, 0.9625, 0.9859, 0.9947, 0.9981, 0.9993, 0.9997]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1262, Y = 344, W = 4, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7514,
                    RefHist = [0.0290, 0.0037, 0.0178, 0.0304, 0.0208, 0.8473, 0, 0.0509],
                    ProbTable = [0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0036, 0.0096, 0.0257, 0.0668, 0.1629, 0.3459, 0.5898, 0.7963, 0.9140, 0.9665, 0.9874, 0.9953, 0.9983, 0.9994, 0.9998]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1246, Y = 361, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.8379,
                    RefHist = [0, 0, 0, 0, 0, 0.8523, 0.1383, 0.0094],
                    ProbTable = [0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0036, 0.0097, 0.0258, 0.0672, 0.1638, 0.3474, 0.5913, 0.7973, 0.9145, 0.9667, 0.9875, 0.9954, 0.9983, 0.9994]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1256, Y = 371, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.7499,
                    RefHist = [0.0229, 0, 0.8400, 0, 0.0045, 0, 0.1231, 0.0096],
                    ProbTable = [0, 0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0043, 0.0117, 0.0311, 0.0801, 0.1915, 0.3916, 0.6364, 0.8263, 0.9282, 0.9723, 0.9896, 0.9962, 0.9986, 0.9995, 0.9998]
                },
            }
        };

        _chascaBulletModels[5, 1] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1301, Y = 418, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 55.0451, Weight = 0.4798,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0009, 0.0024, 0.0065, 0.0176, 0.0463, 0.1166, 0.2641, 0.4938, 0.7262]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1295, Y = 444, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.5925,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.2000, 0.8000],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0032, 0.0086, 0.0230, 0.0602, 0.1483, 0.3212, 0.5626, 0.7776, 0.9048, 0.9627, 0.9860, 0.9948, 0.9981]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1335, Y = 441, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.4305,
                    RefHist = [0, 0, 0, 0, 0, 0.7282, 0.2718, 0],
                    ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0036, 0.0098, 0.0262, 0.0681, 0.1656, 0.3505, 0.5946, 0.7995, 0.9155, 0.9672, 0.9877, 0.9954]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1306, Y = 468, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.4337,
                    RefHist = [0, 0.1921, 0.7900, 0.0179, 0, 0, 0, 0],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0017, 0.0047, 0.0126, 0.0335, 0.0860, 0.2037, 0.4101, 0.6540, 0.8371, 0.9332, 0.9743, 0.9904]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1315, Y = 464, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.4794,
                    RefHist = [0.0373, 0, 0, 0, 0.1571, 0.0429, 0, 0.7627],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0012, 0.0034, 0.0091, 0.0243, 0.0633, 0.1552, 0.3331, 0.5759, 0.7868, 0.9094, 0.9646, 0.9867, 0.9951, 0.9982]
                },
            }
        };

        _chascaBulletModels[5, 2] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1284, Y = 416, W = 2, H = 3,
                    IsCircular = false, Range = 1, RefVal = 0.9705, Weight = 0.7583,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0013, 0.0036, 0.0097, 0.0261, 0.0678, 0.1651, 0.3496, 0.5937, 0.7988, 0.9152, 0.9670]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1308, Y = 424, W = 2, H = 3,
                    IsCircular = false, Range = 1, RefVal = 0.9633, Weight = 0.7801,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0015, 0.0040, 0.0107, 0.0286, 0.0741, 0.1787, 0.3717, 0.6166, 0.8138, 0.9224, 0.9700]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1310, Y = 423, W = 2, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.9660, Weight = 0.7802,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0049, 0.0132, 0.0350, 0.0897, 0.2113, 0.4214, 0.6644, 0.8433, 0.9360, 0.9755]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1308, Y = 466, W = 2, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.9685, Weight = 0.7711,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0056, 0.0151, 0.0399, 0.1016, 0.2351, 0.4552, 0.6943, 0.8606, 0.9438]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1344, Y = 458, W = 2, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.9693, Weight = 0.8149,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0004, 0.0010, 0.0026, 0.0070, 0.0189, 0.0498, 0.1248, 0.2793, 0.5131, 0.7412, 0.8862, 0.9549]
                },
            }
        };

        _chascaBulletModels[5, 3] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1321, Y = 412, W = 2, H = 2,
                    IsCircular = true, Range = 360, RefVal = 304.3371, Weight = 0.6572,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0004, 0.0010, 0.0026, 0.0070, 0.0189, 0.0498, 0.1247, 0.2791, 0.5128, 0.7410, 0.8861, 0.9548]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1287, Y = 459, W = 3, H = 4,
                    IsCircular = false, Range = 1, Weight = 0.7309,
                    RefHist = [0, 0, 0, 0, 0, 0, 0, 1],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0020, 0.0054, 0.0145, 0.0385, 0.0980, 0.2281, 0.4454, 0.6859, 0.8558]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "V", X = 1290, Y = 470, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6873,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.0885, 0.9115],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0017, 0.0047, 0.0127, 0.0338, 0.0869, 0.2054, 0.4127, 0.6564, 0.8385, 0.9338]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1324, Y = 480, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 303.1515, Weight = 0.7995,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0039, 0.0105, 0.0281, 0.0728, 0.1760, 0.3673, 0.6121, 0.8109, 0.9210]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "H", X = 1331, Y = 476, W = 2, H = 3,
                    IsCircular = true, Range = 360, RefVal = 307.8668, Weight = 0.7945,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0004, 0.0012, 0.0033, 0.0089, 0.0238, 0.0621, 0.1525, 0.3285, 0.5708, 0.7833, 0.9076, 0.9639]
                },
            }
        };

        _chascaBulletModels[5, 4] = new FeatureScorerExportData
        {
            Features =
            {
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1278, Y = 425, W = 2, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.9621, Weight = 0.5377,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0061, 0.0165, 0.0436, 0.1102, 0.2520, 0.4780, 0.7134, 0.8712, 0.9484]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1286, Y = 434, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.5379,
                    RefHist = [0.1111, 0.1111, 0, 0, 0, 0, 0, 0.7778],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0044, 0.0119, 0.0318, 0.0819, 0.1951, 0.3971, 0.6417, 0.8296, 0.9297, 0.9729, 0.9899, 0.9963, 0.9986]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1314, Y = 433, W = 2, H = 3,
                    IsCircular = false, Range = 1, Weight = 0.5572,
                    RefHist = [0.0031, 0, 0, 0, 0.0352, 0.1731, 0.7792, 0.0093],
                    ProbTable = [0, 0, 0, 0, 0.0001, 0.0002, 0.0006, 0.0016, 0.0043, 0.0116, 0.0310, 0.0800, 0.1913, 0.3913, 0.6360, 0.8261, 0.9281, 0.9723, 0.9896, 0.9962, 0.9986]
                },
                new FeatureScorerItem
                {
                    Type = "F2", Channel = "S", X = 1290, Y = 450, W = 2, H = 2,
                    IsCircular = false, Range = 1, Weight = 0.6088,
                    RefHist = [0, 0, 0, 0, 0, 0, 0.9333, 0.0667],
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0038, 0.0102, 0.0274, 0.0711, 0.1722, 0.3612, 0.6058, 0.8069, 0.9191, 0.9686]
                },
                new FeatureScorerItem
                {
                    Type = "F1", Channel = "V", X = 1278, Y = 486, W = 3, H = 2,
                    IsCircular = false, Range = 1, RefVal = 0.9556, Weight = 0.5518,
                    ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0005, 0.0014, 0.0037, 0.0101, 0.0269, 0.0699, 0.1696, 0.3570, 0.6015, 0.8040, 0.9177, 0.9681]
                },
            }
        };
    }



    /// <summary>
    /// 桑多涅特化叠加层目标框共享画笔（避免每帧新建 Pen 导致 GDI+ 句柄抖动）
    /// </summary>
    private static readonly System.Drawing.Pen _targetPen = new(System.Drawing.Color.LimeGreen, 2);

    /// <summary>
    /// 木偶（桑多涅）红温状态特征模型（硬编码自训练工具导出的 JSON）。
    /// </summary>
    private static readonly FeatureScorerExportData _overheatModel = new()
    {
        Features =
        {
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 519, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 301.808, Weight = 0.7914,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0007, 0.0019, 0.0051, 0.0138, 0.0366, 0.0937, 0.2193, 0.433, 0.6749, 0.8494, 0.9388, 0.9766, 0.9913, 0.9968]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 518, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 300.5802, Weight = 0.789,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0023, 0.0062, 0.0166, 0.0439, 0.1109, 0.2532, 0.4796, 0.7147, 0.872, 0.9487, 0.9805, 0.9927, 0.9973]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1095, Y = 517, W = 1, H = 1,
                IsCircular = true, Range = 360, RefVal = 297.9216, Weight = 0.7738,
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0213, 0.0558, 0.1384, 0.3038, 0.5426, 0.7633, 0.8976, 0.9597, 0.9848, 0.9944]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1096, Y = 513, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5461,
                RefHist = [0.0705, 0.0023, 0, 0, 0, 0.0018, 0.0739, 0.8516],
                ProbTable = [0, 0, 0.0001, 0.0002, 0.0005, 0.0015, 0.004, 0.0108, 0.0289, 0.0747, 0.18, 0.3737, 0.6186, 0.8151, 0.923, 0.9702, 0.9888, 0.9959, 0.9985, 0.9994, 0.9998]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1097, Y = 516, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5088,
                RefHist = [0.1062, 0.0046, 0, 0, 0, 0, 0.0201, 0.8691],
                ProbTable = [0, 0, 0.0001, 0.0004, 0.001, 0.0026, 0.0071, 0.0192, 0.0504, 0.1262, 0.2819, 0.5162, 0.7436, 0.8874, 0.9554, 0.9831, 0.9937, 0.9977, 0.9991, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "H", X = 1090, Y = 552, W = 4, H = 1,
                IsCircular = false, Range = 1, Weight = 0.4793,
                RefHist = [0, 0, 0.0191, 0.9213, 0.0576, 0.002, 0, 0],
                ProbTable = [0, 0, 0, 0, 0, 0.0001, 0.0003, 0.0008, 0.0021, 0.0058, 0.0156, 0.0414, 0.1051, 0.2419, 0.4645, 0.7022, 0.865, 0.9457, 0.9793, 0.9923, 0.9972]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1105, Y = 564, W = 2, H = 3,
                IsCircular = true, Range = 360, RefVal = 349.1209, Weight = 0.7477,
                ProbTable = [0, 0, 0, 0, 0, 0, 0, 0.0001, 0.0002, 0.0007, 0.0018, 0.0049, 0.0133, 0.0353, 0.0905, 0.2129, 0.4237, 0.6665, 0.8446, 0.9366, 0.9757]
            },
            new FeatureScorerItem
            {
                Type = "F2", Channel = "V", X = 1095, Y = 572, W = 1, H = 4,
                IsCircular = false, Range = 1, Weight = 0.5165,
                RefHist = [0.9278, 0.0164, 0, 0, 0.0052, 0, 0.0121, 0.0384],
                ProbTable = [0, 0.0001, 0.0002, 0.0004, 0.0011, 0.003, 0.0082, 0.0221, 0.0578, 0.143, 0.3121, 0.5522, 0.7702, 0.9011, 0.9612, 0.9854, 0.9946, 0.998, 0.9993, 0.9997, 0.9999]
            },
            new FeatureScorerItem
            {
                Type = "F1", Channel = "H", X = 1105, Y = 572, W = 5, H = 4,
                IsCircular = true, Range = 360, RefVal = 351.1534, Weight = 0.7542,
                ProbTable = [0, 0, 0, 0, 0, 0, 0.0001, 0.0001, 0.0004, 0.0011, 0.0029, 0.0079, 0.0212, 0.0556, 0.138, 0.3032, 0.5419, 0.7628, 0.8973, 0.9596, 0.9848]
            },
        }
    };

    /// <summary>
    /// 判断当前木偶是否处于红温状态（特征评分 ≥ 阈值）。
    /// 评分异常时降级返回 false，不中断战斗。
    /// </summary>
    private static bool IsOverheated(ImageRegion capture)
    {
        try
        {
            return ImageFeatureScorer.Score(_overheatModel, capture.SrcMat) >= OverheatThreshold;
        }
        catch (Exception e)
        {
            Logger.LogWarning("红温状态评分异常: {Message}", e.Message);
            return false;
        }
    }

    /// <summary>
    /// 特化规则：(动作, 角色) → 参数条件（null=无条件，仅检查动作+角色即生效）
    /// 不在此字典中的组合直接跳过，走通用逻辑。
    /// </summary>
    private static readonly Dictionary<(string Action, string Character), Func<object, bool>?> SpecializedRules = new()
    {
        [("UseSkill", "纳西妲")]   = args => args is ActionArgs { Hold: true },
        [("UseSkill", "坎蒂丝")]   = args => args is ActionArgs { Hold: true },
        [("UseSkill", "恰斯卡")]   = args => args is ActionArgs { Hold: true },
        [("Charge",   "那维莱特")] = null,
        [("Charge",   "恰斯卡")]   = null,
        [("Charge",   "桑多涅")]   = null,
    };

    /// <summary>
    /// 根据动作和角色名分派特化逻辑。
    /// 如果当前角色有对应的特化实现，则执行该特化逻辑并返回 true（调用方应跳过通用逻辑）；
    /// 否则返回 false，由调用方执行通用逻辑。
    /// </summary>
    /// <param name="action">动作名（如 "UseSkill"、"Charge"）</param>
    /// <param name="character">角色名（如 "纳西妲"）</param>
    /// <param name="args">动作参数对象（如 UseSkillArgs、ChargeArgs）</param>
    /// <returns>true 表示已由特化逻辑处理，false 表示无特化逻辑</returns>
    public static bool ExecuteSpecializedAction(Avatar avatar, string action, string character, object args)
    {
        // 不在特化规则中 → 提前退出
        if (!SpecializedRules.TryGetValue((action, character), out var condition)) return false;

        // 参数条件存在且不满足 → 提前退出
        if (condition != null && !condition(args)) return false;

        switch (action)
        {
            case "UseSkill":
                return ExecuteUseSkillSpecialized(avatar, character);
            case "Charge":
                return ExecuteChargeSpecialized(avatar, character, ((ActionArgs)args).Ms);
            default:
                return false;
        }
    }

    /// <summary>
    /// UseSkill 特化分派
    /// </summary>
    private static bool ExecuteUseSkillSpecialized(Avatar avatar, string character)
    {
        switch (character)
        {
            // 纳西妲长按 E：按下后向右移动鼠标
            case "纳西妲":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                    Sleep(300, avatar.Ct);
                    for (int j = 0; j < 10; j++)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(1000, 0);
                        Sleep(50);
                    }

                    Sleep(300);
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                    return true;
                }
            }
            // 坎蒂丝长按 E：固定等待 3 秒
            case "坎蒂丝":
            {
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                Thread.Sleep(3000);
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                return true;
            }
            // 恰斯卡长按 E：骑乘蓄力瞄准
            case "恰斯卡":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    // 平滑旋转控制（声明于 try 外，保证 finally 中可取消独立异步旋转循环）
                    var smoothRotateCts = CancellationTokenSource.CreateLinkedTokenSource(avatar.Ct);
                    Task smoothRotateTask = null!;
                    try
                    {
                    // 第一步：确认恰斯卡状态为飞行
                    // 1. 已处于飞行状态（特定位置白色像素）→ 按住左键开始射击，进入第二步
                    // 2. 未飞行且 E 可用（OCR 识别不到 CD）→ 点按 E，等待 400ms，进入第二步
                    // 3. 未飞行且 E 不可用 → 直接跳出动作，不进入第二步
                    if (ChascaIsFlying())
                    {
                        // 已飞行：按住左键（骑乘索敌射击）后进入第二步
                        Simulation.SendInput.Mouse.LeftButtonDown();
                    }
                    else if (ReadEskillCdForChasca() > 0)
                    {
                        // E 不可用，直接跳出动作
                        return true;
                    }
                    else
                    {
                        // E 可用：点按 E，等待 400ms，按住左键（骑乘索敌射击）后进入第二步
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
                        Sleep(400, avatar.Ct);
                        Simulation.SendInput.Mouse.LeftButtonDown();
                    }

                    // 第二步：索敌循环逻辑
                    // 记录自本次特化启动以来，每一帧的视角朝向与时间戳（用于后续转向与退出判定）
                    var orientationHistory = new List<(float Angle, DateTime Time)>();
                    // 视觉识别配置（帧间间隔、恰斯卡稳定时间，与持续索敌一致）
                    var visConfig = AvatarRecognition.GetVisualRecognitionConfig();
                    var frameIntervalMs = visConfig.TargetingDetectionInterval;
                    var chascaStableTime = visConfig.ChascaStableTime;
                    var dpi = TaskContext.Instance().DpiScale;
                    // 距离上一次事件的时间：启动进入第二步/识别到伤害数字/上一次旋转/上一次子弹列表变化/上一次喷射动画
                    // 子弹列表变化与喷射动画的更新时间点在帧内子弹识别处补充
                    var lastEventTime = DateTime.UtcNow;
                    // 稳定时间倍数：识别到子弹喷射后，下一次稳定时间判定阈值翻倍（喷射后留出缓冲再旋转）
                    double stableTimeMultiplier = 1;
                    // 退出条件状态：第二步开始时间（10秒超时）、累计旋转（距上次识别到目标后超过一圈）
                    var startTime = DateTime.UtcNow;
                    float? prevAngle = null;
                    double cumulativeRotation = 0;
                    // 水平旋转力度（像素/次）：初始取配置值（恰斯卡初始旋转力度，默认 1000≈单次 30°），
                    // 之后根据实测旋转角度自适应校准（目标单次旋转角度由配置"恰斯卡单次旋转角度"决定，默认 50°）
                    double rotateX = visConfig.ChascaInitialRotateX * dpi;
                    // 单次旋转角度（度）：由配置决定（默认 50）。传奇血条存在（有目标）时单次旋转该角度，
                    // 无目标（无血条连续旋转）时使用该值的一半
                    double rotateStepAngle = visConfig.ChascaRotateStepAngle;
                    // 上一帧是否执行过水平旋转（用于下一帧实测角度自适应校准）
                    bool rotatedLastFrame = false;
                    // 旋转时实际使用的水平力度（px），供下一帧计算 实测角度÷力度 比例
                    double rotateXUsed = 0;
                    // 最近 5 次 实测角度÷力度 比例（滑动窗口，取中位数校准，抗异常值干扰且响应及时）
                    List<double> angleRatios = new();
                    // 最近一次校准得到的中位数 角度÷力度 比例（供无血条连续旋转换算固定角度力度）
                    double lastMedianRatio = 0;
                    // 无血条连续旋转模式：第一次由稳定时间触发后，不再等待稳定间隔，每帧旋转"单次旋转角度的一半"（默认25°），
                    // 直到再次看到血条或伤害数字后重置（恢复稳定时间判定）
                    bool continuousRotating = false;
                    // 子弹序列变化跟踪：保存至多 N 个历史子弹序列（每帧识别结果），用于检测子弹列表变化。
                    // N 由配置"恰斯卡序列槽数量"决定（默认 2，范围 1-5），识别结果与全部历史序列比较，
                    // 序列变化时替换最旧的历史序列
                    int seqSlotCount = Math.Clamp(visConfig.ChascaSequenceSlotCount, 1, 5);
                    List<ChascaBulletType[]> bulletSeqs = new();
                    List<DateTime> bulletSeqTimes = new();
                    // 退出条件4状态：传奇血条最后出现时间（本次第二步期间出现后，连续1.5秒未出现时触发下车）
                    DateTime? legendaryBarLastSeen = null;

                    // 平滑转动模式：勾选"恰斯卡平滑转动"后启用，取代原有"无血条连续25°/帧"与"传奇血条间歇50°大旋转"两种旋转。
                    // 无目标分支超过稳定时间后置旋转请求标志，由下方独立异步循环持续小步旋转（间隔较小、角度较小），
                    // 转速根据主循环维护的视角-时间序列（orientationHistory）实测值与预期值自适应调节
                    bool smoothRotateEnabled = visConfig.ChascaSmoothRotateEnabled;
                    // 旋转请求标志（主循环写、旋转器线程读，经 Volatile 访问保证可见性）
                    bool smoothRotateRequested = false;
                    // 往回转补偿进行中标志（回转循环写、主循环读，经 Volatile 访问）：回转期间主循环协作空转，避免鼠标操作冲突
                    bool rollbackActive = false;
                    // 平滑旋转步进水平力度（px/步，主循环初始化、旋转器线程读取并调节，经 Volatile 访问）。
                    // 仅在首次进入平滑旋转时初始化，暂停后恢复时沿用上次保存的力度断点（由 EMA 持续调节）
                    int smoothStepX = 0;
                    // 平滑旋转力度是否已初始化（仅主循环线程访问）：保证断点只初始化一次，暂停恢复不重置
                    bool smoothStepInitialized = false;
                    // 独立异步旋转循环：节奏不依赖主循环帧间隔，仅在旋转请求标志为 true 时旋转，
                    // 否则等待一个帧间隔后 continue 跳过（避免忙等）
                    smoothRotateTask = Task.Run(() =>
                    {
                        // 增量消费主循环记录的活跃段样本（仅旋转器线程访问）：
                        // 游标 + 上一个已消费样本 + 转速 EMA（°/s，平滑相邻样本瞬时转速的识别噪声）。
                        // 每个新样本与上一个时间连续（两次采样均在平滑旋转期间）时计算一次瞬时转速并做一次
                        // 小幅度 EMA 修正，每点有且仅使用一次；不连续（暂停后恢复）时重置基线，首个样本仅作起点
                        int lastConsumedIndex = -1;
                        (float Angle, DateTime Time)? lastConsumedSample = null;
                        double emaSpeed = 0;
                        while (!smoothRotateCts.Token.IsCancellationRequested)
                        {
                            // 不满足旋转条件：等待一个帧间隔后跳过
                            if (!Volatile.Read(ref smoothRotateRequested))
                            {
                                Sleep(frameIntervalMs, smoothRotateCts.Token);
                                continue;
                            }
                            // 预期转速（度/秒，可配置）：有目标（传奇血条）与无目标场景统一使用配置值
                            double expectedSmoothRotateSpeed = visConfig.ChascaSmoothRotateSpeed;
                            // 增量消费主循环新增的活跃段样本：样本由主循环仅在平滑旋转活跃段写入，
                            // 暂停段的静止样本不入列。每个新样本与上一个已消费样本时间连续（间隔约一个主循环帧，
                            // 说明两次采样均在平滑旋转期间）时，用两点计算一次瞬时转速并做一次小幅度 EMA 修正；
                            // 时间不连续（暂停后恢复的首个样本）仅重置基线，不修正
                            lock (orientationHistory)
                            {
                                int count = orientationHistory.Count;
                                if (count > lastConsumedIndex + 1)
                                {
                                    for (int i = lastConsumedIndex + 1; i < count; i++)
                                    {
                                        var sample = orientationHistory[i];
                                        if (lastConsumedSample.HasValue)
                                        {
                                            double dt = (sample.Time - lastConsumedSample.Value.Time).TotalSeconds;
                                            // 时间连续判定：正常相邻样本间隔约一个主循环帧，超过 0.25s 视为跨段（暂停恢复）
                                            if (dt > 0.05 && dt <= 0.25)
                                            {
                                                double dAngle = sample.Angle - lastConsumedSample.Value.Angle;
                                                if (dAngle > 180) dAngle -= 360;
                                                else if (dAngle < -180) dAngle += 360;
                                                double instSpeed = Math.Abs(dAngle) / dt;
                                                // 转速 EMA（新值 30% 权重）：平滑相邻样本瞬时转速的识别噪声
                                                emaSpeed = emaSpeed > 0 ? emaSpeed * 0.7 + instSpeed * 0.3 : instSpeed;
                                                // 转速过低（初始力度过小或画面静止）时也按低速评估，避免永不进入自适应而空转
                                                if (emaSpeed > 0.1)
                                                {
                                                    double factor = Math.Clamp(expectedSmoothRotateSpeed / Math.Max(emaSpeed, 0.5), 0.2, 5.0);
                                                    if (Math.Abs(factor - 1) > 0.1)
                                                    {
                                                        // 步进力度按乘法 EMA 渐近（new = current × factor^0.2）：
                                                        // 在乘法域平滑调节，放大/缩小对称互逆（factor 与其倒数调整恰好互为倒数，
                                                        // 单次最多放大 5^0.2≈1.38 倍、最小缩小 0.2^0.2≈0.72 倍），
                                                        // 避免线性插值造成放大远大于缩小的不对称
                                                        double current = Volatile.Read(ref smoothStepX);
                                                        double newStep = Math.Clamp(current * Math.Pow(factor, 0.2), 1, 2000);
                                                        Volatile.Write(ref smoothStepX, (int)newStep);
                                                    }
                                                }
                                            }
                                            // 不连续（暂停后恢复）：此样本仅作新基线，不参与转速计算
                                        }
                                        lastConsumedSample = sample;
                                    }
                                    lastConsumedIndex = count - 1;
                                }
                            }
                            // 按调节后的步进力度小角度旋转一次
                            int stepX = Volatile.Read(ref smoothStepX);
                            if (stepX > 0)
                            {
                                Simulation.SendInput.Mouse.MoveMouseBy(stepX,
                                    (int)(visConfig.ChascaPressStrength * stepX * 0.194));
                            }
                            // 独立于主循环帧的步进间隔（约62步/秒，10°级小步连续旋转）
                            Sleep(16, smoothRotateCts.Token);
                        }
                    }, avatar.Ct);

                    // 局部函数：下车动作（松开左键 → 长按 E 落地 → 检测 E 进入 CD → 松开 E），2 秒超时兜底防止卡死
                    void LandChasca()
                    {
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        Sleep(500, avatar.Ct);
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                        try
                        {
                            var landStartTime = DateTime.UtcNow;
                            while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - landStartTime).TotalSeconds < 3)
                            {
                                if (ReadEskillCdForChasca() > 0)
                                {
                                    break;
                                }
                                Sleep(100, avatar.Ct);
                            }
                        }
                        finally
                        {
                            Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                        }
                    }

                    // 局部函数：按目标角度水平旋转一次（用最近中位数 角度÷力度 比例换算力度；无样本时回退 rotateX）
                    // 旋转后额外等待两个帧间隔，确保画面稳定后再继续识别
                    void RotateStep(double targetDeg)
                    {
                        double stepX = lastMedianRatio > 0 ? targetDeg / lastMedianRatio : rotateX;
                        Simulation.SendInput.Mouse.MoveMouseBy((int)stepX, (int)(visConfig.ChascaPressStrength * stepX * 0.194));
                        Sleep(frameIntervalMs * 2, avatar.Ct);
                    }

                    // 局部函数：停止平滑旋转，并对子弹识别延迟导致的过冲做往回转补偿。
                    // 子弹识别存在延迟，识别到"应停止旋转"（子弹喷射/序列变化）时视角实际已多转，
                    // 故以与正转相同的步进节奏（16ms）与断点力度（smoothStepX，方向取反）持续往回转，
                    // 每步用视角识别判断是否到达目标点（停止角度-30°）±5°，到达后退出（不再一次性转固定角度）。
                    // 回转在独立异步循环中执行，不阻塞主循环：主循环检测 rollbackActive 后协作空转避免鼠标操作冲突
                    void StopSmoothRotate()
                    {
                        if (!Volatile.Read(ref smoothRotateRequested))
                        {
                            return; // 未在旋转中，无需停止与回转
                        }
                        Volatile.Write(ref smoothRotateRequested, false);
                        Volatile.Write(ref rollbackActive, true);
                        Task.Run(() =>
                        {
                            try
                            {
                                using (var cap = CaptureToRectArea())
                                {
                                    double stopAngle = CameraOrientation.Compute(cap.SrcMat);
                                    double targetAngle = stopAngle - 30;
                                    int stepX = Volatile.Read(ref smoothStepX);
                                    var rollbackStart = DateTime.UtcNow;
                                    while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - rollbackStart).TotalSeconds < 3)
                                    {
                                        using (var curCap = CaptureToRectArea())
                                        {
                                            double cur = CameraOrientation.Compute(curCap.SrcMat);
                                            double diff = cur - targetAngle;
                                            if (diff > 180) diff -= 360;
                                            else if (diff < -180) diff += 360;
                                            if (Math.Abs(diff) <= 5)
                                            {
                                                break; // 到达目标点 ±5° 内，退出回转
                                            }
                                            // diff>0 需要左转（负力度），diff<0 过头需右转（正力度）
                                            int dir = diff > 0 ? -1 : 1;
                                            Simulation.SendInput.Mouse.MoveMouseBy(dir * stepX, (int)(visConfig.ChascaPressStrength * dir * stepX * 0.194));
                                        }
                                        Sleep(16, avatar.Ct);
                                    }
                                    Logger.LogInformation("恰斯卡特化：平滑转动停止，往回转补偿（目标 {Target:F0}°±5°）", targetAngle);
                                }
                            }
                            finally
                            {
                                Volatile.Write(ref rollbackActive, false);
                            }
                        });
                    }

                    while (!avatar.Ct.IsCancellationRequested)
                    {
                        // 退出条件1：20秒超时
                        if ((DateTime.UtcNow - startTime).TotalSeconds >= 20)
                        {
                            Logger.LogInformation("恰斯卡特化退出：20秒超时，开始落地");
                            LandChasca();
                            break;
                        }

                        using (var capture = CaptureToRectArea())
                        {
                            // 退出条件2：不处于飞行状态（已下车）
                            if (!ChascaIsFlyingByPixel(capture.SrcMat))
                            {
                                // 已下车：松开左键并等待 300ms 即可
                                Logger.LogInformation("恰斯卡特化退出：不处于飞行状态（已下车）");
                                Simulation.SendInput.Mouse.LeftButtonUp();
                                Sleep(300, avatar.Ct);
                                break;
                            }

                            // 获取当前视角朝向（每帧计算，用于下方累计旋转判定），
                            // 仅当平滑旋转活跃（请求标志为 true）时才记录到序列：暂停段的静止样本不入列，
                            // 保证旋转器评估窗口内的视角变化完全由平滑旋转自身产生
                            var angle = CameraOrientation.Compute(capture.SrcMat);
                            if (Volatile.Read(ref smoothRotateRequested))
                            {
                                lock (orientationHistory)
                                {
                                    orientationHistory.Add((angle, DateTime.UtcNow));
                                }
                            }

                            // 往回转补偿进行中：本帧协作空转（不识别不旋转），避免主循环的鼠标操作与回转循环冲突
                            if (Volatile.Read(ref rollbackActive))
                            {
                                Sleep(frameIntervalMs, avatar.Ct);
                                continue;
                            }
                            // 累计旋转（距上次识别到目标后重新计数）：相邻帧角度差归一化到 (-180,180] 后累加
                            float delta = 0;
                            if (prevAngle.HasValue)
                            {
                                delta = angle - prevAngle.Value;
                                if (delta > 180) delta -= 360;
                                else if (delta < -180) delta += 360;
                                cumulativeRotation += delta;
                            }
                            // 血条识别：区分传奇血条与普通血条
                            // FindBloodBars 内部自动更新传奇血条跨帧追踪（与持续索敌共用静态追踪器，
                            // 开启持续索敌时跨帧识别信息可保留，此处不清空追踪器）
                            // 提前到校准块之前：退出条件4需跟踪传奇血条出现状态
                            var bars = AvatarRecognition.FindBloodBars(capture);
                            var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();
                            var hasLegendaryBar = valid.Any(b => AvatarRecognition.IsLegendaryBar(b.x, b.y));

                            // 退出条件4状态维护：记录传奇血条最后出现时间
                            if (hasLegendaryBar)
                            {
                                legendaryBarLastSeen = DateTime.UtcNow;
                            }

                            // 自适应旋转力度：对 实测角度÷使用力度 的比例滑动取中位数，据此预测当前力度的单次旋转角并校准
                            // 中位数对异常值（角度识别误差、画面抖动导致的离群测量）稳健，窗口避免无限累积导致调节迟钝
                            // 预期单次旋转角度：由配置"恰斯卡单次旋转角度"决定（默认 50°），传奇血条与无血条场景校准目标一致
                            // 调节与补转分离：每次旋转后先按中位数预测调节力度（向目标角度收敛），
                            // 再判断实测角度是否超容差（<60% 或 >130%），超差则用角度差值补转并跳过后续步骤
                            if (rotatedLastFrame)
                            {
                                var actual = Math.Abs(delta);
                                if (actual > 1 && rotateXUsed > 0) // 忽略噪声级角度差（画面稳定后无操作时接近 0）
                                {
                                    // 实测角度÷使用力度：每像素力度产生的旋转角度
                                    angleRatios.Add(actual / rotateXUsed);
                                    if (angleRatios.Count > 5)
                                    {
                                        angleRatios.RemoveAt(0);
                                    }
                                    var sorted = angleRatios.OrderBy(r => r).ToArray();
                                    var medianRatio = sorted[sorted.Length / 2];
                                    lastMedianRatio = medianRatio; // 供无血条连续旋转换算固定角度力度
                                    // 预期单次旋转角度：由配置决定（默认 50°）
                                    double expected = rotateStepAngle;
                                    // 按中位数比例预测当前力度的单次旋转角度，并向预期收敛（始终执行，避免力度停在旧值反复补转）
                                    var predicted = medianRatio * rotateX;
                                    if (predicted < expected)
                                    {
                                        double factor = Math.Clamp(expected / predicted, 1.0, 5.0);
                                        rotateX *= factor;
                                        Logger.LogInformation("自适应旋转角：预测单次{Predicted:F2}°，将旋转力度调整为{Factor:F2}倍", predicted, factor);
                                    }
                                    else if (predicted > expected)
                                    {
                                        double factor = Math.Clamp(expected / predicted, 0.2, 1.0);
                                        rotateX *= factor;
                                        Logger.LogInformation("自适应旋转角：预测单次{Predicted:F2}°，将旋转力度调整为{Factor:F2}倍", predicted, factor);
                                    }
                                    // 实际角度与预期偏差过大：跳过后续步骤，先用角度差值补转
                                    if (actual < expected * 0.6 || actual > expected * 1.3)
                                    {
                                        // 角度差值（正=向右补转，负=向左回补），用中位数比例换算为水平力度
                                        double diff = expected - actual;
                                        double compensateX = diff / medianRatio;
                                        Logger.LogInformation("自适应旋转角：实测{Actual:F2}°偏离预期{Expected:F0}°，补转{Diff:F2}°", actual, expected, diff);
                                        rotatedLastFrame = false; // 补转结果不计入下一次校准
                                        Simulation.SendInput.Mouse.MoveMouseBy((int)compensateX, (int)(visConfig.ChascaPressStrength * compensateX * 0.194));
                                        Sleep(frameIntervalMs, avatar.Ct); // 补转后额外等待一个帧间隔
                                        prevAngle = angle; // 补转后更新基准角度，保证下一帧累计旋转正确
                                        continue; // 跳过后续步骤（血条/伤害/子弹识别与稳定旋转），重新截图
                                    }
                                }
                                rotatedLastFrame = false;
                            }
                            prevAngle = angle;

                            if (valid.Count > 0 && !hasLegendaryBar)
                            {
                                // 存在普通血条且不存在传奇血条：参考桑多涅逻辑，瞄准最近血条中心
                                // 中心点使用 1080p 的 (960,300)，将敌人置于屏幕偏上位置（恰斯卡相对俯视）
                                continuousRotating = false; // 再次看到血条，重置连续旋转状态
                                Volatile.Write(ref smoothRotateRequested, false); // 再次看到血条，停止平滑旋转
                                var preAimX = (int)(960 * AssetScale);
                                var preAimY = (int)(300 * AssetScale);
                                var nearest = valid.OrderBy(b =>
                                    Math.Abs((b.x + b.width / 2) - preAimX) +
                                    Math.Abs((b.y + b.height / 2) - preAimY)).First();
                                var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                                var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                                // 单次旋转力度为桑多涅逻辑的四分之三（0.35×0.75、0.25×0.75，原四分之一翻3倍）
                                Simulation.SendInput.Mouse.MoveMouseBy(
                                    (int)(offsetX * 0.2625 * dpi), (int)(offsetY * 0.1875 * dpi));
                                cumulativeRotation = 0; // 识别到普通血条，累计旋转重新计数
                            }
                            else
                            {
                                // 存在传奇血条 或 无任何血条：做伤害数字识别，瞄准有效伤害数字
                                // 中心点使用 1080p 的 (960,360)，力度系数可配置（见 ChascaAimForceX/Y）
                                var damageResult = AvatarRecognition.FindDamageNumber(capture);
                                if (damageResult.HasValue)
                                {
                                    continuousRotating = false; // 再次看到伤害数字，重置连续旋转状态
                                    Volatile.Write(ref smoothRotateRequested, false); // 再次看到伤害数字，停止平滑旋转
                                    var (dcx, dcy, _, _, _, _, _) = damageResult.Value;
                                    Simulation.SendInput.Mouse.MoveMouseBy(
                                        (int)((dcx - (int)(960 * AssetScale)) * visConfig.ChascaAimForceX * dpi),
                                        (int)((dcy - (int)(360 * AssetScale)) * visConfig.ChascaAimForceY * dpi));
                                    lastEventTime = DateTime.UtcNow; // 识别到伤害数字，视为活动事件
                                    cumulativeRotation = 0; // 识别到伤害数字，累计旋转重新计数
                                }
                                else
                                {
                                    // 无目标（无血条且无伤害数字）：依赖子弹状态判断当前是否需要移动视角
                                    // 子弹识别与喷射检测仅在无目标分支执行，血条/伤害数字可见时短路跳过

                                    // 恰斯卡子弹识别：六个槽位的元素状态（空/风/火/水/雷/冰）
                                    var bullets = RecognizeChascaBullets(capture, visConfig.ChascaBulletThreshold);
                                    // 每帧输出识别到的子弹序列（元素名）
                                    string[] elementNames = ["空", "风", "火", "水", "雷", "冰"];
                                    Logger.LogInformation("恰斯卡子弹序列: {Bullets}", string.Join(",", bullets.Select(b => elementNames[(int)b])));

                                    // 子弹框识别：子弹框不存在（<0.5）时视为正在进行子弹喷射，更新时间
                                    if (ChascaIsSpraying(capture))
                                    {
                                        Logger.LogInformation("检测到子弹发射");
                                        lastEventTime = DateTime.UtcNow;
                                        cumulativeRotation = 0; // 识别到子弹喷射，累计旋转重新计数
                                        stableTimeMultiplier = 2; // 喷射后下一次稳定时间判定阈值翻倍
                                        StopSmoothRotate(); // 子弹喷射中，停止平滑旋转并往回转补偿
                                    }
                                    else
                                    {
                                        // 子弹变化跟踪：当前帧序列与全部历史序列比较，无相同序列则更新时间，
                                        // 历史序列不足数量时追加，否则替换最旧的历史序列
                                        bool seqSame = false;
                                        foreach (var seq in bulletSeqs)
                                        {
                                            if (ChascaSeqEquals(bullets, seq)) { seqSame = true; break; }
                                        }
                                        if (!seqSame)
                                        {
                                            lastEventTime = DateTime.UtcNow;
                                            cumulativeRotation = 0; // 子弹序列变化，累计旋转重新计数
                                            StopSmoothRotate(); // 子弹序列变化，停止平滑旋转并往回转补偿
                                            if (bulletSeqs.Count < seqSlotCount)
                                            {
                                                bulletSeqs.Add(bullets);
                                                bulletSeqTimes.Add(DateTime.UtcNow);
                                            }
                                            else
                                            {
                                                // 替换最旧的历史序列（记录时间最早者）
                                                int oldestIdx = 0;
                                                for (int k = 1; k < bulletSeqs.Count; k++)
                                                {
                                                    if (bulletSeqTimes[k] < bulletSeqTimes[oldestIdx]) oldestIdx = k;
                                                }
                                                bulletSeqs[oldestIdx] = bullets;
                                                bulletSeqTimes[oldestIdx] = DateTime.UtcNow;
                                            }
                                        }
                                    }

                                    // 旋转索敌：无血条时进入连续旋转模式——第一次由稳定时间触发后不再等待稳定间隔，
                                    // 每帧旋转"单次旋转角度的一半"直到再次看到血条或伤害数字（识别处重置 continuousRotating）
                                    // 传奇血条存在（有目标）：保持稳定时间判定，单次旋转"单次旋转角度"（自适应力度）
                                    // 勾选"恰斯卡平滑转动"时以上两种旋转均被平滑转动取代
                                    if (smoothRotateEnabled)
                                    {
                                        // 平滑转动：超过稳定时间（传奇血条存在时还须过起飞后不旋转观察期）后置旋转请求标志，
                                        // 由独立异步旋转循环持续小步旋转（间隔较小、角度较小，转速自适应调节），无需间隔等待
                                        if ((!hasLegendaryBar || (DateTime.UtcNow - startTime).TotalSeconds >= visConfig.ChascaNoRotateBeforeSeconds) &&
                                            (DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime * stableTimeMultiplier)
                                        {
                                            if (!Volatile.Read(ref smoothRotateRequested))
                                            {
                                                // 平滑旋转启动：仅在首次进入时初始化步进力度作为自适应过渡起点，
                                                // 按当前校准力度换算约 10° 步进（无校准样本时取初始力度 10%）；
                                                // 暂停后恢复时沿用上次保存的力度断点（不重置，由 EMA 继续调节）
                                                if (!smoothStepInitialized)
                                                {
                                                    double stepDeg = 10;
                                                    Volatile.Write(ref smoothStepX,
                                                        (int)Math.Max(1, lastMedianRatio > 0 ? stepDeg / lastMedianRatio : rotateX * 0.1));
                                                    smoothStepInitialized = true;
                                                    Logger.LogInformation("恰斯卡特化：平滑转动开始（每步约{F0}°，转速自适应调节）", stepDeg);
                                                }
                                            }
                                            Volatile.Write(ref smoothRotateRequested, true);
                                        }
                                    }
                                    else if (!hasLegendaryBar)
                                    {
                                        if (!continuousRotating)
                                        {
                                            // 未开始连续旋转：等待稳定时间后触发第一次旋转（单次旋转角度的一半）
                                            if ((DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime * stableTimeMultiplier)
                                            {
                                                Logger.LogInformation("恰斯卡特化：无血条且无伤害数字，开始连续旋转索敌（每帧{F0}°）", rotateStepAngle / 2);
                                                RotateStep(rotateStepAngle / 2);
                                                continuousRotating = true;
                                                stableTimeMultiplier = 1;
                                            }
                                        }
                                        else
                                        {
                                            // 连续旋转：每帧转"单次旋转角度的一半"，不等待稳定间隔
                                            RotateStep(rotateStepAngle / 2);
                                        }
                                    }
                                    else
                                    {
                                        // 传奇血条存在：重置连续旋转状态，按稳定时间单次旋转"单次旋转角度"
                                        continuousRotating = false;
                                        // 传奇血条存在时，起飞后前 chascaNoRotateBeforeSeconds 秒不执行旋转
                                        //（开局观察期，默认 1 秒，配置为 0 时立即按稳定时间旋转）
                                        if ((DateTime.UtcNow - startTime).TotalSeconds >= visConfig.ChascaNoRotateBeforeSeconds &&
                                            (DateTime.UtcNow - lastEventTime).TotalSeconds > chascaStableTime * stableTimeMultiplier)
                                        {
                                            rotatedLastFrame = true; // 下一帧用实测旋转角度自适应校准力度
                                            rotateXUsed = rotateX; // 记录本次旋转实际使用的力度，供下一帧计算 角度÷力度 比例
                                            Simulation.SendInput.Mouse.MoveMouseBy((int)rotateX, (int)(visConfig.ChascaPressStrength * rotateX * 0.194));
                                            lastEventTime = DateTime.UtcNow; // 上一次旋转
                                            stableTimeMultiplier = 1; // 本次翻倍判定已生效，恢复正常阈值
                                            Sleep(frameIntervalMs * 2, avatar.Ct);
                                        }
                                    }
                                }
                            }

                            // 退出条件3：距上次识别到伤害数字或血条后，旋转超过一圈（360°）
                            // 依赖本帧朝向的累计旋转，放在截图块内（朝向记录之后）
                            // 传奇血条存在时不触发（持续索敌中，不应因旋转超一圈而落地）；
                            // 无血条连续旋转模式转满一圈仍无目标时落地兜底（识别到血条/伤害数字会重置累计）
                            if (!hasLegendaryBar && Math.Abs(cumulativeRotation) >= 360)
                            {
                                Logger.LogInformation("恰斯卡特化退出：累计旋转超过一圈（{Rotation:F0}°），开始落地", cumulativeRotation);
                                LandChasca();
                                break;
                            }

                            // 退出条件4：传奇血条曾出现且连续1.5秒未出现 → 下车
                            if (legendaryBarLastSeen.HasValue && (DateTime.UtcNow - legendaryBarLastSeen.Value).TotalSeconds >= 1.5)
                            {
                                Logger.LogInformation("恰斯卡特化退出：传奇血条连续1.5秒未出现，开始落地");
                                LandChasca();
                                break;
                            }
                        }

                        // 每帧末尾等待帧间间隔
                        Sleep(frameIntervalMs);
                    }

                    return true;
                    }
                    finally
                    {
                        // 取消平滑旋转独立异步循环，避免旋转器在第二步结束后继续旋转/泄漏
                        smoothRotateCts.Cancel();
                        try { smoothRotateTask.Wait(1000); } catch (Exception) { }
                        // 保证异常路径下左键与 E 键均释放，避免按键卡住
                        Simulation.SendInput.Mouse.LeftButtonUp();
                        Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                    }
                }
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Charge 重击特化分派
    /// </summary>
    private static bool ExecuteChargeSpecialized(Avatar avatar, string character, int ms)
    {
        switch (character)
        {
            // 那维莱特：按住普攻循环向右旋转
            case "那维莱特":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
                    try
                    {
                        while (ms >= 0)
                        {
                            if (avatar.Ct is { IsCancellationRequested: true })
                            {
                                return true;
                            }

                            Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                            ms -= 50;
                            Sleep(50);
                        }
                    }
                    finally
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }
                return true;
            }
            // 恰斯卡：按住普攻分段变速旋转
            case "恰斯卡":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
                    try
                    {
                        int tick = -4;
                        while (ms >= 0)
                        {
                            if (avatar.Ct is { IsCancellationRequested: true })
                            {
                                return true;
                            }

                            const double lowspeed = 0.7, highspeed = 50;
                            double rateX, rateY;
                            if (tick < 3)
                            {
                                rateX = highspeed;
                                rateY = highspeed * 0.23;
                            }
                            else if (tick < 40)
                            {
                                rateX = lowspeed * 0.7;
                                rateY = 0;
                            }
                            else if (tick < 43)
                            {
                                rateX = highspeed;
                                rateY = highspeed * 0.4;
                            }
                            else if (tick < 70)
                            {
                                rateX = lowspeed * 0.9;
                                rateY = 0;
                            }
                            else if (tick < 73)
                            {
                                rateX = highspeed;
                                rateY = highspeed;
                            }
                            else
                            {
                                rateX = lowspeed;
                                rateY = 0;
                            }

                            Simulation.SendInput.Mouse.MoveMouseBy((int)(rateX * 50 * dpi), (int)(rateY * 50 * dpi));
                            tick = (tick + 1) % 100;
                            Sleep(25);
                            ms -= 25;
                        }

                        return true;
                    }
                    finally
                    {
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }
            }
            // 桑多涅：按住普攻 + 截图寻的血条/伤害数字追踪
            case "桑多涅":
            {
                using (AvatarRecognition.BeginExclusiveOperation())
                {
                    var dpi = TaskContext.Instance().DpiScale;
                    var visConfig = AvatarRecognition.GetVisualRecognitionConfig();
                    var frameIntervalMs = visConfig.TargetingDetectionInterval;
                    var drawResults = visConfig.DrawRecognitionResults;
                    var lockLostWaitTime = visConfig.LockLostWaitTime;

                    Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);

                    DateTime? lastSeenTargetTime = null;
                    var startTime = DateTime.UtcNow;
                    var maxDurationMs = ms;
                    int overheatCount = 0;  // 红温连续命中计数

                    try
                    {
                        while (!avatar.Ct.IsCancellationRequested && (DateTime.UtcNow - startTime).TotalMilliseconds < maxDurationMs)
                        {
                            using (var capture = CaptureToRectArea())
                            {
                                // 距重击开始超过 3 秒后开始检测红温，连续命中 3 次（1/3 → 2/3 → 3/3）才提前退出
                                if ((DateTime.UtcNow - startTime).TotalSeconds >= 3)
                                {
                                    if (IsOverheated(capture))
                                    {
                                        overheatCount++;
                                        if (overheatCount >= 3)
                                        {
                                            Logger.LogInformation("桑多涅重击特化：连续 3 次检测到红温状态，提前退出");
                                            break;
                                        }

                                        Logger.LogInformation("桑多涅重击特化：检测到红温状态 {OverheatCount}/3", overheatCount);
                                    }
                                    else
                                    {
                                        overheatCount = 0;
                                    }
                                }

                                int preAimX = (int)(capture.Width * 0.5);
                                int preAimY = (int)(capture.Height * (480.0 / 1080.0));

                                var bars = AvatarRecognition.FindBloodBars(capture);
                                var valid = bars.Where(b => b.x > (int)(200 * AssetScale)).ToList();

                                var drawList = new System.Collections.Generic.List<View.Drawable.RectDrawable>();

                                bool hasLegendaryBar = valid.Any(b => AvatarRecognition.IsLegendaryBar(b.x, b.y));

                                if (valid.Count > 0 && !hasLegendaryBar)
                                {
                                    lastSeenTargetTime = DateTime.UtcNow;
                                    var nearest = valid.OrderBy(b => Math.Abs((b.x + b.width / 2) - preAimX) + Math.Abs((b.y + b.height / 2) - preAimY)).First();
                                    //Logger.LogInformation("追踪血条: 裁剪坐标({X},{Y}) 大小({W}×{H})", nearest.x, nearest.y, nearest.width, nearest.height);
                                    var offsetX = (nearest.x + nearest.width / 2) - preAimX;
                                    var offsetY = (nearest.y + nearest.height / 2) - preAimY;
                                    Simulation.SendInput.Mouse.MoveMouseBy((int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));

                                    if (drawResults)
                                    {
                                        foreach (var b in valid)
                                        {
                                            var rect = new OpenCvSharp.Rect(b.x, b.y, b.width, b.height);
                                            if (b.x == nearest.x && b.y == nearest.y && b.width == nearest.width && b.height == nearest.height)
                                                drawList.Add(capture.ToRectDrawable(rect, "target", _targetPen));
                                            else
                                                drawList.Add(capture.ToRectDrawable(rect, "blood"));
                                        }
                                    }
                                }
                                else
                                {
                                    var damageResult = AvatarRecognition.FindDamageNumber(capture);
                                    if (damageResult.HasValue)
                                    {
                                        var (dcx, dcy, _, dx, dy, dw, dh) = damageResult.Value;
                                        lastSeenTargetTime = DateTime.UtcNow;
                                        var offsetX = dcx - preAimX;
                                        var offsetY = dcy - preAimY;
                                        Simulation.SendInput.Mouse.MoveMouseBy((int)(offsetX * 0.35 * dpi), (int)(offsetY * 0.25 * dpi));
                                        if (drawResults)
                                        {
                                            drawList.Add(capture.ToRectDrawable(
                                                new OpenCvSharp.Rect(dx, dy, dw, dh),
                                                "damage_target",
                                                _targetPen));
                                        }
                                    }

                                    if (!damageResult.HasValue)
                                    {

                                        if (!hasLegendaryBar && (DateTime.UtcNow - (lastSeenTargetTime ?? startTime)).TotalSeconds >= 1.5)
                                        {
                                            Logger.LogInformation("桑多涅重击特化：超过1.5秒未找到目标，提前退出");
                                            View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("SandroneBloodBars", drawList);
                                            break;
                                        }

                                        if (!lastSeenTargetTime.HasValue || (DateTime.UtcNow - lastSeenTargetTime.Value).TotalSeconds >= lockLostWaitTime)
                                        {
                                            Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                                        }
                                    }
                                }

                                View.Drawable.VisionContext.Instance().DrawContent.PutOrRemoveRectList("SandroneBloodBars", drawList);
                            }

                            Sleep(frameIntervalMs);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    finally
                    {
                        View.Drawable.VisionContext.Instance().DrawContent.RemoveRect("SandroneBloodBars");
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
                    }
                }

                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// 恰斯卡是否处于飞行状态：特定位置白色像素识别（照抄 SkillBoostHelper.SpaceAtSecondPlaceExist）
    /// </summary>
    private static bool ChascaIsFlying()
    {
        using var region = CaptureToRectArea();
        return ChascaIsFlyingByPixel(region.SrcMat);
    }

    /// <summary>
    /// 恰斯卡是否处于飞行状态：使用已有截图判定（避免索敌循环中二次截图）
    /// </summary>
    private static bool ChascaIsFlyingByPixel(Mat src)
    {
        var pixel = src.At<Vec3b>(1028, 1584);
        return pixel.Item0 >= 250 && pixel.Item1 >= 250 && pixel.Item2 >= 250;
    }

    /// <summary>
    /// 恰斯卡 E 技能冷却秒数（OCR 识别，照抄 SkillBoostHelper.ReadEskillCdAsync 核心逻辑，无冷却跟踪副作用）
    /// 识别不到 CD 时返回 0，视为 E 可用
    /// </summary>
    private static double ReadEskillCdForChasca()
    {
        using var cdRegion = CaptureToRectArea();
        var eRa = cdRegion.DeriveCrop(AutoFightAssets.Get(cdRegion).ECooldownRect);
        using var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        var cd = StringUtils.TryParseDouble(text);
        // OCR 常丢失小数点：如 "0.3" 被读成 "03"，此时按 0.x 秒处理
        if (text != null && text.Length == 2 && text[0] == '0' && char.IsAsciiDigit(text[1]))
        {
            cd = (text[1] - '0') / 10.0;
        }
        return cd;
    }

    /// <summary>
    /// 恰斯卡飞行子弹状态：六个槽位各自的元素属性（空/风/火/水/雷/冰）
    /// </summary>
    private enum ChascaBulletType
    {
        Empty = 0,
        Anemo = 1,   // 风
        Pyro = 2,    // 火
        Hydro = 3,   // 水
        Electro = 4, // 雷
        Cryo = 5,    // 冰
    }

    /// <summary>
    /// 恰斯卡是否处于喷射状态：子弹框不存在时为喷射
    /// 子弹框特征评分低于 0.5 判定为子弹框不存在（正在喷射）
    /// </summary>
    private static bool ChascaIsSpraying(ImageRegion capture)
    {
        return ImageFeatureScorer.Score(_chascaBulletBoxModel, capture.SrcMat) < 0.5;
    }

    /// <summary>
    /// 恰斯卡飞行子弹识别：识别全部六个槽位（供日志完整输出），对每个槽位用对应元素模型评分，
    /// 取最高且超过阈值的元素（阈值由配置指定，默认 0.5）；该槽位没有任何可用模型（缺失）时直接返回空（0）
    /// 序列变化比较时忽略槽位 1 和槽位 6（见 ChascaSeqEquals）
    /// </summary>
    private static ChascaBulletType[] RecognizeChascaBullets(ImageRegion capture, double threshold)
    {
        var result = new ChascaBulletType[6];
        for (int pos = 0; pos < result.Length; pos++)
        {
            double bestScore = 0;
            ChascaBulletType bestType = ChascaBulletType.Empty;
            bool hasModel = false;
            for (int elemIdx = 0; elemIdx < 5; elemIdx++)
            {
                var model = _chascaBulletModels[pos, elemIdx];
                if (model == null) continue; // 缺失的元素模型不参与评分
                hasModel = true;
                double score = ImageFeatureScorer.Score(model, capture.SrcMat);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestType = (ChascaBulletType)(elemIdx + 1);
                }
            }
            // 该槽位无模型或最高分未过阈值：直接判定为空（返回 0）
            result[pos] = !hasModel || bestScore < threshold ? ChascaBulletType.Empty : bestType;
        }
        return result;
    }

    /// <summary>
    /// 子弹序列比较：只比较槽位 2-5（忽略槽位 1 与槽位 6，两者受子弹填充规则限制信息量低）；
    /// 空槽位与风元素视为等价（两者视觉特征易混淆，避免误判触发序列变化）
    /// </summary>
    private static bool ChascaSeqEquals(ChascaBulletType[] a, ChascaBulletType[] b)
    {
        if (a.Length != b.Length) return false;
        // 槽位 1、6 不参与比较，只比较索引 1-4
        for (int i = 1; i < a.Length - 1; i++)
        {
            var x = a[i] == ChascaBulletType.Empty ? ChascaBulletType.Anemo : a[i];
            var y = b[i] == ChascaBulletType.Empty ? ChascaBulletType.Anemo : b[i];
            if (x != y) return false;
        }
        return true;
    }
}

/// <summary>
/// 特化动作参数（由动作类型决定哪些字段生效）
/// </summary>
/// <param name="Hold">UseSkill 是否长按</param>
/// <param name="Ms">Charge 持续时间（毫秒）</param>
public sealed record ActionArgs(bool Hold = false, int Ms = 0);
