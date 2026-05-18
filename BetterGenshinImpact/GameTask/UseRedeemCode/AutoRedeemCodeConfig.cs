using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

[Serializable]
public partial class AutoRedeemCodeConfig: ObservableObject
{
    /// <summary>
    /// 是否启用剪切板监听
    /// </summary>
    [ObservableProperty]
    private bool _clipboardListenerEnabled = true;

    /// <summary>
    /// 是否在一条龙启动时自动检查兑换码
    /// </summary>
    [ObservableProperty]
    private bool _autoRedeemCodeCheckEnabled = false;

    /// <summary>
    /// 每个UID上次检查兑换码的日期 (key=UID, value=yyyy-MM-dd格式)
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, string> _lastRedeemCodeCheckDates = new();

    /// <summary>
    /// 每个 UID 已成功兑换过的兑换码记录
    /// (key=UID, value=该 UID 的 code → <see cref="UsedCodeRecord"/> 字典)
    /// 由 ConfigService.OnAnyPropertyChanged → Save 自动持久化到 User/config.json。
    /// 字段初始化器 <c>new()</c> 保证旧版 JSON（缺 <c>usedCodesByUid</c>）反序列化后仍为空字典而不是 null。
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, Dictionary<string, UsedCodeRecord>> _usedCodesByUid = new();

    /// <summary>
    /// 记录某 UID 成功兑换某兑换码。内部 mutate + 显式触发 PropertyChanged，
    /// 这是触发 ConfigService.Save 的唯一可靠路径
    /// （<c>[ObservableProperty]</c> 对相同引用赋值会被 short-circuit，不触发 PropertyChanged）。
    /// </summary>
    public void MarkRedeemed(string uid, string code, string? valid)
    {
        if (!_usedCodesByUid.TryGetValue(uid, out var inner))
        {
            inner = new Dictionary<string, UsedCodeRecord>();
            _usedCodesByUid[uid] = inner;
        }
        inner[code] = new UsedCodeRecord { Valid = valid, RedeemedAt = DateTime.Now };
        OnPropertyChanged(nameof(UsedCodesByUid));
    }

    /// <summary>
    /// 清理过期 / 兜底 TTL 已超的已兑换记录。语义不变量（防止 Bug A 回归）：
    /// <list type="bullet">
    /// <item>Valid 非空 AND Valid &lt; today                    → 删除（A：明确过期）</item>
    /// <item>Valid 为空 AND now - RedeemedAt &gt; 30 days       → 删除（C：仅 Valid 缺失时兜底）</item>
    /// <item>其它（包括 Valid 非空且 &gt;= today 但 RedeemedAt 极久）→ 保留</item>
    /// </list>
    /// 必须使用 <c>else if</c> 显式守卫 Valid 为空才进入 30 天 TTL 分支，否则会误删
    /// <c>Valid="2099-12-31", RedeemedAt=now-365days</c> 这类长期未过期记录。
    /// </summary>
    public void RemoveExpiredRedeemedCodes(string today, DateTime now)
    {
        var emptyUids = new List<string>();
        foreach (var (uid, inner) in _usedCodesByUid)
        {
            var toRemove = new List<string>();
            foreach (var (code, record) in inner)
            {
                bool hasValid = !string.IsNullOrEmpty(record.Valid);
                if (hasValid && string.Compare(record.Valid, today, StringComparison.Ordinal) < 0)
                {
                    toRemove.Add(code); // A: explicitly expired
                }
                else if (!hasValid && (now - record.RedeemedAt) > TimeSpan.FromDays(30))
                {
                    toRemove.Add(code); // C: TTL fallback only when Valid is missing
                }
            }
            foreach (var code in toRemove)
            {
                inner.Remove(code);
            }
            if (inner.Count == 0)
            {
                emptyUids.Add(uid);
            }
        }
        foreach (var uid in emptyUids)
        {
            _usedCodesByUid.Remove(uid);
        }
        OnPropertyChanged(nameof(UsedCodesByUid));
    }

    /// <summary>
    /// 附带修复（design.md 风险 1）：更新某 UID 的当日已检查日期。
    /// 现状直接索引赋值不会触发 <c>[ObservableProperty]</c> 的 setter，因此不会触发
    /// PropertyChanged，本方法集中处理 mutate + 显式触发，避免本次主修复因为同一隐性
    /// 依赖而退化。
    /// </summary>
    public void UpdateLastCheckDate(string uid, string date)
    {
        _lastRedeemCodeCheckDates[uid] = date;
        OnPropertyChanged(nameof(LastRedeemCodeCheckDates));
    }
}

/// <summary>
/// 已兑换记录的 POCO，作为 <see cref="AutoRedeemCodeConfig.UsedCodesByUid"/> 内层 value。
/// 字段命名遵循 STJ camelCase 序列化（<c>valid</c>、<c>redeemedAt</c>），
/// 与 <c>ConfigService.JsonOptions.PropertyNamingPolicy = CamelCase</c> 兼容。
/// </summary>
[Serializable]
public class UsedCodeRecord
{
    /// <summary>
    /// 兑换码声明的有效期，<c>yyyy-MM-dd</c>；可空（剪贴板路径或 codes.json 未带此字段时为空）。
    /// 用于 Strategy A 清理：<c>Valid &lt; today</c> 时移除。
    /// </summary>
    public string? Valid { get; set; }

    /// <summary>
    /// 首次兑换成功的本地时间。
    /// 用于 Strategy C 兜底清理：仅当 <c>Valid</c> 为空且 <c>RedeemedAt &lt; now - 30 days</c> 时移除。
    /// </summary>
    public DateTime RedeemedAt { get; set; }
}
