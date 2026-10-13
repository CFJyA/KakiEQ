using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            var vm = ViewModel ?? DataContext as PlaylistsViewModel;
            if (vm != null)
            {
                int realCount = vm.Playlists.Count(p => !p.IsCreateCard);
                var inputTextBox = new TextBox
                {
                    PlaceholderText = "Nombre de la playlist",
                    Text = $"Playlist {realCount + 1}"
                };

                var xamlRoot = this.XamlRoot ?? MainWindow.Instance?.Content?.XamlRoot;
                if (xamlRoot == null) return;

                var dialog = new ContentDialog
                {
                    Title = "Nueva Playlist",
                    Content = inputTextBox,
                    PrimaryButtonText = "Crear",
                    CloseButtonText = "Cancelar",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = xamlRoot
                };

                try
                {
                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
                    {
                        await vm.CreatePlaylistAsync(inputTextBox.Text.Trim());
                    }
                }
                catch
                {
                    // Ignore ContentDialog concurrency errors
                }
            }
        }
    }
}

