using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KakiEQ_WinUI.Helpers
{
    public static class ImageBlurHelper
    {
        private static readonly string BlurCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KakiEQ",
            "BlurCache");

        /// <summary>
        /// Generates or retrieves a heavily blurred version of a cover art image.
        /// Scales down to a compact 40x40 grid and runs a multi-pass box blur for a smooth, high-fidelity Gaussian blur aesthetic.
        /// </summary>
        public static async Task<string?> GetBlurredCoverAsync(string? originalCoverPath)
        {
            if (string.IsNullOrEmpty(originalCoverPath) || !File.Exists(originalCoverPath))
                return null;

            try
            {
                Directory.CreateDirectory(BlurCacheDir);

                using var md5 = MD5.Create();
                string hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(originalCoverPath)));
                string blurPath = Path.Combine(BlurCacheDir, $"{hash}_blur80.jpg");

                if (File.Exists(blurPath))
                {
                    var fileInfo = new FileInfo(blurPath);
                    if (fileInfo.Length > 0)
                        return blurPath;
                }

                return await Task.Run(async () =>
                {
                    try
                    {
                        string effectiveSource = ImageHelper.EnsureDisplayableImage(originalCoverPath);
                        using var inFs = new FileStream(effectiveSource, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var inStream = inFs.AsRandomAccessStream();
                        var decoder = await BitmapDecoder.CreateAsync(inStream);

                        // Downscale to 120x120 for an ~80% smooth frosted blur where the artwork is distinguishable
                        uint targetW = 120;
                        uint targetH = 120;

                        var transform = new BitmapTransform
                        {
                            ScaledWidth = targetW,
                            ScaledHeight = targetH,
                            InterpolationMode = BitmapInterpolationMode.Linear
                        };

                        var pixelData = await decoder.GetPixelDataAsync(
                            BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Premultiplied,
                            transform,
                            ExifOrientationMode.RespectExifOrientation,
                            ColorManagementMode.ColorManageToSRgb);

                        byte[] pixels = pixelData.DetachPixelData();

                        // Multi-pass box blur on 120x120 pixel array (radius 3, 2 passes) for ~80% blur
                        FastBoxBlur(pixels, (int)targetW, (int)targetH, 3, 2);

                        // Save blurred JPEG directly to BlurCache
                        using (var outFs = new FileStream(blurPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                        using (var outStream = outFs.AsRandomAccessStream())
                        {
                            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
                            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, targetW, targetH, 96, 96, pixels);
                            await encoder.FlushAsync();
                        }

                        return blurPath;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImageBlurHelper] Error generating blur: {ex.Message}");
                        return originalCoverPath;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageBlurHelper] Outer error: {ex.Message}");
                return originalCoverPath;
            }
        }

        private static void FastBoxBlur(byte[] pixels, int w, int h, int radius, int passes)
        {
            byte[] buffer = new byte[pixels.Length];

            for (int p = 0; p < passes; p++)
            {
                // Horizontal pass
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int b = 0, g = 0, r = 0, count = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int nx = Math.Clamp(x + k, 0, w - 1);
                            int idx = (y * w + nx) * 4;
                            b += pixels[idx];
                            g += pixels[idx + 1];
                            r += pixels[idx + 2];
                            count++;
                        }
                        int tidx = (y * w + x) * 4;
                        buffer[tidx] = (byte)(b / count);
                        buffer[tidx + 1] = (byte)(g / count);
                        buffer[tidx + 2] = (byte)(r / count);
                        buffer[tidx + 3] = 255;
                    }
                }

                // Vertical pass
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int b = 0, g = 0, r = 0, count = 0;
                        for (int k = -radius; k <= radius; k++)
                        {
                            int ny = Math.Clamp(y + k, 0, h - 1);
                            int idx = (ny * w + x) * 4;
                            b += buffer[idx];
                            g += buffer[idx + 1];
                            r += buffer[idx + 2];
                            count++;
                        }
                        int sidx = (y * w + x) * 4;
                        pixels[sidx] = (byte)(b / count);
                        pixels[sidx + 1] = (byte)(g / count);
                        pixels[sidx + 2] = (byte)(r / count);
                        pixels[sidx + 3] = 255;
                    }
                }
            }
        }
    }
}
