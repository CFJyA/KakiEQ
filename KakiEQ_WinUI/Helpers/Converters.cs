using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KakiEQ_WinUI.Helpers
{
    public static class Converters
    {
        private static readonly SolidColorBrush AccentBrush = new(Color.FromArgb(255, 187, 134, 252)); // #BB86FC
        private static readonly SolidColorBrush InactiveBrush = new(Color.FromArgb(255, 119, 119, 119)); // #777777
        private static readonly SolidColorBrush HeartBrush = new(Color.FromArgb(255, 255, 64, 129)); // #FF4081

        public static Visibility ToVisibility(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;
        public static SolidColorBrush ToAccentBrush(bool isActive) => isActive ? AccentBrush : InactiveBrush;
        public static SolidColorBrush ToHeartBrush(bool isLiked) => isLiked ? HeartBrush : InactiveBrush;
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool val = value is bool b && b;
            if (Invert) val = !val;
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            bool isVis = value is Visibility v && v == Visibility.Visible;
            return Invert ? !isVis : isVis;
        }
    }

    public class BoolToAccentBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return Converters.ToAccentBrush(value is bool b && b);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToHeartBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return Converters.ToHeartBrush(value is bool b && b);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed class ImagePathToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null!;

            try
            {
                Uri uri;
                if (System.IO.File.Exists(path))
                    uri = new Uri(System.IO.Path.GetFullPath(path));
                else if (!Uri.TryCreate(path, UriKind.Absolute, out uri!))
                    return null!;

                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
            }
            catch
            {
                return null!;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

