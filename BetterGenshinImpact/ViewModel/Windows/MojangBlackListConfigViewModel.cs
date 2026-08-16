using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class MojangItemViewModel : ObservableObject
{
    public required string Name { get; init; }

    public required string Color { get; init; }

    public int ColorIndex { get; init; }

    [ObservableProperty]
    private bool _isBlackListed;
}

public partial class MojangBlackListConfigViewModel : AutoPickConfigWindowViewModelBase
{
    /// <summary>颜色分组展示顺序，与莫版模板颜色一致</summary>
    private static readonly string[] ColorOrder = ["灰", "绿", "蓝", "紫", "白"];

    public string[] StatusFilters { get; } = ["全部", "已勾选", "未勾选"];

    public string[] ColorFilters { get; } = ["全部", "灰", "绿", "蓝", "紫", "白"];

    public ObservableCollection<MojangItemViewModel> Items { get; } = [];

    public ICollectionView ItemsView { get; }

    /// <summary>批量勾选/取消期间挂起视图刷新，避免逐项触发 ItemsView.Refresh。</summary>
    private bool _suppressRefresh;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusFilter = "全部";

    [ObservableProperty]
    private string _colorFilter = "全部";

    public MojangBlackListConfigViewModel()
    {
        var blackList = MojangPickFilter.Load();
        // 模板加载的 CPU 工作放在后台线程执行，避免在 UI 线程同步加载导致窗口卡顿
        // （正常流程启动时已后台预加载完成；此处兜底，未加载时仅在后台等待加载完成）
        var templateInfos = MojangMatch.IsLoaded
            ? MojangMatch.Instance.GetTemplateInfos()
            : Task.Run(() => MojangMatch.Instance.GetTemplateInfos()).GetAwaiter().GetResult();
        foreach (var info in templateInfos)
        {
            var item = new MojangItemViewModel
            {
                Name = info.Name,
                Color = info.Color,
                ColorIndex = System.Array.IndexOf(ColorOrder, info.Color),
                IsBlackListed = blackList.Contains(info.Name),
            };
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }

        ItemsView = new ListCollectionView(Items)
        {
            Filter = FilterItem,
            CustomSort = new MojangItemComparer(),
        };
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MojangItemViewModel.Color)));
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_suppressRefresh && e.PropertyName == nameof(MojangItemViewModel.IsBlackListed))
        {
            ItemsView.Refresh();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ItemsView.Refresh();
    }

    partial void OnStatusFilterChanged(string value)
    {
        ItemsView.Refresh();
    }

    partial void OnColorFilterChanged(string value)
    {
        ItemsView.Refresh();
    }

    private bool FilterItem(object item)
    {
        if (item is not MojangItemViewModel m)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText)
            && !m.Name.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (StatusFilter == "已勾选" && !m.IsBlackListed)
        {
            return false;
        }

        if (StatusFilter == "未勾选" && m.IsBlackListed)
        {
            return false;
        }

        if (ColorFilter != "全部" && m.Color != ColorFilter)
        {
            return false;
        }

        return true;
    }

    [RelayCommand]
    private void SelectAll()
    {
        _suppressRefresh = true;
        try
        {
            foreach (var item in ItemsView.Cast<MojangItemViewModel>().ToList())
            {
                item.IsBlackListed = true;
            }
        }
        finally
        {
            _suppressRefresh = false;
        }

        ItemsView.Refresh();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _suppressRefresh = true;
        try
        {
            foreach (var item in ItemsView.Cast<MojangItemViewModel>().ToList())
            {
                item.IsBlackListed = false;
            }
        }
        finally
        {
            _suppressRefresh = false;
        }

        ItemsView.Refresh();
    }

    [RelayCommand]
    private void Save()
    {
        SaveAndClose(() =>
        {
            MojangPickFilter.Save(Items.Where(i => i.IsBlackListed).Select(i => i.Name));
        });
    }

    private sealed class MojangItemComparer : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (MojangItemViewModel)x!;
            var b = (MojangItemViewModel)y!;
            var c = a.ColorIndex.CompareTo(b.ColorIndex);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        }
    }
}
