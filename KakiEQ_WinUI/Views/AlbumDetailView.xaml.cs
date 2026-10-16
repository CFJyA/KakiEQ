using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class AlbumDetailView : UserControl
    {
        public AlbumDetailView()
        {
            InitializeComponent();
        }

        private void Track_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Track track && DataContext is AlbumDetailViewModel vm)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.Tracks));
            }
        }

        private void MoreAlbum_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AlbumItem album)
            {
                MainViewModel.Instance?.NavigateToAlbum(album.Title);
            }
        }

        private void AddAlbumToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AlbumDetailViewModel vm && vm.Tracks.Any() && sender is FrameworkElement anchor)
            {
                PlaylistDialogHelper.ShowAddTracksToPlaylistFlyout(
                    anchor,
                    vm.Tracks,
                    vm.AlbumTitle,
                    this.XamlRoot);
            }
        }

        private void AddSingleTrackToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement anchor && anchor.Tag is Track track)
            {
                var root = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
                if (root != null)
                {
                    PlaylistDialogHelper.ShowAddToPlaylistFlyout(anchor, track, root);
                }
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

