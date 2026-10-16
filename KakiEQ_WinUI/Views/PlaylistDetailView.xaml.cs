using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class PlaylistDetailView : UserControl
    {
        public PlaylistDetailViewModel? ViewModel => DataContext as PlaylistDetailViewModel;
        private INotifyCollectionChanged? _observedTracks;

        public PlaylistDetailView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
                HookTracksCollection();
            };
            Loaded += (s, e) => HookTracksCollection();
        }

        private void HookTracksCollection()
        {
            if (_observedTracks != null)
            {
                _observedTracks.CollectionChanged -= Tracks_CollectionChanged;
                _observedTracks = null;
            }

            if (ViewModel?.Tracks is INotifyCollectionChanged observable)
            {
                _observedTracks = observable;
                _observedTracks.CollectionChanged += Tracks_CollectionChanged;
            }
        }

        private void Tracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                _ = ViewModel?.ReorderTracksAsync();
            }
        }

        private void TracksListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            _ = ViewModel?.ReorderTracksAsync();
        }

        private void Track_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.Tracks));
            }
        }

        private async void EditPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (vm != null)
            {
                var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
                if (xamlRoot == null) return;

                bool edited = await PlaylistDialogHelper.ShowEditPlaylistDialogAsync(xamlRoot, vm.CurrentPlaylist);
                if (edited)
                {
                    await vm.RefreshDetailsAsync();
                }
            }
        }

        private async void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (vm != null)
            {
                var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
                if (xamlRoot == null)
                {
                    vm.DeletePlaylistCommand.Execute(null);
                    return;
                }

                var confirmDialog = new ContentDialog
                {
                    Title = "Eliminar playlist",
                    Content = $"¿Estás seguro de que deseas eliminar la playlist \"{vm.CurrentPlaylist.Name}\"? Esta acción no se puede deshacer.",
                    PrimaryButtonText = "Eliminar",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = xamlRoot
                };

                var result = await confirmDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    vm.DeletePlaylistCommand.Execute(null);
                }
            }
        }

        private void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (sender is FrameworkElement btn && ((btn.Tag as Track) ?? (btn.DataContext as Track)) is Track track)
            {
                TrackContextMenuHelper.ShowAtTarget(
                    track,
                    btn,
                    null,
                    onRemoveFromPlaylist: () => vm?.RemoveTrackCommand.Execute(track));
            }
        }

        private void TrackRow_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (sender is FrameworkElement row && ((row.Tag as Track) ?? (row.DataContext as Track)) is Track track)
            {
                TrackContextMenuHelper.ShowAtTarget(
                    track,
                    row,
                    e.GetPosition(row),
                    onRemoveFromPlaylist: () => vm?.RemoveTrackCommand.Execute(track));
            }
        }
    }
}
