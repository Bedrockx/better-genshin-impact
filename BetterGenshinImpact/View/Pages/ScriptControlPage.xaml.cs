using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;


namespace BetterGenshinImpact.View.Pages;

public partial class ScriptControlPage
{
    public ScriptControlViewModel ViewModel { get; }

    public ScriptControlPage(ScriptControlViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
    
    //聚焦函数
    private void ConfigRow_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext != null)
        {
            var listView = FindParent<ListView>(fe);
            if (listView != null)
                listView.SelectedItem = fe.DataContext;
            
        }
    }
    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && !(parent is T))
            parent = VisualTreeHelper.GetParent(parent);
        return parent as T;
    }
}
