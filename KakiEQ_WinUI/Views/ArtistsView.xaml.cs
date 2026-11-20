using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class ArtistsView : Page
    {
        public ArtistsViewModel? ViewModel => (DataContext as ArtistsViewModel) ?? MainViewModel.Instance?.ArtistsVm;

        public ArtistsView()
        {
            if (MainViewModel.Instance != null)
            {
                DataContext = MainViewModel.Instance.ArtistsVm;
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
            if (e.Parameter is ArtistsViewModel vm && DataContext != vm)
            {
                DataContext = vm;
                Bindings?.Update();
            }
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void Artist_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ArtistItem artist)
            {
                MainViewModel.Instance?.NavigateToArtist(artist.Name);
            }
        }

        private void ArtistsHeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ArtistsSearchCountPanel == null) return;
            if (e.NewSize.Width < 680)
            {
                Grid.SetRow(ArtistsSearchCountPanel, 1);
                Grid.SetColumn(ArtistsSearchCountPanel, 0);
                Grid.SetColumnSpan(ArtistsSearchCountPanel, 2);
                ArtistsSearchCountPanel.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                Grid.SetRow(ArtistsSearchCountPanel, 0);
                Grid.SetColumn(ArtistsSearchCountPanel, 1);
                Grid.SetColumnSpan(ArtistsSearchCountPanel, 1);
                ArtistsSearchCountPanel.HorizontalAlignment = HorizontalAlignment.Right;
            }
        }
    }
}
