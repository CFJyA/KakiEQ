using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class HomeView : Page
    {
        public HomeViewModel? ViewModel => (DataContext as HomeViewModel) ?? MainViewModel.Instance?.HomeVm;

        public HomeView()
        {
            if (MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.HomeVm;
            }
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is HomeViewModel vm && DataContext != vm)
            {
                DataContext = vm;
                Bindings?.Update();
            }
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void HistoryTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.HistoryTracks));
            }
        }

        private void SuggestedTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.SuggestedTracks));
            }
        }

        private void FavoriteTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.FavoriteTracks));
            }
        }

        private void RecentTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.RecentlyAddedTracks));
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

        private void OpenKronatlBanner_Click(object sender, RoutedEventArgs e)
        {
            var season = ViewModel?.ActiveKronatlSeason;
            if (season != null)
            {
                MainWindow.Instance?.OpenKronatlCodex(season);
            }
        }
    }
}
