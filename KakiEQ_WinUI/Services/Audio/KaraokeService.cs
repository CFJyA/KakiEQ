using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KakiEQ",
            "KaraokeCache");

        private static readonly ConcurrentDictionary<string, bool> CachedKeys = new();
        private static readonly ConcurrentQueue<Track> HighPriorityQueue = new();
        private static readonly ConcurrentQueue<Track> LibraryQueue = new();
        private static readonly HashSet<string> EnqueuedKeys = new();
        private static readonly object PreloadLock = new();
        private static bool _isPreloaderRunning = false;

        public static event EventHandler<Track>? TrackKaraokeReady;

        static KaraokeService()
        {
            Initialize();
        }

        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    Directory.CreateDirectory(CacheDirectory);
                }

                var files = Directory.GetFiles(CacheDirectory, "*.kinst");
                foreach (var file in files)
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(key))
                    {
                        CachedKeys[key] = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KaraokeService] Init error: {ex.Message}");
            }
        }

        public static string GetTrackKey(Track track)
        {
            if (string.IsNullOrEmpty(track.FilePath)) return string.Empty;
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(track.FilePath.ToLowerInvariant()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static string? GetInstrumentalPath(Track? track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return null;
            string key = GetTrackKey(track);
            if (string.IsNullOrEmpty(key)) return null;

            string expectedPath = Path.Combine(CacheDirectory, $"{key}.kinst");
            if (CachedKeys.ContainsKey(key) || File.Exists(expectedPath))
            {
                CachedKeys[key] = true;
                return expectedPath;
            }
            return null;
        }

        public static bool IsTrackReady(Track? track)
        {
            return GetInstrumentalPath(track) != null;
        }

        public static void ClearCache()
        {
            try
            {
                CachedKeys.Clear();
                if (Directory.Exists(CacheDirectory))
                {
                    var files = Directory.GetFiles(CacheDirectory, "*.kinst");
                    foreach (var file in files)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KaraokeService] ClearCache error: {ex.Message}");
            }
        }

        private static string GetScriptPath()
        {
            string appBase = AppContext.BaseDirectory;
            string localScript = Path.Combine(appBase, "Assets", "Scripts", "separate_in_memory.py");
            if (File.Exists(localScript)) return localScript;

            string projectFallback = Path.GetFullPath(Path.Combine(appBase, "..", "..", "..", "..", "KakiEQ_WinUI", "Assets", "Scripts", "separate_in_memory.py"));
            if (File.Exists(projectFallback)) return projectFallback;

            return localScript;
        }

        public static async Task<string?> SeparateTrackAsync(
            Track track,
            IProgress<int>? progress = null,
            Action<string>? statusCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
            {
                return null;
            }

            string? existing = GetInstrumentalPath(track);
            if (!string.IsNullOrEmpty(existing))
            {
                progress?.Report(100);
                return existing;
            }

            string key = GetTrackKey(track);
            string scriptPath = GetScriptPath();
            if (!File.Exists(scriptPath))
            {
                Debug.WriteLine($"[KaraokeService] Script not found at {scriptPath}");
                return null;
            }

            string targetKinstPath = Path.Combine(CacheDirectory, $"{key}.kinst");
            statusCallback?.Invoke("Aislando instrumental con aceleración GPU...");

            return await Task.Run(() =>
            {
                Process? process = null;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{scriptPath}\" \"{track.FilePath}\" \"{targetKinstPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    process = new Process { StartInfo = psi };

                    var percentRegex = new Regex(@"PROGRESS:(\d+)", RegexOptions.Compiled);

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;

                        var match = percentRegex.Match(e.Data);
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                        {
                            progress?.Report(Math.Clamp(pct, 1, 99));
                        }
                    };

                    process.Start();
                    try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    process.BeginErrorReadLine();

                    while (!process.WaitForExit(500))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(true); } catch { }
                            return null;
                        }
                    }

                    if (process.ExitCode == 0 && File.Exists(targetKinstPath))
                    {
                        CachedKeys[key] = true;
                        progress?.Report(100);
                        TrackKaraokeReady?.Invoke(null, track);
                        return targetKinstPath;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[KaraokeService] Separation error: {ex.Message}");
                    return null;
                }
                finally
                {
                    process?.Dispose();
                }
            }, cancellationToken);
        }

        // --- INTELLIGENT PRE-PROCESSING ENGINE ---
        public static void PrioritizeTrack(Track? track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath) || IsTrackReady(track))
                return;

            string key = GetTrackKey(track);
            lock (PreloadLock)
            {
                HighPriorityQueue.Enqueue(track);
                EnqueuedKeys.Add(key);

                if (!_isPreloaderRunning)
                {
                    _isPreloaderRunning = true;
                    _ = Task.Run(ProcessPreloadQueueAsync);
                }
            }
        }

        public static void EnqueueTracksForPreload(IEnumerable<Track>? tracks)
        {
            if (tracks == null) return;

            lock (PreloadLock)
            {
                foreach (var track in tracks)
                {
                    if (track == null || string.IsNullOrEmpty(track.FilePath)) continue;
                    string key = GetTrackKey(track);
                    if (string.IsNullOrEmpty(key) || CachedKeys.ContainsKey(key) || EnqueuedKeys.Contains(key))
                        continue;

                    EnqueuedKeys.Add(key);
                    LibraryQueue.Enqueue(track);
                }

                if (!_isPreloaderRunning && (!HighPriorityQueue.IsEmpty || !LibraryQueue.IsEmpty))
                {
                    _isPreloaderRunning = true;
                    _ = Task.Run(ProcessPreloadQueueAsync);
                }
            }
        }

        private static async Task ProcessPreloadQueueAsync()
        {
            while (true)
            {
                Track? nextTrack = null;
                lock (PreloadLock)
                {
                    // Always process high-priority (active song & upcoming song) first!
                    if (HighPriorityQueue.TryDequeue(out nextTrack))
                    {
                    }
                    else if (LibraryQueue.TryDequeue(out nextTrack))
                    {
                    }
                    else
                    {
                        _isPreloaderRunning = false;
                        return;
                    }
                }

                if (nextTrack != null)
                {
                    try
                    {
                        if (!IsTrackReady(nextTrack))
                        {
                            await SeparateTrackAsync(nextTrack);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[KaraokePreloader] Error: {ex.Message}");
                    }
                }

                await Task.Delay(500);
            }
        }
    }
}
