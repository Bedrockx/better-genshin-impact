using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace BetterGenshinImpact.View.Pages;

public partial class TaskSettingsPage : Page
{
    private TaskSettingsPageViewModel ViewModel { get; }

    public TaskSettingsPage(TaskSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        
        // 页面加载时初始化内置线路
        Loaded += (s, e) => ViewModel.InitializeBuiltinRoutes();
    }
    
    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        // 旋转寻敌开关变化时，不再自动覆盖 PaimonEndModel
        // PaimonEndModel 由用户通过"派蒙检查模式"开关独立控制
    }
    
    /// <summary>
    /// 处理调试路径输入框变化事件
    /// </summary>
    private void OnDebugRoutePathChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.OnDebugRoutePathChanged();
    }
}
