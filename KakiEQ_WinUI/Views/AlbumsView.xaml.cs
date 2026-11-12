using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
                UpdateFilterButtonsVisuals();
            };
            Loaded += (s, e) => UpdateFilterButtonsVisuals();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is AlbumsViewModel vm && DataContext != vm)
            {
                DataContext = vm;
                Bindings?.Update();
            }
            UpdateFilterButtonsVisuals();
            FluidAnimationHelper.PlayPageEntranceAnimation(this);
        }

        private void Album_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AlbumItem album)
            {
                MainViewModel.Instance?.NavigateToAlbum(album.Title, album.Artist);
            }
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.FilterAllCommand.Execute(null);
            UpdateFilterButtonsVisuals();
        }

        private void BtnFilterSingle_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.FilterSingleCommand.Execute(null);
            UpdateFilterButtonsVisuals();
        }

        private void BtnFilterEp_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.FilterEpCommand.Execute(null);
            UpdateFilterButtonsVisuals();
        }

        private void BtnFilterAlbum_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.FilterAlbumCommand.Execute(null);
            UpdateFilterButtonsVisuals();
        }

        private void UpdateFilterButtonsVisuals()
        {
            if (ViewModel == null) return;

            ApplyFilterButtonStyle(BtnFilterAll, ViewModel.IsFilterAll);
            ApplyFilterButtonStyle(BtnFilterSingle, ViewModel.IsFilterSingle);
            ApplyFilterButtonStyle(BtnFilterEp, ViewModel.IsFilterEp);
            ApplyFilterButtonStyle(BtnFilterAlbum, ViewModel.IsFilterAlbum);
        }

        private void ApplyFilterButtonStyle(Button? btn, bool isActive)
        {
            if (btn == null) return;

            if (isActive)
            {
                if (Application.Current.Resources.TryGetValue("AppAccent", out object? accent) && accent is Brush accentBrush)
                {
                    btn.Background = accentBrush;
                    btn.BorderBrush = accentBrush;
                }
                else
                {
                    var fallback = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252));
                    btn.Background = fallback;
                    btn.BorderBrush = fallback;
                }

                var darkBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 18));
                if (btn.Content is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock tb) tb.Foreground = darkBrush;
                        if (child is FontIcon fi) fi.Foreground = darkBrush;
                    }
                }
            }
            else
            {
                if (Application.Current.Resources.TryGetValue("AppSurfaceElevated", out object? elevated) && elevated is Brush elevatedBrush)
                    btn.Background = elevatedBrush;

                if (Application.Current.Resources.TryGetValue("AppBorder", out object? border) && border is Brush borderBrush)
                    btn.BorderBrush = borderBrush;

                if (Application.Current.Resources.TryGetValue("AppTextSecondary", out object? textSec) && textSec is Brush textSecBrush)
                {
                    if (btn.Content is StackPanel sp)
                    {
                        foreach (var child in sp.Children)
                        {
                            if (child is TextBlock tb) tb.Foreground = textSecBrush;
                            if (child is FontIcon fi) fi.Foreground = textSecBrush;
                        }
                    }
                }
            }
        }
    }
}
