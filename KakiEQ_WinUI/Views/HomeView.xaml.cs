using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class HomeView : UserControl
    {
        public HomeViewModel? ViewModel => DataContext as HomeViewModel;

        public HomeView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        private void SuggestedTrack_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as HomeViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new System.Collections.Generic.List<Track>(vm.SuggestedTracks));
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

        private void NavigateEqualizer_Click(object sender, RoutedEventArgs e)
        {
            MainViewModel.Instance?.NavigateEqualizerCommand.Execute(null);
        }
    }
}

