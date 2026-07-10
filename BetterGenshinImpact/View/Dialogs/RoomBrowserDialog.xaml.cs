using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BetterGenshinImpact.GameTask.AutoHoeing.Multiplayer.Models;

namespace BetterGenshinImpact.View.Dialogs;

public partial class RoomBrowserDialog
{
    private readonly Func<Task<List<RoomSummary>>> _refreshCallback;

    public string? SelectedRoomCode { get; private set; }

    public RoomBrowserDialog(List<RoomSummary> rooms, Func<Task<List<RoomSummary>>> refreshCallback)
    {
        _refreshCallback = refreshCallback;
        InitializeComponent();
        UpdateRoomList(rooms);
    }

    private void UpdateRoomList(List<RoomSummary> rooms)
    {
        RoomListView.ItemsSource = rooms;
        EmptyText.Visibility = rooms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RoomListView.Visibility = rooms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        var rooms = await _refreshCallback();
        UpdateRoomList(rooms);
    }

    private void OnJoinClick(object sender, RoutedEventArgs e)
    {
        if (RoomListView.SelectedItem is RoomSummary room)
        {
            SelectedRoomCode = room.Code;
            DialogResult = true;
            Close();
        }
    }

    private void OnRoomDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RoomListView.SelectedItem is RoomSummary room)
        {
            SelectedRoomCode = room.Code;
            DialogResult = true;
            Close();
        }
    }
}
