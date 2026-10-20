using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ImageMagick;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KakiEQ_WinUI.Helpers
{
    public static class ImageHelper
    {
        private static readonly string JxlCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KakiEQ",
            "Cache",
            "JxlCache");

        static ImageHelper()
        {
            try
            {
                Directory.CreateDirectory(JxlCacheDir);
            }
            catch { }
        }

        /// <summary>
        /// Checks whether the given file or path is a JPEG XL image (.jxl).
        /// </summary>
        public static bool IsJxl(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.EndsWith(".jxl", StringComparison.OrdinalIgnoreCase)) return true;

            // Also check magic bytes if file exists
            try
            {
                if (File.Exists(path))
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    byte[] header = new byte[12];
                    int read = fs.Read(header, 0, header.Length);
                    if (read >= 2 && header[0] == 0xFF && header[1] == 0x0A) return true; // Direct codestream
                    if (read >= 12 && header[4] == 0x4A && header[5] == 0x58 && header[6] == 0x4C && header[7] == 0x20) return true; // ISOBMFF container
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Ensures that the returned image path is directly renderable by WinUI 3 (WIC).
        /// If the image is JPEG XL (.jxl), it is converted to a lossless/high-quality cached PNG.
        /// </summary>
        public static string EnsureDisplayableImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return path ?? string.Empty;

            if (!IsJxl(path))
                return path;

            try
            {
                Directory.CreateDirectory(JxlCacheDir);

                var fi = new FileInfo(path);
                string key = $"{path}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
                using var md5 = MD5.Create();
                string hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
                string cachedPng = Path.Combine(JxlCacheDir, $"{hash}.png");

                if (File.Exists(cachedPng) && new FileInfo(cachedPng).Length > 0)
                {
                    return cachedPng;
                }

                // Decode JXL using Magick.NET and write to cached PNG
                using (var image = new MagickImage(path))
                {
                    image.Format = MagickFormat.Png;
                    image.Write(cachedPng);
                }

                return cachedPng;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageHelper] Error decoding JXL '{path}': {ex.Message}");
                return path;
            }
        }

        /// <summary>
        /// Converts raw bytes that may contain JPEG XL into displayable PNG bytes.
        /// </summary>
        public static byte[] EnsureDisplayableBytes(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length < 2) return rawBytes ?? Array.Empty<byte>();

            bool isJxl = (rawBytes[0] == 0xFF && rawBytes[1] == 0x0A) ||
                         (rawBytes.Length >= 12 && rawBytes[4] == 0x4A && rawBytes[5] == 0x58 && rawBytes[6] == 0x4C && rawBytes[7] == 0x20);

            if (!isJxl) return rawBytes;

            try
            {
                using var image = new MagickImage(rawBytes);
                using var ms = new MemoryStream();
                image.Format = MagickFormat.Png;
                image.Write(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageHelper] Error converting JXL bytes: {ex.Message}");
                return rawBytes;
            }
        }

        /// <summary>
        /// Synchronously creates a WinUI BitmapImage from any supported image file, including .jxl.
        /// </summary>
        public static BitmapImage? LoadBitmapSync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                string displayPath = EnsureDisplayableImage(path);
                return new BitmapImage(new Uri(Path.GetFullPath(displayPath)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageHelper] Error loading BitmapImage: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Searches the directory containing an audio file for common cover art filenames, including .jxl.
        /// </summary>
        public static string? FindCoverInDirectory(string audioFilePath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(audioFilePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

                string[] searchNames = new[]
                {
                    "cover.jxl", "folder.jxl", "front.jxl", "album.jxl", "artwork.jxl",
                    "cover.jpg", "folder.jpg", "front.jpg", "album.jpg", "artwork.jpg",
                    "cover.png", "folder.png", "front.png", "album.png", "artwork.png",
                    "cover.webp", "folder.webp", "front.webp"
                };

                foreach (var name in searchNames)
                {
                    string candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
