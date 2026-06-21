using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoSwitchRoles;

/// <summary>
/// 配对界面切换角色 - BetterGI 原生 C# 独立任务。
/// 1:1 复刻 JS 脚本 <c>User/JsScript/AutoSwitchRoles/main.js</c>（manifest 名「配对界面切换角色」）的流程
/// （坐标 / sleep / 重试次数）。在游戏「队伍配置 / 配对」界面按号位切换为指定角色（支持别名）。
/// 单机独立任务，不涉及联机 / SignalR。两种模式（RecommendedMode / QuickPairMode）均与 JS 等价，
/// QuickPairMode 含已知缺陷，按用户确认原样移植。
/// </summary>
public class AutoSwitchRolesTask : ISoloTask
{
    public string Name => "配对界面切换角色";

    private readonly ILogger<AutoSwitchRolesTask> _logger = App.GetLogger<AutoSwitchRolesTask>();
    private AutoSwitchRolesConfig _config = null!;
    private CancellationToken _ct;

    /// <summary>配置组传入的地图追踪配置（接口对齐，本任务不使用）。</summary>
    private readonly PathingPartyConfig? _partyConfig;

    /// <summary>配置组传入的独立任务配置覆盖，为 null 时使用全局 AutoSwitchRolesConfig。</summary>
    private readonly Dictionary<string, object?>? _settingsOverride;

    /// <summary>配置组名称。</summary>
    private readonly string? _groupName;

    private AutoSwitchRolesResources _res = null!;

    /// <summary>当前单轮打开配对界面尝试次数。</summary>
    private int _openTries;

    /// <summary>累计打开配对界面尝试次数（用于决定是否传送后重开）。</summary>
    private int _totalOpenTries;

    public AutoSwitchRolesTask(PathingPartyConfig? partyConfig = null,
        Dictionary<string, object?>? settings = null, string? groupName = null)
    {
        _partyConfig = partyConfig;
        _settingsOverride = settings;
        _groupName = groupName;
    }

    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        _config = TaskContext.Instance().Config.AutoSwitchRolesConfig;

        // 配置组传入覆盖时，在全局配置深拷贝上应用，避免污染全局单例（R3.4）
        if (_settingsOverride is { Count: > 0 })
        {
            var json = JsonSerializer.Serialize(_config);
            _config = JsonSerializer.Deserialize<AutoSwitchRolesConfig>(json) ?? _config;
            ApplySettingsOverride();
        }

        if (!string.IsNullOrEmpty(_groupName))
        {
            _logger.LogInformation("配对界面切换角色任务启动 [配置组: {Group}]", _groupName);
        }
        else
        {
            _logger.LogInformation("配对界面切换角色任务启动");
        }

