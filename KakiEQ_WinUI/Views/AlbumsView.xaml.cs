using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class AlbumsView : UserControl
    {
        public AlbumsViewModel? ViewModel => DataContext as AlbumsViewModel;
        private bool? _isMaximizedLayout = null;

        public AlbumsView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                Bindings?.Update();
            };
            AlbumsGridView.Loaded += (s, e) =>
            {
                ApplyItemDimensions(CheckIsMaximized(ActualWidth));
            };
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateGridItemSize(ActualWidth);
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateGridItemSize(e.NewSize.Width);
        }

        private bool CheckIsMaximized(double width)
        {
            return (MainWindow.Instance?.IsMaximized == true) || width > 1250;
        }

        private void UpdateGridItemSize(double width)
        {
            if (width <= 0) return;
            bool isMax = CheckIsMaximized(width);
            if (_isMaximizedLayout != isMax)
            {
                _isMaximizedLayout = isMax;
                ApplyItemDimensions(isMax);
            }
        }

        private void ApplyItemDimensions(bool isMax)
        {
            var wrapGrid = GetWrapGrid(AlbumsGridView);
            if (wrapGrid != null)
            {
                double targetWidth = isMax ? 180 : 171;
                double targetHeight = isMax ? 240 : 232;
                if (wrapGrid.ItemWidth != targetWidth || wrapGrid.ItemHeight != targetHeight)
                {
                    wrapGrid.ItemWidth = targetWidth;
                    wrapGrid.ItemHeight = targetHeight;
                }
            }
        }

        private static ItemsWrapGrid? GetWrapGrid(GridView? gridView)
        {
            if (gridView == null) return null;
            if (gridView.ItemsPanelRoot is ItemsWrapGrid panel) return panel;
            return FindChild<ItemsWrapGrid>(gridView);
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
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

