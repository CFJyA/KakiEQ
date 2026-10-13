using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KakiEQ_WinUI.Helpers
{
    public static class ColorHelper
    {
        private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(255, 30, 30, 30));

        public static SolidColorBrush GetDefaultBrush() => DefaultBrush;

        public static SolidColorBrush FromHex(string hex)
        {
            try
            {
                hex = hex.Replace("#", "");
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return new SolidColorBrush(Color.FromArgb(255, r, g, b));
                }
                else if (hex.Length == 8)
                {
                    byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                    return new SolidColorBrush(Color.FromArgb(a, r, g, b));
                }
            }
            catch { }
            return DefaultBrush;
        }

        public static async Task<SolidColorBrush> GetDominantColorAsync(string? imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return DefaultBrush;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(imagePath);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(stream);

                // Scale down to 32x32 for super-fast average color sampling
                var transform = new BitmapTransform
                {
                    ScaledWidth = 32,
                    ScaledHeight = 32,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] pixels = pixelData.DetachPixelData();
                long r = 0, g = 0, b = 0;
                int count = 0;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    b += pixels[i];
                    g += pixels[i + 1];
                    r += pixels[i + 2];
                    count++;
                }

                if (count == 0) return DefaultBrush;

                byte avgR = (byte)(r / count);
                byte avgG = (byte)(g / count);
                byte avgB = (byte)(b / count);

                // Darken slightly for readability of overlays
                avgR = (byte)Math.Clamp(avgR * 0.45, 18, 180);
                avgG = (byte)Math.Clamp(avgG * 0.45, 18, 180);
                avgB = (byte)Math.Clamp(avgB * 0.45, 18, 180);

                return new SolidColorBrush(Color.FromArgb(255, avgR, avgG, avgB));
            }
            catch
            {
                return DefaultBrush;
            }
        }
    }
}

