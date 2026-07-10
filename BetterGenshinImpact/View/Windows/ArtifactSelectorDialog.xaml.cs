using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 构建版本选择弹窗，展示最多 3 个 GitHub Actions 构建产物供用户选择
/// </summary>
public partial class ArtifactSelectorDialog : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>
    /// 用户选择的构建产物，未选择时为 null
    /// </summary>
    public BuildArtifactInfo? SelectedArtifact { get; private set; }

    public ArtifactSelectorDialog(List<BuildArtifactInfo> artifacts)
    {
        InitializeComponent();
        ArtifactListBox.ItemsSource = artifacts;

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void ArtifactListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArtifactListBox.SelectedItem is BuildArtifactInfo artifact)
        {
            SelectedArtifact = artifact;
            DialogResult = true;
            Close();
        }
    }
}
