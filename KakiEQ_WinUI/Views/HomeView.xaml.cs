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

        private void HomeView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            if (w <= 0) return;

            if (HeroSectionGrid != null && HeroLeftCol != null && HeroRightCol != null && HeroWelcomeCard != null && HeroKronatlCard != null)
            {
                if (w < 760)
                {
                    HeroLeftCol.Width = new GridLength(1, GridUnitType.Star);
                    HeroRightCol.MaxWidth = double.PositiveInfinity;
                    HeroRightCol.Width = new GridLength(0);
                    Grid.SetColumnSpan(HeroWelcomeCard, 2);
                    Grid.SetRow(HeroKronatlCard, 1);
                    Grid.SetColumn(HeroKronatlCard, 0);
                    Grid.SetColumnSpan(HeroKronatlCard, 2);
                }
                else
                {
                    HeroLeftCol.Width = new GridLength(1.15, GridUnitType.Star);
                    HeroRightCol.MaxWidth = 430;
                    HeroRightCol.Width = new GridLength(1, GridUnitType.Star);
                    Grid.SetColumnSpan(HeroWelcomeCard, 1);
                    Grid.SetRow(HeroKronatlCard, 0);
                    Grid.SetColumn(HeroKronatlCard, 1);
                    Grid.SetColumnSpan(HeroKronatlCard, 1);
                }
            }

            if (QuickAccessGrid != null && QuickCol3 != null && QuickCol4 != null && QuickBtn3 != null && QuickBtn4 != null)
            {
                if (w < 640)
                {
                    QuickCol3.Width = new GridLength(0);
                    QuickCol4.Width = new GridLength(0);
                    Grid.SetRow(QuickBtn3, 1);
                    Grid.SetColumn(QuickBtn3, 0);
                    Grid.SetRow(QuickBtn4, 1);
                    Grid.SetColumn(QuickBtn4, 1);
                }
                else
                {
                    QuickCol3.Width = new GridLength(1, GridUnitType.Star);
                    QuickCol4.Width = new GridLength(1, GridUnitType.Star);
                    Grid.SetRow(QuickBtn3, 0);
                    Grid.SetColumn(QuickBtn3, 3);
                    Grid.SetRow(QuickBtn4, 0);
                    Grid.SetColumn(QuickBtn4, 4);
                }
            }
        }
    }
}
