using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Helpers
{
    public static class TrackContextMenuHelper
    {
        public static MenuFlyout CreateTrackFlyout(Track track, Action? onRemoveFromPlaylist = null)
        {
            var flyout = new MenuFlyout();

            // 1. Favoritos (Yollotl)
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

            // 2. Play next
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

            // 3. Add to queue end
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

            // 4. Add to playlist submenu
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

            // 5. (Exclusive to PlaylistDetailView) Eliminar de Playlist
            if (onRemoveFromPlaylist != null)
            {
                var removePlaylistItem = new MenuFlyoutItem
                {
                    Text = "Eliminar de Playlist",
                    Icon = new FontIcon { Glyph = "\uE74D" }
                };
                removePlaylistItem.Click += (s, args) =>
                {
                    onRemoveFromPlaylist();
                };
                flyout.Items.Add(removePlaylistItem);
            }

            // Separator
            flyout.Items.Add(new MenuFlyoutSeparator());

            // 6. Ver / Editar Metadatos
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

            // 7. Preparar Karaoke con IA
            var karaokeItem = new MenuFlyoutItem
            {
                Text = "Preparar Modo Karaoke (Separar con IA)",
                Icon = new FontIcon { Glyph = "\uE720" }
            };
            karaokeItem.Click += (s, args) =>
            {
                _ = MainViewModel.Instance?.PrepareKaraokeWithAiAsync(track);
            };
            flyout.Items.Add(karaokeItem);

            return flyout;
        }

        public static void ShowAtTarget(Track track, FrameworkElement target, Point? position = null, Action? onRemoveFromPlaylist = null)
        {
            var flyout = CreateTrackFlyout(track, onRemoveFromPlaylist);
            if (position.HasValue)
            {
                flyout.ShowAt(target, new FlyoutShowOptions { Position = position.Value });
            }
            else
            {
                flyout.ShowAt(target);
            }
        }
    }
}
