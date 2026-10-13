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
                    Text = "Reproducir siguiente",
                    Icon = new FontIcon { Glyph = "\uE895" }
                };
                playNextItem.Click += (s, args) =>
                {
                    if (MainViewModel.Instance?.CurrentQueue != null)
                    {
                        int insertIndex = MainViewModel.Instance.CurrentQueueIndex + 1;
                        if (insertIndex >= 0 && insertIndex <= MainViewModel.Instance.CurrentQueue.Count)
                        {
                            MainViewModel.Instance.CurrentQueue.Insert(insertIndex, track);
                        }
                    }
                };
                flyout.Items.Add(playNextItem);

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

                flyout.ShowAt(btn);
            }
        }
    }
}

