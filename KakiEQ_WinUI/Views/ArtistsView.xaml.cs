using Microsoft.UI.Xaml.Controls;
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

        private void Artist_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ArtistItem artist)
            {
                MainViewModel.Instance?.NavigateToArtist(artist.Name);
            }
        }
    }
}