        try
        {
            _res = new AutoSwitchRolesResources(_logger);

            // combat_avatar.json 缺失/解析失败 = 致命终止（R4.9）
            if (!_res.LoadAliasMap()) return;

            // 1. （C# 无需 setGameMetrics）返回主界面（R6.1）
            await new ReturnMainUiTask().Start(_ct);

            // 2. 模式分流（R6.3~R6.5）
            var mode = AutoSwitchRolesDecisions.SelectMode(_config.Option);
            switch (mode)
            {
                case SwitchRolesMode.Recommended:
                    // 切换队伍（R6.2）：仅 RecommendedMode 在主流程前切
                    if (!string.IsNullOrEmpty(_config.SwitchPartyName))
                    {
                        await new SwitchPartyTask().Start(_config.SwitchPartyName, _ct);
                    }
                    await RunRecommendedModeAsync();
                    break;
                case SwitchRolesMode.QuickPair:
                    await RunQuickPairModeAsync();
                    break;
                default:
                    _logger.LogWarning("未知模式「{Opt}」，返回主界面结束", _config.Option); // R6.5
                    await new ReturnMainUiTask().Start(_ct);
                    break;
            }

            // 5. 结束：返回主界面 + 清空队伍缓存（R10.5）
            await new ReturnMainUiTask().Start(_ct);
            RunnerContext.Instance.ClearCombatScenes();
        }
        catch (OperationCanceledException)
        {
            // R1.3：取消必须透传，停止后续操作
            _logger.LogInformation("配对界面切换角色任务被取消");
            throw;
        }
        catch (Exception ex)
        {
            // R1.5：非取消异常记结构化日志后结束，不向上抛导致配置组中断
            _logger.LogError(ex, "配对界面切换角色任务异常终止");
        }
    }

    // ====================== 输入封装（1080P 基准坐标，自动缩放） ======================

    /// <summary>点击 1080P 基准坐标（移动 + 左键单击）。</summary>
    private static void Click(double x, double y) => GameCaptureRegion.GameRegion1080PPosClick(x, y);

    /// <summary>移动到 1080P 基准坐标。</summary>
    private static void MoveMouseTo(double x, double y) => GameCaptureRegion.GameRegion1080PPosMove(x, y);

    /// <summary>相对移动鼠标。</summary>
    private static void MoveMouseBy(int dx, int dy) => Simulation.SendInput.Mouse.MoveMouseBy(dx, dy);

    private static void LeftButtonDown() => Simulation.SendInput.Mouse.LeftButtonDown();

    private static void LeftButtonUp() => Simulation.SendInput.Mouse.LeftButtonUp();

    private static void KeyPress(User32.VK vk) => Simulation.SendInput.Keyboard.KeyPress(vk);

    /// <summary>JS keyPress("VK_LBUTTON") 等价：鼠标左键单击。</summary>
    private static void LeftButtonClick() => Simulation.SendInput.Mouse.LeftButtonClick();

    /// <summary>
    /// 获取当前队伍角色名数组（对齐 GlobalMethod.GetAvatars，R5.3 / R10.1）。
    /// 空队返回空数组。
    /// </summary>
    private static string[] GetAvatars()
    {
        var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
        var avatars = combatScenes.GetAvatars();
        return avatars.Count > 0 ? avatars.Select(a => a.Name).ToArray() : [];
    }

    /// <summary>
    /// 翻页滚动，对应 JS <c>scrollPage</c>。起点 (400,750)、step 10、delay 5、松开前 sleep 700、松开后 sleep 500。
    /// </summary>
    private async Task ScrollPageAsync(double totalDistance, int stepDistance = 10, int delayMs = 5)
    {
        MoveMouseTo(400, 750);
        await Delay(50, _ct);
        LeftButtonDown();

        var steps = (int)Math.Ceiling(totalDistance / stepDistance);
        for (int j = 0; j < steps; j++)
        {
            var remaining = totalDistance - j * stepDistance;
            var move = remaining < stepDistance ? remaining : stepDistance;
            MoveMouseBy(0, -(int)Math.Round(move));
            await Delay(delayMs, _ct);
        }

        await Delay(700, _ct);
        LeftButtonUp();
        await Delay(500, _ct);
    }

    /// <summary>
    /// 将配置组传入的覆盖值应用到当前配置。键名与 JS settings.json 的 name 一致。
    /// </summary>
    private void ApplySettingsOverride()
    {
        if (_settingsOverride == null) return;

        T Get<T>(string key, T fallback)
        {
            if (_settingsOverride.TryGetValue(key, out var val) && val != null)
            {
                try
                {
                    // 处理 JsonElement 类型（System.Text.Json 反序列化 Dictionary<string, object?> 时的默认类型）
                    if (val is JsonElement jsonElement)
                    {
                        val = ConvertJsonElement(jsonElement);
                        if (val == null) return fallback;
                    }

                    // 处理 double -> int 转换时的 NaN/Infinity 和精度问题
                    if (typeof(T) == typeof(int) && val is double d)
                    {
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            return fallback;
                        if (Math.Abs(d - Math.Round(d)) < 1e-9)
                            return (T)(object)Convert.ToInt32(Math.Round(d));
                        return (T)(object)Convert.ToInt32(d);
                    }
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch { return fallback; }
            }
            return fallback;
        }

        // 将 JsonElement 转换为实际的值类型
        static object? ConvertJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        _config.SwitchPartyName = Get("switchPartyName", _config.SwitchPartyName);
        _config.Option = Get("option", _config.Option);
        _config.Position1 = Get("position1", _config.Position1);
        _config.Position2 = Get("position2", _config.Position2);
        _config.Position3 = Get("position3", _config.Position3);
        _config.Position4 = Get("position4", _config.Position4);
    }

    /// <summary>
    /// 配置组可配置参数定义，顺序严格与 JS settings.json 一致：
    /// switchPartyName → option → position1 → position2 → position3 → position4。
    /// </summary>
    public static List<SoloTaskSettingItem> GetSettingDefinitions()
    {
        var c = TaskContext.Instance().Config.AutoSwitchRolesConfig;
        return new List<SoloTaskSettingItem>
        {
            new() { Name = "switchPartyName", Label = "切换队伍（默认为当前配对）", Type = "text", DefaultValue = c.SwitchPartyName },
            new() { Name = "option", Label = "选择模式", Type = "select", DefaultValue = c.Option,
                Options = new() { "推荐-非快速配对模式 @Tool_tingsu", "存在bug-快速配对模式 @兩夢三醒" } },
            new() { Name = "position1", Label = "1号位（空选即不处理，支持别名）", Type = "text", DefaultValue = c.Position1 },
            new() { Name = "position2", Label = "2号位（空选即不处理，支持别名）", Type = "text", DefaultValue = c.Position2 },
            new() { Name = "position3", Label = "3号位（空选即不处理，支持别名）", Type = "text", DefaultValue = c.Position3 },
            new() { Name = "position4", Label = "4号位（空选即不处理，支持别名）", Type = "text", DefaultValue = c.Position4 },
        };
    }

    // ====================== 共用：打开配对界面 OpenPairingInterface（R7） ======================

    /// <summary>
    /// 打开配对界面（对应 JS <c>openPairingInterface</c>）。按 L 后等待 <paramref name="openWaitMs"/>，
    /// 用「队伍配置.png」模板确认。单轮最多 3 次（R7.2）；单轮耗尽后，累计 &lt; 6 次则传送到固定坐标后递归重开
    /// （R7.3），累计达 6 次失败终止（R7.4）。队伍配置模板在进入流程前读一次缓存复用，避免每轮重读。
    /// </summary>
    private async Task<bool> OpenPairingInterface(int openWaitMs, Mat? teamConfigTpl)
    {
        while (AutoSwitchRolesDecisions.ShouldRetrySingleRound(_openTries)) // _openTries < 3
        {
            KeyPress(User32.VK.VK_L);
            await Delay(openWaitMs, _ct);

            var region = CaptureToRectArea();
            // 模板缺失（teamConfigTpl == null）→ 无法识别，本轮视为失败（与 JS「读图异常即终止」对齐，
            // 但本任务把致命限定在 combat_avatar.json，模板缺失按可恢复 → 重试耗尽后 OpenPairing 返回 false）。
            if (teamConfigTpl != null)
            {
                var ro = RecognitionObject.TemplateMatch(teamConfigTpl, 0, 0, 1920, 1080);
                if (region.Find(ro).IsExist())
                {
                    _openTries = 0;
                    return true;
                }
            }

            _openTries++;
            _totalOpenTries++;
        }

        if (AutoSwitchRolesDecisions.ShouldTeleportAndReopen(_totalOpenTries)) // _totalOpenTries < 6
        {
            await new TpTask(_ct).Tp(2297.630859375, -824.5517578125);
            _openTries = 0;
            return await OpenPairingInterface(openWaitMs, teamConfigTpl);
        }

        _logger.LogError("无法打开配对界面，任务结束");
        return false;
    }

    // ====================== 推荐-非快速配对模式 RecommendedMode（R8） ======================

    private async Task RunRecommendedModeAsync()
    {
        var positionCoordinates = new (double X, double Y)[]
        {
            (460, 538), (792, 538), (1130, 538), (1462, 538)
        };

        var initialAvatars = GetAvatars();
        var positionResolved = new[]
        {
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position1, _res.AliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position2, _res.AliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position3, _res.AliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position4, _res.AliasMap),
        };
        var targetAvatars = AutoSwitchRolesDecisions.BuildTargetAvatars(initialAvatars, positionResolved);
        _logger.LogInformation("目标角色: [{Target}]", string.Join(", ", targetAvatars));

        if (AutoSwitchRolesDecisions.IsAllEmpty(positionResolved)) // R5.4
        {
            _logger.LogInformation("未设置任何角色，跳过切换");
            await new ReturnMainUiTask().Start(_ct);
            return;
        }

        // 模板缓存复用（避免每轮重读）
        var teamConfigTpl = _res.TryReadTemplate("Assets/RecognitionObject/队伍配置.png");
        var replaceTpl = _res.TryReadTemplate("Assets/RecognitionObject/更换.png");
        var joinTpl = _res.TryReadTemplate("Assets/RecognitionObject/加入.png");
        var filterConfig = _res.LoadFilterConfig();
        var noResultTpl = _res.TryReadTemplate("Assets/RecognitionObject/暂无筛选结果.png");

        var retryCount = 0;
        var switchSuccess = false;
        while (retryCount < 2) // R10.3 总尝试次数最多 2 次
        {
            if (!await SwitchCharactersRecommended(positionCoordinates, positionResolved,
                    teamConfigTpl, replaceTpl, joinTpl, filterConfig, noResultTpl))
            {
                _logger.LogError("切换过程失败");
                return;
            }

            await new ReturnMainUiTask().Start(_ct);
            var finalAvatars = GetAvatars();
            if (AutoSwitchRolesDecisions.AvatarsEqual(targetAvatars, finalAvatars)) // R10.2
            {
                _logger.LogInformation("角色切换成功");
                switchSuccess = true;
                break;
            }

            retryCount++;
            if (retryCount >= 2)
            {
                _logger.LogError("角色切换失败");
                return;
            }

            await new ReturnMainUiTask().Start(_ct);
        }

        if (!switchSuccess)
        {
            _logger.LogError("角色切换失败");
        }
    }

    private async Task<bool> SwitchCharactersRecommended(
        (double X, double Y)[] positionCoordinates,
        string?[] positionResolved,
        Mat? teamConfigTpl,
        Mat? replaceTpl,
        Mat? joinTpl,
        Dictionary<string, (string? Element, string? Weapon)> filterConfig,
        Mat? noResultTpl)
    {
        if (!await OpenPairingInterface(3200, teamConfigTpl)) return false;

        for (int i = 0; i < 4; i++)
        {
            var selected = positionResolved[i];
            if (selected == null)
            {
                _logger.LogInformation("未设置{Num}号位，跳过", i + 1);
                continue;
            }

            Click(positionCoordinates[i].X, positionCoordinates[i].Y);
            await Delay(1000, _ct);

            // 筛选块（R8.2 / R8.3）：仅当 filterConfig 有该角色 且 noResultTpl 可用
            var hasNoFilterResult = false;
            if (filterConfig.TryGetValue(selected, out var f) && noResultTpl != null)
            {
                var filterTpl = _res.TryReadTemplate("Assets/RecognitionObject/筛选.png");
                if (filterTpl != null)
                {
                    var filterBtn = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(filterTpl, 0, 0, 1920, 1080));
                    if (filterBtn.IsExist())
                    {
                        filterBtn.Click();
                        await Delay(200, _ct);

                        if (f.Element != null)
                        {
                            var elemTpl = _res.TryReadTemplate($"Assets/RecognitionObject/{f.Element}.png");
                            if (elemTpl != null)
                            {
                                var eBtn = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(elemTpl, 0, 0, 1920, 1080));
                                if (eBtn.IsExist()) { eBtn.Click(); await Delay(200, _ct); }
                                else _logger.LogWarning("未找到元素筛选图标: {Element}", f.Element);
                            }
                            else
                            {
                                _logger.LogWarning("元素筛选图标模板缺失，跳过元素筛选: {Element}", f.Element);
                            }
                        }

                        if (f.Weapon != null)
                        {
                            var wpnTpl = _res.TryReadTemplate($"Assets/RecognitionObject/{f.Weapon}.png");
                            if (wpnTpl != null)
                            {
                                var wBtn = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(wpnTpl, 0, 0, 1920, 1080));
                                if (wBtn.IsExist()) { wBtn.Click(); await Delay(200, _ct); }
                                else _logger.LogWarning("未找到武器筛选图标: {Weapon}", f.Weapon);
                            }
                            else
                            {
                                _logger.LogWarning("武器筛选图标模板缺失，跳过武器筛选: {Weapon}", f.Weapon);
                            }
                        }

                        var confirmTpl = _res.TryReadTemplate("Assets/RecognitionObject/确认筛选.png");
                        if (confirmTpl != null)
                        {
                            var confirm = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(confirmTpl, 0, 0, 1920, 1080));
                            if (confirm.IsExist())
                            {
                                confirm.Click();
                                await Delay(500, _ct);

                                var noResult = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(noResultTpl, 0, 0, 1920, 1080));
                                if (noResult.IsExist()) // R8.3
                                {
                                    _logger.LogWarning("筛选后无结果，跳过{Num}号位", i + 1);
                                    hasNoFilterResult = true;
                                    for (int k = 0; k < 3; k++)
                                    {
                                        KeyPress(User32.VK.VK_ESCAPE);
                                        await Delay(200, _ct);
                                    }
                                }
                            }
                            else _logger.LogWarning("未找到确认筛选图标");
                        }
                        else _logger.LogWarning("确认筛选图标模板缺失");
                    }
                    else _logger.LogWarning("未找到筛选图标");
                }
                else _logger.LogWarning("筛选图标模板缺失，跳过筛选");
            }

            if (hasNoFilterResult) continue;

            // 角色头像查找（R8.4）：最多 3 页，每页内 num 从 1 递增直到头像文件不存在
            var characterFound = false;
            var pageTries = 0;
            while (pageTries < 3)
            {
                for (int num = 1; ; num++)
                {
                    var mat = _res.TryReadCharacterImage(AutoSwitchRolesDecisions.CharacterImageFileName(selected, num));
                    if (mat == null) break; // 首个不存在即停（R4.6）

                    var res = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(mat, 0, 0, 1920, 1080));
                    if (res.IsExist())
                    {
                        _logger.LogInformation("已找到角色「{Name}」", selected);
                        res.Click();
                        await Delay(200, _ct);
                        characterFound = true;
                        break;
                    }
                }

                if (characterFound) break;
                if (pageTries < 3)
                {
                    _logger.LogInformation("滚动页面");
                    await ScrollPageAsync(350);
                }
                pageTries++;
            }

            if (!characterFound)
            {
                _logger.LogError("未找到角色「{Name}」", selected); // R8.6
                continue;
            }

            // 更换 / 加入（R8.5）
            var replace = replaceTpl != null
                ? CaptureToRectArea().Find(RecognitionObject.TemplateMatch(replaceTpl, 0, 0, 1920, 1080))
                : new Region();
            var join = joinTpl != null
                ? CaptureToRectArea().Find(RecognitionObject.TemplateMatch(joinTpl, 0, 0, 1920, 1080))
                : new Region();

            if (replace.IsExist() || join.IsExist())
            {
                await Delay(300, _ct);
                (replace.IsExist() ? replace : join).Click();
                LeftButtonClick(); // JS keyPress("VK_LBUTTON")
                await Delay(500, _ct);
            }
            else
            {
                _logger.LogError("该角色已在队伍中，无需切换");
                await Delay(300, _ct);
                KeyPress(User32.VK.VK_ESCAPE);
                await Delay(500, _ct);
            }

            await Delay(500, _ct);
        }

        return true;
    }

    // ====================== 存在bug-快速配对模式 QuickPairMode（R9，含已知缺陷，1:1 移植） ======================

    private async Task RunQuickPairModeAsync()
    {
        // 缺陷1 / R9.1 设计裁定：JS 源 QuickPairMode 切队伍块读的是「不存在的」settings.partyName 字段（非
        // switchPartyName），故 `!!settings.partyName` 恒为 false，QuickPairMode 实际从不切换队伍，直接走
        // `else { returnMainUi() }`。按用户确认「1:1 移植含缺陷，保证行为与 JS 等价」，C# 保留此缺陷语义：
        // QuickPairMode 不读 SwitchPartyName，切队伍块走 else 分支（仅返回主界面）。若后续要修复，应让此处读
        // _config.SwitchPartyName 并加 TpToStatueOfTheSeven 兜底（API 已备好），但不在本次迁移内。
        await new ReturnMainUiTask().Start(_ct);

        var initialAvatars = GetAvatars();
        var positionCoordinates = new (double X, double Y)[]
        {
            (107, 190), (254, 188), (414, 189), (554, 198)
        };
        var positionResolved = new[]
        {
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position1, _res.AliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position2, _res.AliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position3, _res.AliasMap),
            AutoSwitchRolesDecisions.ResolvePosition(_config.Position4, _res.AliasMap),
        };
        var targetAvatars = AutoSwitchRolesDecisions.BuildTargetAvatars(initialAvatars, positionResolved);
        _logger.LogInformation("目标角色: [{Target}]", string.Join(", ", targetAvatars));

        if (AutoSwitchRolesDecisions.IsAllEmpty(positionResolved))
        {
            _logger.LogInformation("未设置任何角色，跳过切换");
            await new ReturnMainUiTask().Start(_ct);
            return;
        }

        var teamConfigTpl = _res.TryReadTemplate("Assets/RecognitionObject/队伍配置.png");

        var retryCount = 0;
        var switchSuccess = false;
        while (retryCount < 2)
        {
            if (!await SwitchCharactersQuick(positionCoordinates, positionResolved, teamConfigTpl))
            {
                _logger.LogError("切换过程失败");
                return;
            }

            await new ReturnMainUiTask().Start(_ct);
            var finalAvatars = GetAvatars();
            if (AutoSwitchRolesDecisions.AvatarsEqual(targetAvatars, finalAvatars))
            {
                _logger.LogInformation("角色切换成功");
                switchSuccess = true;
                break;
            }

            retryCount++;
            if (retryCount >= 2)
            {
                _logger.LogError("角色切换失败");
                return;
            }

            await new ReturnMainUiTask().Start(_ct);
        }

        if (!switchSuccess)
        {
            _logger.LogError("角色切换失败");
        }
    }

    private async Task<bool> SwitchCharactersQuick(
        (double X, double Y)[] positionCoordinates,
        string?[] positionResolved,
        Mat? teamConfigTpl)
    {
        if (!await OpenPairingInterface(3500, teamConfigTpl)) return false;

        // 统计有文字区域数（R9.2，对应 JS regionsWithTextCount）
        var ocrRegions = new (double X, double Y, double W, double H)[]
        {
            (340, 181, 315, 330), (655, 181, 315, 330), (970, 181, 315, 330), (1285, 181, 315, 330)
        };
        var region = CaptureToRectArea();
        var regionsWithTextCount = 0;
        foreach (var (x, y, w, h) in ocrRegions)
        {
            if (region.FindMulti(RecognitionObject.Ocr(x, y, w, h)).Count > 0)
            {
                regionsWithTextCount++;
            }
        }
        _logger.LogInformation("有文字的区域数量为: {Count}", regionsWithTextCount);

        Click(1212, 1020);
        await Delay(1000, _ct);
        _logger.LogInformation("点击快速编队"); // R9.3

        for (int i = 0; i < regionsWithTextCount; i++)
        {
            if (i >= positionCoordinates.Length) break;
            Click(positionCoordinates[i].X, positionCoordinates[i].Y); // 取消已有角色
            await Delay(1000, _ct);
        }

        // 逐号位重选（R9.4 / R9.5）
        for (int i = 0; i < 4; i++)
        {
            var selected = positionResolved[i];
            var (x, y) = positionCoordinates[i];

            Click(800, 123); // 添加
            await Delay(1000, _ct);

            if (selected == null)
            {
                _logger.LogInformation("未设置{Num}号位，保持原选择（可能存在未知bug）", i + 1);
                Click(x, y);
                await Delay(1000, _ct);
                continue;
            }

            _logger.LogInformation("开始设置{Num}号位，目标角色【{Name}】", i + 1, selected);

            var characterFound = false;
            var pageTries = 0;
            while (pageTries < 40)
            {
                for (int num = 1; ; num++)
                {
                    var mat = _res.TryReadCharacterImage(AutoSwitchRolesDecisions.CharacterImageFileName(selected, num));
                    if (mat == null) break;

                    var res = CaptureToRectArea().Find(RecognitionObject.TemplateMatch(mat, 0, 0, 1920, 1080));
                    if (res.IsExist())
                    {
                        _logger.LogInformation("已找到角色「{Name}」", selected);
                        res.Click();
                        await Delay(500, _ct);
                        characterFound = true;
                        break;
                    }
                }

                if (characterFound) break;
                if (pageTries < 30)
                {
                    _logger.LogInformation("滚动页面");
                    await ScrollPageAsync(200);
                }
                if (pageTries == 15)
                {
                    _logger.LogInformation("重置位置，再试一次");
                    Click(800, 123);
                    await Delay(1000, _ct);
                }
                pageTries++;
            }

            if (!characterFound)
            {
                _logger.LogError("未找到「{Name}」，尝试选择原来的角色", selected); // R9.5
                Click(800, 123);
                await Delay(1000, _ct);
                Click(x, y);
                await Delay(1000, _ct);
                continue;
            }
        }

        // 保存（R9.6）
        Click(427, 1024);
        await Delay(1000, _ct);
        LeftButtonClick();
        await Delay(500, _ct);
        return true;
    }
}
