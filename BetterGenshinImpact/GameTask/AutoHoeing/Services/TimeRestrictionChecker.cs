using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoHoeing.Services;

/// <summary>
/// 时间限制检查器：解析不运行时段，判断当前是否在限制时段内
/// </summary>
public class TimeRestrictionChecker
{
    private static readonly ILogger Logger = App.GetLogger<TimeRestrictionChecker>();
    private readonly List<(int startMinutes, int endMinutes)> _restrictions = new();

    /// <summary>
    /// 解析不运行时段配置字符串
    /// 支持：单个小时(8)、连续区间(8-11, 23:11-23:55)、中文逗号分隔
    /// </summary>
    public void ParseRestrictions(string restrictionString)
    {
        _restrictions.Clear();
        if (string.IsNullOrWhiteSpace(restrictionString)) return;

        var clean = restrictionString.Replace("，", ",").Replace("：", ":");
        foreach (var seg in clean.Split(','))
        {
            var s = seg.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            try
            {
                if (s.Contains('-'))
                {
                    var parts = s.Split('-');
                    var start = ParseTime(parts[0].Trim(), false);
                    var end = ParseTime(parts[1].Trim(), true);
                    _restrictions.Add((start, end));
                }
                else
                {
                    var start = ParseTime(s, false);
                    var end = ParseTime(s, true);
                    _restrictions.Add((start, end));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("解析时间限制失败 '{Seg}': {Msg}", s, ex.Message);
            }
        }
    }

    private static int ParseTime(string str, bool isEnd)
    {
        if (str.Contains(':'))
        {
            var parts = str.Split(':');
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        }
        var h = int.Parse(str);
        return h * 60 + (isEnd ? 59 : 0);
    }

    public bool IsInRestrictedPeriod()
    {
        var now = DateTime.Now;
        var current = now.Hour * 60 + now.Minute;

        foreach (var (start, end) in _restrictions)
        {
            var effectiveEnd = end >= start ? end : end + 24 * 60;
            if ((current >= start && current < effectiveEnd)
                || (current + 24 * 60 >= start && current + 24 * 60 < effectiveEnd))
                return true;
        }
        return false;
    }

    public bool IsApproachingRestriction(int thresholdMinutes = 10)
    {
        var now = DateTime.Now;
        var current = now.Hour * 60 + now.Minute;

        foreach (var (start, _) in _restrictions)
        {
            var nextStart = start;
            if (nextStart <= current) nextStart += 24 * 60;
            var wait = nextStart - current;
            if (wait > 0 && wait <= thresholdMinutes)
                return true;
        }
        return false;
    }

    public async Task WaitUntilAllowed(CancellationToken ct)
    {
        while (IsInRestrictedPeriod() && !ct.IsCancellationRequested)
        {
            Logger.LogInformation("处于限制时间内，等待中...");
            await Task.Delay(60_000, ct);
        }
    }
}
