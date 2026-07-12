using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.UseRedeemCode.Model;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Rect = OpenCvSharp.Rect;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

public class UseRedemptionCodeTask : ISoloTask
{
    private static readonly ILogger _logger = App.GetLogger<UseRedemptionCodeTask>();


    private readonly List<RedeemCode> _list;
    private readonly string? _uid;
    private readonly RedeemCodeHistoryStore _historyStore;

    public UseRedemptionCodeTask(List<RedeemCode> list, string? uid = null)
    {
        _list = list;
        _uid = uid;
        _historyStore = new RedeemCodeHistoryStore(TaskContext.Instance().Config.AutoRedeemCodeConfig);
    }

    public UseRedemptionCodeTask(List<string> strList, string? uid = null)
    {
        _list = strList
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => new RedeemCode(code, null))
            .ToList();
        _uid = uid;
        _historyStore = new RedeemCodeHistoryStore(TaskContext.Instance().Config.AutoRedeemCodeConfig);
    }

    public string Name => "使用兑换码";

    public async Task Start(CancellationToken ct)
    {
        InitLog(_list);

        try
        {
            Rect captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;

            await new ReturnMainUiTask().Start(ct);

            var page = new BvPage(ct);

            _logger.LogInformation("使用兑换码: {Msg}", "打开设置");
            // 按ESC键打开菜单
            page.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            // 等待ESC后菜单出现
            await page.Locator(new BvImage("UseRedeemCode:esc_return_button.png")).WaitFor();
            // 点击设置按钮
            page.Click(45, 825);
            await page.Wait(1000);

            // 点击账户
            _logger.LogInformation("使用兑换码: {Msg}", "点击账户 —— 前往兑换");
            await page.GetByText("账户").WithRoi(captureRect.CutLeft(0.2)).Click();
            await page.Wait(300);

            // 点击前往兑换
            await page.GetByText("前往兑换").WithRoi(captureRect.CutRight(0.3)).Click();

            // 等待兑换码输入框出现
            await page.GetByText("兑换奖励").WaitFor();


            foreach (var redeemCode in _list)
            {
                if (string.IsNullOrEmpty(redeemCode.Code))
                {
                    continue;
                }

                await UseRedeemCode(redeemCode, page);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("使用兑换码时发生错误: {Message}", ex.Message);
            _logger.LogDebug(ex, "使用兑换码时发生错误");
        }
        finally
        {
            // 清空剪贴板
            UIDispatcherHelper.Invoke(Clipboard.Clear);
            // 返回主界面
            await new ReturnMainUiTask().Start(ct);
            
        }
    }

    private async Task UseRedeemCode(RedeemCode redeemCode, BvPage page)
    {
        Rect captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;

        _logger.LogInformation("输入兑换码: {Code}", redeemCode.Code);
        // 将要输入的文本复制到剪贴板
        UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(redeemCode.Code!));
        // 粘贴兑换码
        await page.GetByText("粘贴").WithRoi(captureRect.CutRight(0.5)).Click();
        // 点击兑换
        await page.Locator(ElementRecognition.Get("BtnWhiteConfirm")).Click();

        // === OQ-1 (a) 锚点：BtnWhiteConfirm.Click 已 await 返回 = 已向服务器提交一次 (uid, code)。
        // 立即标记为终态，无视后续 OCR 结果（OQ-2 a 删除拒绝识别 / OQ-6 b 仅做日志区分）。
        // 罕见的"提交未真正成功"少数派由用户接受为可接受代价（bugfix.md 2.4）。 ===
        // 总是写入会话级成功 short-circuit（不分 UID、不持久化），覆盖剪贴板路径与显式 UID 路径，
        // 避免同会话内对同一码重复触发兑换 UI（3.14 / 风险 5 不变量）。
        RedeemCodeCache.MarkAsSucceededInSession(redeemCode.Code);
        // OQ-4 (a) 守卫：剪贴板路径（_uid 为 null/empty）不写持久化，与 3.8 不变量保持一致。
        // 仅在显式传入 UID 时写入跨进程持久化记录，避免污染任何 UID 桶。
        if (!string.IsNullOrEmpty(_uid))
        {
            _historyStore.MarkRedeemed(_uid, redeemCode.Code, redeemCode.Valid);
        }

        // === OQ-6 (b) 保留 1 秒 OCR 等待 —— 仅做日志区分，不再影响标记决策。 ===
        var list = await page.GetByText("兑换成功").TryWaitFor(1000);
        if (list.Count > 0)
        {
            _logger.LogInformation("兑换码 {Code} 兑换成功", redeemCode.Code);
            // 成功路径仍点击确认按钮 + 等待动画（3.10 不变量）。
            await page.Locator(ElementAssets.Instance.BtnBlackConfirm).Click();
            await page.Wait(5100);
            return;
        }

        // OCR 未识别到"兑换成功" —— 可能是已使用 / 已过期 / 不存在 / 无效 / 网络异常等任意状态。
        // 标记已在 OQ-1 锚点完成，这里不再决定持久化；仅打日志区分。
        _logger.LogInformation(
            "兑换码 {Code} 提交完成，未识别到兑换成功文本（可能是已使用 / 过期 / 网络异常）",
            redeemCode.Code);
        // OQ-3 (b) 降级保留：剪贴板路径（_uid 空）补一个会话级失败缓存（1 天 TTL），
        // 用于同会话内若用户再次粘贴同一码时由 FilterExpiredCodes 第 3 排除条件 short-circuit。
        // _uid 非空路径不调 MarkAsFailed —— 持久化层已在 OQ-1 锚点写入，
        // 再调失败缓存会让同一码同时存在于"已兑换桶"与"会话失败缓存"，语义混乱。
        if (string.IsNullOrEmpty(_uid))
        {
            RedeemCodeCache.MarkAsFailed(redeemCode.Code);
        }
        await page.GetByText("清除").WithRoi(captureRect.CutRight(0.5)).Click();
    }

    private static void InitLog(List<RedeemCode> list)
    {
        _logger.LogInformation("开始使用兑换码:");
        foreach (var redeemCode in list)
        {
            if (string.IsNullOrEmpty(redeemCode.Items))
            {
                _logger.LogInformation("{Code}", redeemCode.Code);
            }
            else
            {
                _logger.LogInformation("{Code} - {Msg}", redeemCode.Code, redeemCode.Items);
            }
        }
    }
}
