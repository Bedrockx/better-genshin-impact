using System;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

/// <summary>
/// 已兑换记录的薄包装 —— 只承担只读查询 + 转调到 <see cref="AutoRedeemCodeConfig"/> 的 public 方法。
/// </summary>
/// <remarks>
/// 所有真正会写状态的逻辑都落在 <see cref="AutoRedeemCodeConfig"/> 内部（因为只有该类内部能访问
/// <c>protected OnPropertyChanged</c>，从而可靠触发
/// <c>OnAnyPropertyChanged → ConfigService.Save</c>）。
/// 本类的存在主要是为了集中 <c>uid == "default"</c> 兜底场景的诊断日志（design.md 风险 2），
/// 以及让单元测试集中在 history store 接口上。
/// </remarks>
public class RedeemCodeHistoryStore
{
    private static readonly ILogger _logger = App.GetLogger<RedeemCodeHistoryStore>();
    private readonly AutoRedeemCodeConfig _config;

    public RedeemCodeHistoryStore(AutoRedeemCodeConfig config)
    {
        _config = config;
    }

    /// <summary>只读查询：某 UID 历史上是否已成功兑换过该兑换码。</summary>
    public bool IsRedeemed(string uid, string code)
    {
        if (uid == "default")
        {
            _logger.LogDebug("RedeemCodeHistoryStore.IsRedeemed 命中 default 兜底 UID，"
                + "可能源自一条龙配置未填写 GenshinUid，将与其它未填写 UID 的配置共享桶。");
        }
        return _config.UsedCodesByUid.TryGetValue(uid, out var inner)
               && inner.ContainsKey(code);
    }

    /// <summary>
    /// 转调到 <see cref="AutoRedeemCodeConfig.MarkRedeemed"/> —— 真正的写入与
    /// <c>OnPropertyChanged → Save</c> 触发由后者负责。
    /// </summary>
    public void MarkRedeemed(string uid, string code, string? valid)
    {
        if (uid == "default")
        {
            _logger.LogDebug("RedeemCodeHistoryStore.MarkRedeemed 命中 default 兜底 UID，"
                + "可能源自一条龙配置未填写 GenshinUid，将与其它未填写 UID 的配置共享桶。");
        }
        _config.MarkRedeemed(uid, code, valid);
    }

    /// <summary>转调到 <see cref="AutoRedeemCodeConfig.RemoveExpiredRedeemedCodes"/>。</summary>
    public void Cleanup(string today, DateTime now)
        => _config.RemoveExpiredRedeemedCodes(today, now);
}
