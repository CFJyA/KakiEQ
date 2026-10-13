using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class PlaylistDetailView : UserControl
    {
        public PlaylistDetailViewModel? ViewModel => DataContext as PlaylistDetailViewModel;

        public PlaylistDetailView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        private void Track_ItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (e.ClickedItem is Track track && vm != null)
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(track, new List<Track>(vm.Tracks));
            }
        }

        private void RemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            var vm = ViewModel ?? DataContext as PlaylistDetailViewModel;
            if (sender is Button btn && btn.Tag is Track track && vm != null)
            {
                vm.RemoveTrackCommand.Execute(track);
            }
        }
    }
}

