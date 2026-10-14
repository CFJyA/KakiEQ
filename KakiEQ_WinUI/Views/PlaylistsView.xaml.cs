using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class PlaylistsView : UserControl
    {
        public PlaylistsViewModel? ViewModel => DataContext as PlaylistsViewModel;

        public PlaylistsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        private void Playlist_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Playlist playlist)
            {
                if (playlist.IsCreateCard)
                {
                    CreatePlaylist_Click(sender, new RoutedEventArgs());
                    return;
                }

                MainViewModel.Instance?.NavigateToPlaylist(playlist);
            }
        }

        private async void CreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
            if (xamlRoot == null) return;

            var newPl = await PlaylistDialogHelper.ShowCreatePlaylistDialogAsync(xamlRoot);
            if (newPl != null && ViewModel != null)
            {
                await ViewModel.LoadPlaylistsAsync();
            }
        }

        private async void PlayPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is Playlist playlist)
            {
                var dbService = MainViewModel.Instance?.DbService;
                if (dbService != null)
                {
                    var tracks = await dbService.GetTracksForPlaylistAsync(playlist.Name);
                    if (tracks.Count > 0 && MainViewModel.Instance != null)
                    {
                        await MainViewModel.Instance.PlayTrackAsync(tracks[0], tracks);
                    }
                }
            }
        }

        private async void EditPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is Playlist playlist)
            {
                var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
                if (xamlRoot == null) return;

                bool edited = await PlaylistDialogHelper.ShowEditPlaylistDialogAsync(xamlRoot, playlist);
                if (edited && ViewModel != null)
                {
                    await ViewModel.LoadPlaylistsAsync();
                }
            }
        }

        private async void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is Playlist playlist)
            {
                var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
                if (xamlRoot == null) return;

                var confirmDialog = new ContentDialog
                {
                    Title = "Eliminar Playlist",
                    Content = $"¿Estás seguro de que deseas eliminar la playlist \"{playlist.Name}\"?",
                    PrimaryButtonText = "Eliminar",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = xamlRoot
                };

                var res = await confirmDialog.ShowAsync();
                if (res == ContentDialogResult.Primary && ViewModel != null)
                {
                    await ViewModel.DeletePlaylistAsync(playlist);
                    MainWindow.Instance?.ShowToast($"Playlist \"{playlist.Name}\" eliminada");
                }
            }
        }
    }
}

