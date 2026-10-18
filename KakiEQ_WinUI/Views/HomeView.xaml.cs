using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class HomeView : UserControl
    {
        public HomeViewModel? ViewModel => DataContext as HomeViewModel;

        private bool? _isMaximizedLayout = null;

        public HomeView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
            Loaded += (s, e) =>
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
            double targetWidth = isMax ? 180 : 171;
            double targetHeight = isMax ? 230 : 224;

            UpdateWrapGrid(HistoryGridView, targetWidth, targetHeight);
            UpdateWrapGrid(SuggestedGridView, targetWidth, targetHeight);
            UpdateWrapGrid(FavoriteGridView, targetWidth, targetHeight);
        }

        private static void UpdateWrapGrid(GridView? gridView, double targetWidth, double targetHeight)
        {
            var wrapGrid = GetWrapGrid(gridView);
            if (wrapGrid != null && (wrapGrid.ItemWidth != targetWidth || wrapGrid.ItemHeight != targetHeight))
            {
                wrapGrid.ItemWidth = targetWidth;
                wrapGrid.ItemHeight = targetHeight;
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

        private void HistoryTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new System.Collections.Generic.List<Track>(vm.HistoryTracks));
            }
        }

        private void SuggestedTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new System.Collections.Generic.List<Track>(vm.SuggestedTracks));
            }
        }

        private void FavoriteTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new System.Collections.Generic.List<Track>(vm.FavoriteTracks));
            }
        }

        private void RecentTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new System.Collections.Generic.List<Track>(vm.RecentlyAddedTracks));
            }
        }

        private void NavigateSongs_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.Instance?.NavigateSongsCommand.Execute(null);
        }

        private void NavigateAlbums_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.Instance?.NavigateAlbumsCommand.Execute(null);
        }

        private void NavigateArtists_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.Instance?.NavigateArtistsCommand.Execute(null);
        }

        private void NavigatePlaylists_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.Instance?.NavigatePlaylistsCommand.Execute(null);
        }

        private void NavigateEqualizer_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.Instance?.NavigateEqualizerCommand.Execute(null);
        }

    }
}

