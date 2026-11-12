using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Helpers
{
    public static class DiscMetadataHelper
    {
        /// <summary>
        /// Reads the Disc metadata from a TagLib.File, supporting both standard numeric disc numbers (1, 2, 3...)
        /// and custom text disc names (e.g., "Agua", "Tierra", "Fuego", "Viento").
        /// </summary>
        public static string GetDiscFromTag(TagLib.File tfile)
        {
            if (tfile?.Tag == null)
                return string.Empty;

            try
            {
                // 1. Check Xiph / Vorbis Comments (FLAC, OGG, OPUS)
                if (tfile.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
                {
                    string? subtitle = xiph.GetFirstField("DISCSUBTITLE");
                    if (!string.IsNullOrWhiteSpace(subtitle))
                        return NormalizeSavedDiscValue(subtitle);

                    string? discNum = xiph.GetFirstField("DISCNUMBER") ?? xiph.GetFirstField("DISC");
                    if (!string.IsNullOrWhiteSpace(discNum))
                        return NormalizeSavedDiscValue(discNum);
                }

                // 2. Check ID3v2 frames (MP3, WAV, AIFF)
                if (tfile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2)
                {
                    // TSST = Set Subtitle (ID3v2.4)
                    var tsst = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TSST", false);
                    string? tsstText = tsst?.Text?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(tsstText))
                        return NormalizeSavedDiscValue(tsstText);

                    // TXXX custom DISCSUBTITLE or DISC
                    var txxxSubtitle = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "DISCSUBTITLE", false);
                    string? txxxSubText = txxxSubtitle?.Text?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(txxxSubText))
                        return NormalizeSavedDiscValue(txxxSubText);

                    // TPOS = Part of a set (standard Disc field in ID3v2; can contain numbers like "1/2" or text like "Agua")
                    var tpos = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TPOS", false);
                    string? tposText = tpos?.Text?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(tposText))
                        return NormalizeSavedDiscValue(tposText);
                }

                // 3. Check Apple / MP4 tags (M4A, AAC)
                if (tfile.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
                {
                    string? dashSub = apple.GetDashBox("com.apple.iTunes", "DISCSUBTITLE");
                    if (!string.IsNullOrWhiteSpace(dashSub))
                        return NormalizeSavedDiscValue(dashSub);

                    string? dashDisc = apple.GetDashBox("com.apple.iTunes", "DISC");
                    if (!string.IsNullOrWhiteSpace(dashDisc))
                        return NormalizeSavedDiscValue(dashDisc);
                }

                // 4. Check ASF / WMA tags
                if (tfile.GetTag(TagLib.TagTypes.Asf, false) is TagLib.Asf.Tag asf)
                {
                    string? partOfSet = asf.GetDescriptorString("WM/PartOfSet");
                    if (!string.IsNullOrWhiteSpace(partOfSet))
                        return NormalizeSavedDiscValue(partOfSet);
                }

                // 5. Fallback to standard numeric Tag.Disc
                if (tfile.Tag.Disc > 0)
                {
                    return tfile.Tag.Disc.ToString();
                }
            }
            catch
            {
                if (tfile.Tag.Disc > 0)
                    return tfile.Tag.Disc.ToString();
            }

            return string.Empty;
        }

        /// <summary>
        /// Writes a numeric or custom text Disc value into the audio file's tags.
        /// </summary>
        public static void SetDiscInTag(TagLib.File tfile, string? rawDiscInput)
        {
            if (tfile?.Tag == null)
                return;

            string cleaned = NormalizeSavedDiscValue(rawDiscInput);

            if (string.IsNullOrEmpty(cleaned))
            {
                tfile.Tag.Disc = 0;
                ClearCustomDiscTags(tfile);
                return;
            }

            if (uint.TryParse(cleaned, out uint numericDisc))
            {
                ClearCustomDiscTags(tfile);
                tfile.Tag.Disc = numericDisc;
                return;
            }

            bool isFlac = tfile is TagLib.Flac.File ||
                          (!string.IsNullOrEmpty(tfile.Name) && tfile.Name.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));

            bool isVorbisOrOgg = isFlac || tfile is TagLib.Ogg.File ||
                                 (!string.IsNullOrEmpty(tfile.Name) && 
                                  (tfile.Name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || 
                                   tfile.Name.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)));

            bool isAppleMp4 = tfile is TagLib.Mpeg4.File ||
                              (!string.IsNullOrEmpty(tfile.Name) && 
                               (tfile.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) || 
                                tfile.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || 
                                tfile.Name.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)));

            bool isAsf = tfile is TagLib.Asf.File ||
                         (!string.IsNullOrEmpty(tfile.Name) && tfile.Name.EndsWith(".wma", StringComparison.OrdinalIgnoreCase));

            // Custom text disc value (e.g. "Agua", "Tierra", "Fuego", "Viento")
            tfile.Tag.Disc = 0;

            if (isFlac)
            {
                // FLAC files must NEVER contain ID3 tags
                tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);

                if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                {
                    xiph.SetField("DISCNUMBER", cleaned);
                    xiph.SetField("DISCSUBTITLE", cleaned);
                }
            }
            else if (isVorbisOrOgg)
            {
                if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                {
                    xiph.SetField("DISCNUMBER", cleaned);
                    xiph.SetField("DISCSUBTITLE", cleaned);
                }
            }
            else if (isAppleMp4)
            {
                if (tfile.GetTag(TagLib.TagTypes.Apple, true) is TagLib.Mpeg4.AppleTag apple)
                {
                    apple.SetDashBox("com.apple.iTunes", "DISCSUBTITLE", cleaned);
                    apple.SetDashBox("com.apple.iTunes", "DISC", cleaned);
                }
            }
            else if (isAsf)
            {
                if (tfile.GetTag(TagLib.TagTypes.Asf, true) is TagLib.Asf.Tag asf)
                {
                    asf.SetDescriptorString(cleaned, "WM/PartOfSet");
                }
            }
            else
            {
                // ID3v2 containers (MP3, WAV, AIFF)
                if (tfile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2)
                {
                    var tpos = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TPOS", true);
                    if (tpos != null) tpos.Text = new[] { cleaned };

                    var tsst = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TSST", true);
                    if (tsst != null) tsst.Text = new[] { cleaned };
                }
            }
        }

        private static void ClearCustomDiscTags(TagLib.File tfile)
        {
            try
            {
                bool isFlac = tfile is TagLib.Flac.File ||
                              (!string.IsNullOrEmpty(tfile.Name) && tfile.Name.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));

                if (isFlac)
                {
                    tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
                }

                if (tfile.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
                {
                    xiph.RemoveField("DISCSUBTITLE");
                    xiph.RemoveField("DISC");
                }

                if (!isFlac && tfile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2)
                {
                    id3v2.RemoveFrames("TSST");
                    var txxxSub = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "DISCSUBTITLE", false);
                    if (txxxSub != null) id3v2.RemoveFrame(txxxSub);
                }

                if (tfile.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
                {
                    apple.SetDashBox("com.apple.iTunes", "DISCSUBTITLE", string.Empty);
                    apple.SetDashBox("com.apple.iTunes", "DISC", string.Empty);
                }
            }
            catch { }
        }

        /// <summary>
        /// Normalizes raw tag values such as "01/02" -> "1", "02" -> "2", "0" -> "", and preserves custom strings like "Agua".
        /// </summary>
        public static string NormalizeSavedDiscValue(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string trimmed = raw.Trim();

            // Handle "disc/total" format like "1/2" or "01/03"
            int slashIdx = trimmed.IndexOf('/');
            if (slashIdx > 0)
            {
                string firstPart = trimmed.Substring(0, slashIdx).Trim();
                string secondPart = trimmed.Substring(slashIdx + 1).Trim();
                if (int.TryParse(firstPart, out int discNum) && (string.IsNullOrEmpty(secondPart) || int.TryParse(secondPart, out _)))
                {
                    return discNum > 0 ? discNum.ToString() : string.Empty;
                }
            }

            if (int.TryParse(trimmed, out int parsedNum))
            {
                return parsedNum > 0 ? parsedNum.ToString() : string.Empty;
            }

            return trimmed;
        }

        /// <summary>
        /// Lazily populates Track.Disc from the audio file for any tracks loaded from an older database cache where Disc was null.
        /// </summary>
        public static bool EnsureTracksDiscLoaded(IEnumerable<Track> tracks)
        {
            bool anyUpdated = false;
            foreach (var track in tracks)
            {
                if (track.Disc == null)
                {
                    string extracted = string.Empty;
                    try
                    {
                        if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                        {
                            using var tfile = TagLib.File.Create(track.FilePath);
                            extracted = GetDiscFromTag(tfile);
                        }
                    }
                    catch { }

                    track.Disc = extracted;
                    anyUpdated = true;
                }
            }
            return anyUpdated;
        }

        /// <summary>
        /// Returns the effective group key for a track's disc metadata. Empty/unset defaults to "1".
        /// </summary>
        public static string NormalizeDiscGroupKey(string? discValue)
        {
            string normalized = NormalizeSavedDiscValue(discValue);
            return string.IsNullOrEmpty(normalized) ? "1" : normalized;
        }

        /// <summary>
        /// Compares two disc group keys: numeric keys sort numerically (1, 2, 3...),
        /// while text keys (e.g. "Agua", "Tierra", "Fuego", "Viento") sort alphabetically.
        /// </summary>
        public static int CompareDiscKeys(string? a, string? b)
        {
            string sa = NormalizeDiscGroupKey(a);
            string sb = NormalizeDiscGroupKey(b);

            bool aIsNum = int.TryParse(sa, out int numA);
            bool bIsNum = int.TryParse(sb, out int numB);

            if (aIsNum && bIsNum)
            {
                return numA.CompareTo(numB);
            }

            if (!aIsNum && !bIsNum)
            {
                return string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
            }

            // Numeric discs precede text-named discs
            return aIsNum ? -1 : 1;
        }

        /// <summary>
        /// Formats the header label for a disc section (e.g. "1" -> "Disco 1", "Agua" -> "Disco Agua").
        /// </summary>
        public static string FormatDiscHeaderTitle(string discKey)
        {
            string clean = NormalizeDiscGroupKey(discKey);
            if (clean.StartsWith("Disco ", StringComparison.OrdinalIgnoreCase) ||
                clean.StartsWith("Disc ", StringComparison.OrdinalIgnoreCase))
            {
                return clean;
            }
            return $"Disco {clean}";
        }

        /// <summary>
        /// Builds both the ordered playable track list and the UI display list (which includes Spotify-style
        /// "Disco X" header rows ONLY when the album has more than 1 distinct disc).
        /// </summary>
        public static (List<Track> OrderedPlayableTracks, List<Track> DisplayTracks) BuildAlbumTrackLists(IEnumerable<Track> rawAlbumTracks)
        {
            var trackList = rawAlbumTracks?.Where(t => !t.IsDiscHeader).ToList() ?? new List<Track>();
            if (trackList.Count == 0)
            {
                return (new List<Track>(), new List<Track>());
            }

            var discGroups = trackList
                .GroupBy(t => NormalizeDiscGroupKey(t.Disc), StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            discGroups.Sort((g1, g2) => CompareDiscKeys(g1.Key, g2.Key));

            var orderedPlayable = new List<Track>(trackList.Count);
            var displayList = new List<Track>(trackList.Count + discGroups.Count);
            bool hasMultipleDiscs = discGroups.Count > 1;

            foreach (var group in discGroups)
            {
                var sortedGroupTracks = group
                    .OrderBy(t => t.TrackNumber > 0 ? t.TrackNumber : int.MaxValue)
                    .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (hasMultipleDiscs)
                {
                    displayList.Add(new Track
                    {
                        IsDiscHeader = true,
                        DiscHeaderTitle = FormatDiscHeaderTitle(group.Key)
                    });
                }

                foreach (var track in sortedGroupTracks)
                {
                    track.IsDiscHeader = false;
                    orderedPlayable.Add(track);
                    displayList.Add(track);
                }
            }

            return (orderedPlayable, displayList);
        }
    }
}
