using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Audio
{
    public static class KaraokeService
    {
        private static readonly string KaraokeCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KakiEQ",
            "KaraokeCache");

        static KaraokeService()
        {
            try
            {
                Directory.CreateDirectory(KaraokeCacheDir);
            }
            catch { }
        }

        /// <summary>
        /// Calculates a unique MD5 hash string for a track based on its file path.
        /// </summary>
        public static string GetTrackHash(Track track)
        {
            if (string.IsNullOrEmpty(track.FilePath)) return string.Empty;
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(track.FilePath.ToLowerInvariant()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Checks whether an AI-separated or pre-existing instrumental version is available.
        /// </summary>
        public static string? GetInstrumentalPath(Track? track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return null;

            // 1. Check AI cache directory
            string hash = GetTrackHash(track);
            if (!string.IsNullOrEmpty(hash))
            {
                string targetDir = Path.Combine(KaraokeCacheDir, hash);
                string directFile = Path.Combine(targetDir, "no_vocals.mp3");
                if (File.Exists(directFile) && new FileInfo(directFile).Length > 0)
                {
                    return directFile;
                }

                // Also check subfolder structure produced by demucs
                string rawName = Path.GetFileNameWithoutExtension(track.FilePath);
                string subFile = Path.Combine(targetDir, "htdemucs", rawName, "no_vocals.mp3");
                if (File.Exists(subFile) && new FileInfo(subFile).Length > 0)
                {
                    return subFile;
                }
            }

            // 2. Check the directory where the audio file lives for existing instrumental
            try
            {
                string? dir = Path.GetDirectoryName(track.FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string baseName = Path.GetFileNameWithoutExtension(track.FilePath);
                    string ext = Path.GetExtension(track.FilePath);

                    string[] suffixes = new[]
                    {
                        " [Instrumental]", " - Instrumental", " (Instrumental)",
                        " [Karaoke]", " - Karaoke", " (Karaoke)",
                        "_instrumental", "_karaoke"
                    };

                    foreach (var suffix in suffixes)
                    {
                        string candidate = Path.Combine(dir, $"{baseName}{suffix}{ext}");
                        if (File.Exists(candidate)) return candidate;

                        // Also check .mp3 and .wav extensions
                        string candMp3 = Path.Combine(dir, $"{baseName}{suffix}.mp3");
                        if (File.Exists(candMp3)) return candMp3;

                        string candWav = Path.Combine(dir, $"{baseName}{suffix}.wav");
                        if (File.Exists(candWav)) return candWav;
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool HasInstrumental(Track? track)
        {
            return GetInstrumentalPath(track) != null;
        }

        /// <summary>
        /// Runs AI separation using Demucs in the background to isolate the instrumental track without vocals.
        /// </summary>
        public static async Task<string?> SeparateTrackWithAiAsync(
            Track track,
            IProgress<int>? progress = null,
            Action<string>? statusCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
            {
                return null;
            }

            // If already generated, return instantly
            string? existing = GetInstrumentalPath(track);
            if (!string.IsNullOrEmpty(existing))
            {
                progress?.Report(100);
                return existing;
            }

            string hash = GetTrackHash(track);
            string targetDir = Path.Combine(KaraokeCacheDir, hash);
            Directory.CreateDirectory(targetDir);

            string finalFile = Path.Combine(targetDir, "no_vocals.mp3");

            statusCallback?.Invoke("Iniciando motor de IA...");

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"-u -m demucs --two-stems vocals --mp3 -n htdemucs -o \"{targetDir}\" \"{track.FilePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using var process = new Process { StartInfo = psi };

                    var percentRegex = new Regex(@"(\d+)%", RegexOptions.Compiled);

                    DataReceivedEventHandler outputHandler = (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;

                        var match = percentRegex.Match(e.Data);
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                        {
                            progress?.Report(Math.Clamp(pct, 1, 99));
                        }

                        if (e.Data.Contains("Separating") || e.Data.Contains("Selected model"))
                        {
                            statusCallback?.Invoke("Separando voz con IA...");
                        }
                    };

                    process.OutputDataReceived += outputHandler;
                    process.ErrorDataReceived += outputHandler;

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    while (!process.WaitForExit(500))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(true); } catch { }
                            return null;
                        }
                    }

                    // Check output file
                    string rawName = Path.GetFileNameWithoutExtension(track.FilePath);
                    string subFile = Path.Combine(targetDir, "htdemucs", rawName, "no_vocals.mp3");

                    if (File.Exists(subFile) && new FileInfo(subFile).Length > 0)
                    {
                        try
                        {
                            File.Copy(subFile, finalFile, true);
                        }
                        catch { }

                        progress?.Report(100);
                        return File.Exists(finalFile) ? finalFile : subFile;
                    }

                    if (File.Exists(finalFile) && new FileInfo(finalFile).Length > 0)
                    {
                        progress?.Report(100);
                        return finalFile;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[KaraokeService] AI Separation error: {ex.Message}");
                    return null;
                }
            }, cancellationToken);
        }
    }
}
