using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class AlbumsView : Page
    {
        public AlbumsViewModel? ViewModel => (DataContext as AlbumsViewModel) ?? MainViewModel.Instance?.AlbumsVm;

        public AlbumsView()
        {
            if (MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.AlbumsVm;
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
            if (e.Parameter is AlbumsViewModel vm && DataContext != vm)
            {
                DataContext = vm;
                Bindings?.Update();
            }
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void Album_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AlbumItem album)
            {
                MainViewModel.Instance?.NavigateToAlbum(album.Title, album.Artist);
            }
        }
    }
}
