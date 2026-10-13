using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KakiEQ_WinUI.Helpers
{
    public static class GridViewLayoutHelper
    {
        public static void AdjustItemWidth(GridView gridView, double minItemWidth, double itemHeight, double horizontalBuffer = 0)
        {
            if (gridView == null) return;

            var wrapGrid = gridView.ItemsPanelRoot as ItemsWrapGrid ?? FindVisualChild<ItemsWrapGrid>(gridView);
            if (wrapGrid == null)
            {
                RoutedEventHandler? onLoaded = null;
                onLoaded = (s, e) =>
                {
                    gridView.Loaded -= onLoaded;
                    AdjustItemWidth(gridView, minItemWidth, itemHeight, horizontalBuffer);
                };
                gridView.Loaded += onLoaded;
                return;
            }

            var scrollViewer = FindVisualChild<ScrollViewer>(gridView);
            double availableWidth;
            if (scrollViewer != null && scrollViewer.ViewportWidth > 0)
            {
                availableWidth = scrollViewer.ViewportWidth;
            }
            else
            {
                availableWidth = gridView.ActualWidth;
            }

            if (availableWidth <= 0 || minItemWidth <= 0) return;

            // Subtract buffer (e.g. for scrollbars or spacing) plus 2px tolerance for subpixel rounding
            double usableWidth = Math.Max(minItemWidth, availableWidth - 2 - horizontalBuffer);
            int columns = Math.Max(1, (int)Math.Floor(usableWidth / minItemWidth));
            double itemWidth = Math.Floor(usableWidth / columns);

            wrapGrid.ItemWidth = itemWidth;
            if (itemHeight > 0)
            {
                wrapGrid.ItemHeight = itemHeight;
            }
        }

        public static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}

