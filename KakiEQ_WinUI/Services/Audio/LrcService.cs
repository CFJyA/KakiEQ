using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Audio
{
    public class LrcService
    {
        private static readonly Regex LrcTimeRegex = new(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

        public static string GetLyricsFolder()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Lyrics");
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetTrackLrcCachePath(Track track)
        {
            string key = $"{track.Artist.Trim().ToLowerInvariant()}_{track.Title.Trim().ToLowerInvariant()}";
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            string hashStr = Convert.ToHexString(hash).ToLowerInvariant();
            return Path.Combine(GetLyricsFolder(), $"{hashStr}.lrc");
        }

        /// <summary>
        /// Parses an LRC text into an ordered list of LrcLine items.
        /// </summary>
        public static List<LrcLine> Parse(string? lrcContent)
        {
            var result = new List<LrcLine>();
            if (string.IsNullOrWhiteSpace(lrcContent))
                return result;

            using var reader = new StringReader(lrcContent);
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                var matches = LrcTimeRegex.Matches(line);
                if (matches.Count == 0) continue;

                // Extract text by removing all timestamp tags
                string text = LrcTimeRegex.Replace(line, "").Trim();

                foreach (Match m in matches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int min) &&
                        int.TryParse(m.Groups[2].Value, out int sec))
                    {
                        int ms = 0;
                        if (m.Groups[3].Success)
                        {
                            string fractionStr = m.Groups[3].Value;
                            if (fractionStr.Length == 1) ms = int.Parse(fractionStr) * 100;
                            else if (fractionStr.Length == 2) ms = int.Parse(fractionStr) * 10;
                            else if (fractionStr.Length >= 3) ms = int.Parse(fractionStr.Substring(0, 3));
                        }

                        var ts = new TimeSpan(0, 0, min, sec, ms);
                        result.Add(new LrcLine
                        {
                            Timestamp = ts,
                            Text = text
                        });
                    }
                }
            }

            return result.OrderBy(l => l.Timestamp).ToList();
        }

        /// <summary>
        /// Formats an ordered list of LrcLines to a standard LRC text.
        /// </summary>
        public static string Format(IEnumerable<LrcLine> lines, string? title = null, string? artist = null, string? album = null)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title)) sb.AppendLine($"[ti:{title}]");
            if (!string.IsNullOrEmpty(artist)) sb.AppendLine($"[ar:{artist}]");
            if (!string.IsNullOrEmpty(album)) sb.AppendLine($"[al:{album}]");
            sb.AppendLine("[by:Kaki EQ]");

            foreach (var line in lines.OrderBy(l => l.Timestamp))
            {
                sb.AppendLine($"{line.TimestampFormatted} {line.Text}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Loads the synchronized LRC text for a track, checking:
        /// 1) Beside the audio file (.lrc)
        /// 2) Embedded in file tags (tfile.Tag.Lyrics)
        /// 3) Local Kaki EQ Lyrics cache
        /// </summary>
        public static async Task<string?> LoadRawLrcForTrackAsync(Track track)
        {
            if (track == null) return null;

            return await Task.Run(() =>
            {
                // 1. External .lrc file in same folder
                try
                {
                    if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                    {
                        string extLrc = Path.ChangeExtension(track.FilePath, ".lrc");
                        if (File.Exists(extLrc))
                        {
                            return File.ReadAllText(extLrc);
                        }
                    }
                }
                catch { }

                // 2. Embedded in audio file tags
                try
                {
                    if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                    {
                        using var tfile = TagLib.File.Create(track.FilePath);
                        if (!string.IsNullOrWhiteSpace(tfile.Tag.Lyrics))
                        {
                            return tfile.Tag.Lyrics;
                        }
                    }
                }
                catch { }

                // 3. Local cache
                try
                {
                    string cachePath = GetTrackLrcCachePath(track);
                    if (File.Exists(cachePath))
                    {
                        return File.ReadAllText(cachePath);
                    }
                }
                catch { }

                return null;
            });
        }

        /// <summary>
        /// Saves the LRC text to all available storage targets (file beside audio, embedded tag, and local cache).
        /// </summary>
        public static async Task<bool> SaveLrcForTrackAsync(Track track, string lrcContent)
        {
            if (track == null || string.IsNullOrWhiteSpace(lrcContent)) return false;

            return await Task.Run(() =>
            {
                bool savedSomewhere = false;

                // 1. Local Cache (always guaranteed to succeed)
                try
                {
                    string cachePath = GetTrackLrcCachePath(track);
                    File.WriteAllText(cachePath, lrcContent, Encoding.UTF8);
                    savedSomewhere = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error caching LRC: {ex.Message}");
                }

                // 2. Save .lrc next to audio file
                try
                {
                    if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                    {
                        string extLrc = Path.ChangeExtension(track.FilePath, ".lrc");
                        File.WriteAllText(extLrc, lrcContent, Encoding.UTF8);
                        savedSomewhere = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not write .lrc beside audio file: {ex.Message}");
                }

                // 3. Embed inside audio file tags
                try
                {
                    if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                    {
                        using var tfile = TagLib.File.Create(track.FilePath);
                        tfile.Tag.Lyrics = lrcContent;
                        tfile.Save();
                        savedSomewhere = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not embed lyrics tag: {ex.Message}");
                }

                return savedSomewhere;
            });
        }

        /// <summary>
        /// Finds the active line index given the current playback position.
        /// </summary>
        public static int FindActiveIndex(List<LrcLine> lines, TimeSpan position)
        {
            if (lines == null || lines.Count == 0) return -1;
            if (position < lines[0].Timestamp) return -1;

            int active = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (position >= lines[i].Timestamp)
                {
                    active = i;
                }
                else
                {
                    break;
                }
            }
            return active;
        }
    }
}
