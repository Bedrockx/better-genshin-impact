using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Controls;
using Wpf.Ui.Violeta.Controls;
using System.Windows.Input; 
using System.Linq; 
using System;
using System.Collections.Generic;

using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.View.Pages;

public partial class OneDragonFlowPage
{
    private OneDragonFlowViewModel ViewModel { get; set; }
    
    private readonly Dictionary<CheckBox, string> _checkBoxMappings;
    
    public static readonly List<string> SereniteaPotSchedule = new List<string> { "每天重复", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };

    public OneDragonFlowPage(OneDragonFlowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        _checkBoxMappings = new Dictionary<CheckBox, string>
        {
            { ClothCheckBox, "布匹" },
            { MomentResinCheckBox, "须臾树脂" },
            { SereniteapotExpBookCheckBox, "大英雄的经验" },
            { SereniteapotExpBookSmallCheckBox, "流浪者的经验" },
            { MagicmineralprecisionCheckBox, "精锻用魔矿" },
            { MOlaCheckBox, "摩拉" },
            { ExpBottleBigCheckBox, "祝圣精华" },
            { ExpBottleSmallCheckBox, "祝圣油膏" }
        };
        
    }
    
    private async void SereniteaPotTpType_Clicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedConfig == null)
        {
            return;
        }
        
        if (sender.Equals(PopupCloseButton)) Popup.IsOpen = false; //关闭弹窗
        
        else if (sender.Equals(PopupConfirmButton)) //确认选择
        {
            var selectedObjects = new List<string>(SereniteaPotComboBox.SelectedItem.ToString().Split('/'))
                .Concat(_checkBoxMappings.Where(pair => pair.Key.IsChecked == true).Select(pair => pair.Value)).ToList();

            ViewModel.SelectedConfig.SecretTreasureObjects = selectedObjects;
            Popup.IsOpen = false;
        }
        
        else if (sender.Equals(PotButton)) //初始化显示购买信息
        {
            if (!ViewModel.SelectedConfig.SecretTreasureObjects.Any())
            {
                ViewModel.SelectedConfig.SecretTreasureObjects.Add("每天重复"); 
            }
          
            SereniteaPotComboBox.SelectedItem = ViewModel.SelectedConfig.SecretTreasureObjects[0];
            
            foreach (var pair in _checkBoxMappings)
            {
                pair.Key.IsChecked = ViewModel.SelectedConfig.SecretTreasureObjects.Contains(pair.Value);
            }
        }
        
    }
    
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

    private bool IsValidBindingCode(string bindingCode)
    {
        return !string.IsNullOrEmpty(bindingCode) && bindingCode.Length == 2 &&
               char.IsLetterOrDigit(bindingCode[0]) && char.IsLetterOrDigit(bindingCode[1]);
    }

    private void GenshinUid_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        if (textBox != null)
        {
            var uid = textBox.Text;
            if (uid.Length == 9 && string.IsNullOrWhiteSpace(ViewModel.SelectedConfig.AccountBindingCode))
            {
                var bindingCode = PromptDialog.Prompt(
                    $"请输入 {ViewModel.SelectedConfig?.Name} / UID {uid} 的绑定码：\n手机登录的账户：切换账号列表显示的后2位 " +
                    $"\n邮箱登录的账户：切换账号列表显示的前2位 ", "UID辅助切换绑定码设置", new Size(400, 250), "如不设置绑定码，则使用轮切方式切换账号");
                if (IsValidBindingCode(bindingCode) && ViewModel.SelectedConfig != null)
                {
                    ViewModel.SelectedConfig.AccountBindingCode = bindingCode;
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(textBox), null);
                    Toast.Success($"UID: {uid} 已经绑定 {ViewModel.SelectedConfig.AccountBindingCode}");
                }
                else
                {
                    var textBox2 = sender as TextBox;
                    var someOtherControl = FindParent<FrameworkElement>(textBox2);
                    if (string.IsNullOrEmpty(bindingCode) || bindingCode == "如不设置绑定码，则使用轮切方式切换账号")
                    {
                        ViewModel.SelectedConfig.AccountBindingCode = string.Empty;
                        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(textBox), someOtherControl);
                        Toast.Warning("绑定码为空，将使用轮切方式切换账号");
                        return;
                    }

                    Toast.Warning("绑定码长度为 2 位，且只能包含字母或数字");
                    GenshinUid_TextChanged(sender, e);
                }
            }
            else if (string.IsNullOrWhiteSpace(uid) || uid.Length != 9 || !int.TryParse(uid, out _))
            {
                Toast.Warning("无效UID，长度 " + uid.Length + " 位，请输入 9 位纯数字的UID");
                if (uid.Length == 0) ViewModel.SelectedConfig.AccountBindingCode = String.Empty;
            } 
        }
    }

    private void AccountBinding_Checked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        if (checkBox != null)
        {
            if (checkBox.IsChecked == true && ViewModel.SelectedConfig != null)
            {
                if (!string.IsNullOrEmpty(ViewModel.SelectedConfig.AccountBindingCode))
                {
                    Toast.Success(
                        $"UID: {ViewModel.SelectedConfig.GenshinUid} 绑定码 {ViewModel.SelectedConfig.AccountBindingCode}");
                    checkBox.IsChecked = true;
                }
                else
                {
                    var bindingCode = PromptDialog.Prompt(
                        $"请输入 {ViewModel.SelectedConfig?.Name} 的绑定码：\n手机登录的账户：切换账号列表显示的后2位 " +
                        $"\n邮箱登录的账户：切换账号列表显示的前2位 ", "UID辅助切换绑定码设置", new Size(400, 250), "如不设置绑定码，则使用轮切方式切换账号");
                    if (string.IsNullOrEmpty(bindingCode) || bindingCode == "如不设置绑定码，则使用轮切方式切换账号")
                    {
                        ViewModel.SelectedConfig.AccountBindingCode = string.Empty;
                        Toast.Warning("绑定码为空，将使用轮切方式切换账号");
                        return;
                    }

                    if (IsValidBindingCode(bindingCode) && ViewModel.SelectedConfig != null)
                    {
                        ViewModel.SelectedConfig.AccountBindingCode = bindingCode;
                        Toast.Success(
                            $"UID: {ViewModel.SelectedConfig.GenshinUid} 绑定码 {ViewModel.SelectedConfig.AccountBindingCode}");
                        checkBox.IsChecked = true;
                    }
                    else
                    {
                        Toast.Warning("绑定码长度为 2 位，且只能包含字母或数字");
                        checkBox.IsChecked = false;
                    }
                }
            }
        }
    }

    private void AddButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ReadScriptGroup();
        var list = ViewModel?.ScriptGroups.Select(x => x.Name).ToList() ?? new List<string>();
        var stackPanel = new StackPanel();
        var comboBox = new ComboBox { ItemsSource = list, SelectedItem = list?[0] };
        stackPanel.Children.Add(comboBox);
        
        var newTaskWindow = PromptDialog.Prompt("请选择自定义任务名称（配置组名称）：", "创建自定义任务",stackPanel, new Size(400, 200));
        if (!string.IsNullOrEmpty(newTaskWindow) && !string.IsNullOrEmpty(comboBox.SelectedItem?.ToString()))
        {
            if (ViewModel.SelectedConfig?.CustomDomainList.Contains(comboBox.SelectedItem?.ToString()) == true)
            {
                Toast.Warning("已经存在同名的自定义任务");
                return;
            }
    
            ViewModel.SelectedConfig?.CustomDomainList.AddRange(new[] { comboBox.SelectedItem?.ToString() });
            ViewModel.SaveConfig();
            ViewModel.InitializeDomainNameList();
            RefreshAllDomainSelectors();
            Toast.Success("已创建自定义秘境任务：" + comboBox.SelectedItem?.ToString());
        }
    }
    
    private void ReduceButtonClick(object sender, RoutedEventArgs e)
    {
        var list = ViewModel?.SelectedConfig?.CustomDomainList ?? new List<string>();
        if (list.Count == 0)
        {
            Toast.Warning("没有可删除的自定义秘境任务");
            return;
        }
    
        var stackPanel = new StackPanel();
        var comboBox1 = new ComboBox { ItemsSource = list, SelectedItem = list?[0] };
        stackPanel.Children.Add(comboBox1);
    
        var result = PromptDialog.Prompt("请选择要删除的自定义任务名称？", "删除自定义任务", stackPanel, new Size(400, 200), null);
        if (comboBox1.SelectedItem != null && !string.IsNullOrEmpty(result) )
        {
            string taskToDelete = comboBox1.SelectedItem.ToString();
            ViewModel?.SelectedConfig?.CustomDomainList.Remove(taskToDelete);
            ViewModel?.SaveConfig();
            ViewModel?.InitializeDomainNameList();
            RefreshAllDomainSelectors();
            Toast.Success("已删除自定义秘境任务：" + taskToDelete);
        }
    }

    /// <summary>
    /// 刷新页面上所有 DomainSelector 的 CustomDomains 属性，
    /// 通过创建新列表实例强制触发 DependencyProperty 变更通知
    /// </summary>
    private void RefreshAllDomainSelectors()
    {
        if (ViewModel?.SelectedConfig == null) return;
        var newList = new List<string>(ViewModel.SelectedConfig.CustomDomainList);
        foreach (var selector in FindVisualChildren<DomainSelector>(this))
        {
            selector.CustomDomains = newList;
        }
    }

    /// <summary>
    /// 在视觉树中递归查找指定类型的所有子元素
    /// </summary>
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var grandChild in FindVisualChildren<T>(child))
                yield return grandChild;
        }
    }
    
    private void ConfigComboBox_Clicked(object sender, MouseButtonEventArgs e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox != null)
        {
            comboBox.Items.Refresh();
        }
    }
    
   private void ConfigComboBox_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox != null)
        {
            ViewModel.ReadScriptGroup();
            var selectedItem = comboBox.SelectedItem?.ToString();
            if (selectedItem != null && ViewModel.SelectedConfig != null)
            {
                if (ViewModel.SelectedConfig.CustomDomainList.Any(c => c.Equals(selectedItem)))
                {
                    ViewModel.ScriptControlPageAsync(ViewModel?.ScriptGroups?.FirstOrDefault(x => x.Name == selectedItem));
                }
                
            }
        }
    }

    private void DomainSelector_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var domainSelector = sender as DomainSelector;
        if (domainSelector != null)
        {
            var selectedItem = domainSelector.SelectedDomain;
            if (selectedItem != null && ViewModel.SelectedConfig != null)
            {
                ViewModel.ReadScriptGroup();
                if (ViewModel.SelectedConfig.CustomDomainList.Any(c => c.Equals(selectedItem)))
                {
                    ViewModel.ScriptControlPageAsync(ViewModel?.ScriptGroups?.FirstOrDefault(x => x.Name == selectedItem));
                }
            }
        }
    }
   
   private async void TextBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
   {
       var stackPanel = new StackPanel
       {
           Orientation = Orientation.Vertical,
           VerticalAlignment = VerticalAlignment.Center,
           HorizontalAlignment = HorizontalAlignment.Center
       };
       var checkBoxes = new Dictionary<string, CheckBox>(); 
       CheckBox selectedCheckBox = null;
       
       var grid = new Grid
       {
           ColumnDefinitions = 
           { 
               new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
               new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
               new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
               new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
           },
           RowDefinitions = 
           { 
               new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
               new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
           }
       };

       int columnIndex = 0;
       int rowIndex = 0;

       foreach (var day in ViewModel.SelectedConfig.PeriodList.Take(8))
       {
           var checkBox = new CheckBox
           {
               Content = day.Key,
               Tag = day.Key,
               MinWidth = 100,
               VerticalAlignment = VerticalAlignment.Center,
               HorizontalAlignment = HorizontalAlignment.Center,
               IsChecked = ViewModel.SelectedConfig.PeriodList[day.Key]
           };
           checkBoxes[day.Key] = checkBox;
           Grid.SetRow(checkBox, rowIndex);
           Grid.SetColumn(checkBox, columnIndex);
           grid.Children.Add(checkBox);

           columnIndex++;
           if (columnIndex >= 4)
           {
               columnIndex = 0;
               rowIndex++;
           }
       }
       
       stackPanel.Children.Add(grid);
       var uiMessageBox = new Wpf.Ui.Controls.MessageBox
       {
           Title = "配置单周期配置",
           Content = new ScrollViewer
           {
               Content = stackPanel,
               VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
           },
           CloseButtonText = "关闭",
           PrimaryButtonText = "确认",
           Owner = Application.Current.ShutdownMode == ShutdownMode.OnMainWindowClose ? null : Application.Current.MainWindow,
           WindowStartupLocation = WindowStartupLocation.CenterOwner,
           SizeToContent = SizeToContent.Width,
           MaxHeight = 200,
       };
       
       var result = await uiMessageBox.ShowDialogAsync();
       if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
       {
           if (checkBoxes.Values.All(x => x.IsChecked == false))
           {
               Toast.Warning("至少选择一个周期");
               return;
           }
           
           foreach (var day in checkBoxes)
           {
               if (day.Key == "每日" && day.Value.IsChecked == true)
               {
                   ViewModel.SelectedConfig.PeriodList[day.Key] = true;
                   foreach (var otherDay in checkBoxes)
                   {
                       if (otherDay.Key!= "每日")
                       {
                           ViewModel.SelectedConfig.PeriodList[otherDay.Key] = false;
                       }
                   }
                   break;
               }

               if (day.Value.IsChecked == true)
               {
                   ViewModel.SelectedConfig.PeriodList[day.Key] = true;
               }
               else
               {
                   ViewModel.SelectedConfig.PeriodList[day.Key] = false;
               } 
              
           }
          
           ViewModel.SaveConfig();
           Toast.Success("配置单执行周期配置已保存");
           ViewModel.RefreshFilteredConfigList();
       }
   }
   
   private void ScheduleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
   {
       ViewModel.RefreshFilteredConfigList(); 
   }

}