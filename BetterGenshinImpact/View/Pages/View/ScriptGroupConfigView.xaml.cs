using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.ViewModel.Pages.View;
using System.Windows;
using System.Windows.Controls.Primitives;


namespace BetterGenshinImpact.View.Pages.View
{
    /// <summary>
    /// ScriptGroupConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class ScriptGroupConfigView : UserControl
    {
        private ScriptGroupConfigViewModel ViewModel { get; }

        public ScriptGroupConfigView(ScriptGroupConfigViewModel viewModel)
        {
            DataContext  = ViewModel = viewModel;
            InitializeComponent();
            
        }
        
        private void OnToggleChanged(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb && ViewModel?.PathingConfig != null)
            {
                var cfg = ViewModel.PathingConfig.AutoFightConfig.FinishDetectConfig;
                if (tb.IsChecked == true)
                {
                    cfg.PaimonEndModel = cfg.EndModel;
                }
                else
                {
                    cfg.PaimonEndModel = false;
                }
            }
        }
    }
}
