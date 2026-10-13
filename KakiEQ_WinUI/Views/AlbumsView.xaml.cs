using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class AlbumsView : UserControl
    {
        public AlbumsViewModel? ViewModel => DataContext as AlbumsViewModel;

        public AlbumsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
        }

        private void AlbumsGridView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            GridViewLayoutHelper.AdjustItemWidth(AlbumsGridView, minItemWidth: 170, itemHeight: 242);
        }

        private void AlbumsGridView_Loaded(object sender, RoutedEventArgs e)
        {
            GridViewLayoutHelper.AdjustItemWidth(AlbumsGridView, minItemWidth: 170, itemHeight: 242);
        }

        private void Album_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AlbumItem album)
            {
                MainViewModel.Instance?.NavigateToAlbum(album.Title);
            }
        }
    }
}

