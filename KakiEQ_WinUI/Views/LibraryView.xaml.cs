using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class LibraryView : UserControl
    {
        public LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

        public LibraryView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        private void Song_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Track track && DataContext is LibraryViewModel vm)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.FilteredTracks));
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && ((btn.Tag as Track) ?? (btn.DataContext as Track)) is Track track)
            {
                TrackContextMenuHelper.ShowAtTarget(track, btn);
            }
        }

        private void TrackRow_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement row && ((row.Tag as Track) ?? (row.DataContext as Track)) is Track track)
            {
                TrackContextMenuHelper.ShowAtTarget(track, row, e.GetPosition(row));
            }
        }
    }
}

