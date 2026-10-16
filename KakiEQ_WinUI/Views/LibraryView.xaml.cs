using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            if (sender is Button btn && btn.Tag is Track track)
            {
                var flyout = new MenuFlyout();

                // "Me gusta" toggle
                var likeItem = new MenuFlyoutItem
                {
                    Text = "Añadir / Quitar de Favoritos (Yollotl)",
                    Icon = new FontIcon { Glyph = "\uEB51" }
                };
                likeItem.Click += (s, args) =>
                {
                    MainViewModel.Instance?.LikeTrackCommand.Execute(track);
                };
                flyout.Items.Add(likeItem);

                // Separator
                flyout.Items.Add(new MenuFlyoutSeparator());

                // Play next
                var playNextItem = new MenuFlyoutItem
                {
                    Text = "Reproducir a continuación",
                    Icon = new FontIcon { Glyph = "\uE895" }
                };
                playNextItem.Click += (s, args) =>
                {
                    MainViewModel.Instance?.AddTrackNext(track);
                };
                flyout.Items.Add(playNextItem);

                // Add to queue end
                var addToQueueItem = new MenuFlyoutItem
                {
                    Text = "Añadir al final de la cola",
                    Icon = new FontIcon { Glyph = "\uE8F1" }
                };
                addToQueueItem.Click += (s, args) =>
                {
                    MainViewModel.Instance?.AddTrackToQueue(track);
                };
                flyout.Items.Add(addToQueueItem);

                // Add to playlist submenu
                var playlistSubItem = new MenuFlyoutSubItem
                {
                    Text = "Añadir a Playlist...",
                    Icon = new FontIcon { Glyph = "\uE93C" }
                };

                if (MainViewModel.Instance?.GlobalPlaylists != null)
                {
                    foreach (var pl in MainViewModel.Instance.GlobalPlaylists)
                    {
                        var plItem = new MenuFlyoutItem { Text = pl.Name };
                        plItem.Click += async (s, args) =>
                        {
                            await MainViewModel.Instance.DbService.AddTrackToPlaylistAsync(pl.Name, track);
                            await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
                        };
                        playlistSubItem.Items.Add(plItem);
                    }
                }
                flyout.Items.Add(playlistSubItem);

                // Separator
                flyout.Items.Add(new MenuFlyoutSeparator());

                // Ver / Editar Metadatos
                var editMetadataItem = new MenuFlyoutItem
                {
                    Text = "Ver / Editar Metadatos",
                    Icon = new FontIcon { Glyph = "\uE70F" }
                };
                editMetadataItem.Click += (s, args) =>
                {
                    MainWindow.Instance?.OpenMetadataWindow(track);
                };
                flyout.Items.Add(editMetadataItem);

                flyout.ShowAt(btn);
            }
        }
    }
}

