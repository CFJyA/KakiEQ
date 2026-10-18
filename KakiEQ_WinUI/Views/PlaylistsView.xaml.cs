using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class PlaylistsView : UserControl
    {
        public PlaylistsViewModel? ViewModel => DataContext as PlaylistsViewModel;
        private bool? _isMaximizedLayout = null;

        public PlaylistsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
            PlaylistsGridView.Loaded += (s, e) =>
            {
                ApplyItemDimensions(CheckIsMaximized(ActualWidth));
            };
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGridItemSize(ActualWidth);
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridItemSize(e.NewSize.Width);
        }

        private bool CheckIsMaximized(double width)
        {
            return (MainWindow.Instance?.IsMaximized == true) || width > 1250;
        }

        private void UpdateGridItemSize(double width)
        {
            if (width <= 0) return;
            bool isMax = CheckIsMaximized(width);
            if (_isMaximizedLayout != isMax)
            {
                _isMaximizedLayout = isMax;
                ApplyItemDimensions(isMax);
            }
        }

        private void ApplyItemDimensions(bool isMax)
        {
            var wrapGrid = GetWrapGrid(PlaylistsGridView);
            if (wrapGrid != null)
            {
                double targetWidth = isMax ? 180 : 171;
                double targetHeight = isMax ? 240 : 232;
                if (wrapGrid.ItemWidth != targetWidth || wrapGrid.ItemHeight != targetHeight)
                {
                    wrapGrid.ItemWidth = targetWidth;
                    wrapGrid.ItemHeight = targetHeight;
                }
            }
        }

        private static ItemsWrapGrid? GetWrapGrid(GridView? gridView)
        {
            if (gridView == null) return null;
            if (gridView.ItemsPanelRoot is ItemsWrapGrid panel) return panel;
            return FindChild<ItemsWrapGrid>(gridView);
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
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

