using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.OcrSwitchWeapon;

[Serializable]
public partial class OcrSwitchWeaponConfig : ObservableObject
{
    /// <summary>目标角色中文名或别名，空=运行期回退「纳西妲」</summary>
    [ObservableProperty] private string _character = "";

    /// <summary>目标武器中文名或简称，空=运行期回退「试作金珀」</summary>
    [ObservableProperty] private string _weapon = "";

    /// <summary>元素筛选，「物」=不筛选</summary>
    [ObservableProperty] private string _element = "物";

    /// <summary>快速模式开关，默认 true</summary>
    [ObservableProperty] private bool _quickMode = true;

    /// <summary>武器格子行列，如「73」=第7行第3列，空=不指定</summary>
    [ObservableProperty] private string _gridPosition = "";

    /// <summary>最大滑页次数，默认「2」（字符串保持与 JS input-text 一致，运行期 Clamp 到 [0,99]）</summary>
    [ObservableProperty] private string _pageScrollCount = "2";
}
