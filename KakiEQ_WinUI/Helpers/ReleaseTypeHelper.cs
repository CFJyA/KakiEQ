using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KakiEQ_WinUI.Helpers
{
    public static class ReleaseTypeHelper
    {
        public const string TypeAlbum = "Álbum";
        public const string TypeEp = "EP";
        public const string TypeSingle = "Sencillo";

        /// <summary>
        /// Reads release type metadata from an audio file using TagLib.
        /// Checks standard ID3v2 TXXX (RELEASETYPE, MusicBrainz Album Type), Vorbis/FLAC, and Apple MP4 containers.
        /// </summary>
        public static string GetReleaseTypeFromTag(TagLib.File tfile)
        {
            if (tfile?.Tag == null)
                return string.Empty;

            try
            {
                // 1. Check Xiph / Vorbis Comments (FLAC, OGG, OPUS)
                if (tfile.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
                {
                    string? relType = xiph.GetFirstField("RELEASETYPE")
                                   ?? xiph.GetFirstField("MUSICBRAINZ_ALBUMTYPE")
                                   ?? xiph.GetFirstField("ALBUMTYPE");

                    if (!string.IsNullOrWhiteSpace(relType))
                    {
                        var norm = NormalizeReleaseType(relType);
                        if (!string.IsNullOrEmpty(norm)) return norm;
                    }
                }

                // 2. Check ID3v2 frames (MP3, WAV, AIFF)
                if (tfile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2)
                {
                    var txxxRel = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "RELEASETYPE", false);
                    string? relText = txxxRel?.Text?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(relText))
                    {
                        var norm = NormalizeReleaseType(relText);
                        if (!string.IsNullOrEmpty(norm)) return norm;
                    }

                    var txxxMb = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Type", false);
                    string? mbText = txxxMb?.Text?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(mbText))
                    {
                        var norm = NormalizeReleaseType(mbText);
                        if (!string.IsNullOrEmpty(norm)) return norm;
                    }

                    var txxxAlbumType = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "ALBUMTYPE", false);
                    string? atText = txxxAlbumType?.Text?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(atText))
                    {
                        var norm = NormalizeReleaseType(atText);
                        if (!string.IsNullOrEmpty(norm)) return norm;
                    }
                }

                // 3. Check Apple / MP4 tags (M4A, AAC)
                if (tfile.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
                {
                    string? dashRel = apple.GetDashBox("com.apple.iTunes", "RELEASETYPE")
                                   ?? apple.GetDashBox("com.apple.iTunes", "MusicBrainz Album Type")
                                   ?? apple.GetDashBox("com.apple.iTunes", "ALBUMTYPE");

                    if (!string.IsNullOrWhiteSpace(dashRel))
                    {
                        var norm = NormalizeReleaseType(dashRel);
                        if (!string.IsNullOrEmpty(norm)) return norm;
                    }
                }
            }
            catch
            {
                // Ignore read errors and return empty
            }

            return string.Empty;
        }

        /// <summary>
        /// Writes the release type into the audio file's physical tags using industry standard containers.
        /// </summary>
        public static void SetReleaseTypeInTag(TagLib.File tfile, string releaseType)
        {
            if (tfile?.Tag == null)
                return;

            string canonical = NormalizeReleaseType(releaseType);
            if (string.IsNullOrEmpty(canonical))
                canonical = TypeAlbum;

            string tagValue = canonical switch
            {
                TypeSingle => "single",
                TypeEp => "ep",
                _ => "album"
            };

            try
            {
                // 1. Vorbis / Xiph comments
                if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                {
                    xiph.SetField("RELEASETYPE", tagValue);
                    xiph.SetField("MUSICBRAINZ_ALBUMTYPE", tagValue);
                }

                // 2. ID3v2 User Text frames
                if (tfile.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3v2)
                {
                    var txxxRel = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "RELEASETYPE", true);
                    if (txxxRel != null) txxxRel.Text = new[] { tagValue };

                    var txxxMb = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, "MusicBrainz Album Type", true);
                    if (txxxMb != null) txxxMb.Text = new[] { tagValue };
                }

                // 3. Apple / MP4
                if (tfile.GetTag(TagLib.TagTypes.Apple, true) is TagLib.Mpeg4.AppleTag apple)
                {
                    apple.SetDashBox("com.apple.iTunes", "RELEASETYPE", tagValue);
                    apple.SetDashBox("com.apple.iTunes", "MusicBrainz Album Type", tagValue);
                }
            }
            catch
            {
                // Silently ignore tag write issues for unsupported sub-formats
            }
        }

        /// <summary>
        /// Normalizes various string representations of release types into "Álbum", "EP", or "Sencillo".
        /// </summary>
        public static string NormalizeReleaseType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string trimmed = raw.Trim().ToLowerInvariant();

            if (trimmed.Contains("single") || trimmed.Contains("sencillo"))
                return TypeSingle;

            if (trimmed == "ep" || trimmed.Contains(" ep") || trimmed.Contains("ep ") || trimmed.Contains("- ep") || trimmed.Contains("(ep)"))
                return TypeEp;

            if (trimmed.Contains("album") || trimmed.Contains("álbum") || trimmed.Contains("lp") || trimmed.Contains("compilation"))
                return TypeAlbum;

            return TypeAlbum;
        }

        /// <summary>
        /// Intelligent release type detector when no physical tag is present.
        /// Analyzes album title suffixes (e.g. "- Single", "- EP") and track count.
        /// </summary>
        public static string DetectReleaseType(string? albumTitle, int trackCount = 0, string? existingTagType = null)
        {
            if (!string.IsNullOrWhiteSpace(existingTagType))
            {
                string norm = NormalizeReleaseType(existingTagType);
                if (!string.IsNullOrEmpty(norm)) return norm;
            }

            if (!string.IsNullOrWhiteSpace(albumTitle))
            {
                string title = albumTitle.Trim();
                if (Regex.IsMatch(title, @"(\b|_|-|\(|\[)single(\b|_|-|\)|\])", RegexOptions.IgnoreCase))
                    return TypeSingle;

                if (Regex.IsMatch(title, @"(\b|_|-|\(|\[)ep(\b|_|-|\)|\])", RegexOptions.IgnoreCase))
                    return TypeEp;
            }

            // Fallback heuristics based on track count
            if (trackCount == 1)
                return TypeSingle;

            if (trackCount >= 2 && trackCount <= 6)
                return TypeEp;

            return TypeAlbum;
        }
    }
}
