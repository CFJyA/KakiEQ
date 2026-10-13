using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class ArtistsView : UserControl
    {
        public ArtistsViewModel? ViewModel => DataContext as ArtistsViewModel;

        public ArtistsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        private void ArtistsGridView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            GridViewLayoutHelper.AdjustItemWidth(ArtistsGridView, minItemWidth: 170, itemHeight: 222);
        }

        private void ArtistsGridView_Loaded(object sender, RoutedEventArgs e)
        {
            GridViewLayoutHelper.AdjustItemWidth(ArtistsGridView, minItemWidth: 170, itemHeight: 222);
        }

        private void Artist_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ArtistItem artist)
            {
                MainViewModel.Instance?.NavigateToArtist(artist.Name);
            }
        }
    }
}

