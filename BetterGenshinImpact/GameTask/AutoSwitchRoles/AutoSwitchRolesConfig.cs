using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoSwitchRoles;

/// <summary>
/// 配对界面切换角色任务配置。设置项与 JS 脚本 settings.json 完全一致（6 项）。
/// </summary>
[Serializable]
public partial class AutoSwitchRolesConfig : ObservableObject
{
    /// <summary>切换队伍名，空=不切换队伍（默认为当前配对）。对应 JS switchPartyName。</summary>
    [ObservableProperty] private string _switchPartyName = "";

    /// <summary>模式选择，默认推荐-非快速配对模式。对应 JS option。</summary>
    [ObservableProperty] private string _option = "推荐-非快速配对模式 @Tool_tingsu";

    /// <summary>1号位角色名/别名，空=不处理。对应 JS position1。</summary>
    [ObservableProperty] private string _position1 = "";

    /// <summary>2号位角色名/别名，空=不处理。对应 JS position2。</summary>
    [ObservableProperty] private string _position2 = "";

    /// <summary>3号位角色名/别名，空=不处理。对应 JS position3。</summary>
    [ObservableProperty] private string _position3 = "";

    /// <summary>4号位角色名/别名，空=不处理。对应 JS position4。</summary>
    [ObservableProperty] private string _position4 = "";
}
