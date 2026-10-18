using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Helpers
{
    public static class ArtistHelper
    {
        private static readonly Regex SplitRegex = new(@"\s*;\s*", RegexOptions.Compiled);

        /// <summary>
        /// Splits an artist string separated by ';' with any surrounding whitespace variations:
        /// "artista 1; artista 2", "artista 1;artista 2", "artista 1 ;artista 2", "artista 1 ; artista 2"
        /// </summary>
        public static List<string> SplitArtists(string? artistString)
        {
            if (string.IsNullOrWhiteSpace(artistString))
            {
                return new List<string> { "Unknown Artist" };
            }

            var parts = SplitRegex.Split(artistString)
                                  .Select(a => a.Trim())
                                  .Where(a => !string.IsNullOrEmpty(a))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

            return parts.Count > 0 ? parts : new List<string> { artistString.Trim() };
        }

        /// <summary>
        /// Checks if a track's artist field contains the target artist name as one of its individual artists.
        /// </summary>
        public static bool TrackHasArtist(Track track, string targetArtist)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.Artist) || string.IsNullOrWhiteSpace(targetArtist))
                return false;

            var target = targetArtist.Trim();
            var artists = SplitArtists(track.Artist);
            return artists.Any(a => a.Equals(target, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Formats multiple artists consistently with "; " separator.
        /// </summary>
        public static string NormalizeArtistString(string? artistString)
        {
            var list = SplitArtists(artistString);
            return string.Join("; ", list);
        }

        /// <summary>
        /// Extracts all artists/performers from a TagLib tag preserving multiple artists separated by ';'.
        /// Never discards collaborating artists after the first semicolon.
        /// </summary>
        public static string GetArtistsFromTag(TagLib.Tag? tag)
        {
            if (tag == null) return "Unknown Artist";

            if (tag.Performers != null && tag.Performers.Length > 0)
            {
                var validPerformers = tag.Performers
                    .SelectMany(p => SplitArtists(p))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (validPerformers.Count > 0)
                {
                    return string.Join("; ", validPerformers);
                }
            }

            if (!string.IsNullOrWhiteSpace(tag.JoinedPerformers))
            {
                return NormalizeArtistString(tag.JoinedPerformers);
            }

            if (tag.AlbumArtists != null && tag.AlbumArtists.Length > 0)
            {
                var validAlbumArtists = tag.AlbumArtists
                    .SelectMany(a => SplitArtists(a))
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (validAlbumArtists.Count > 0)
                {
                    return string.Join("; ", validAlbumArtists);
                }
            }

            if (!string.IsNullOrWhiteSpace(tag.JoinedAlbumArtists))
            {
                return NormalizeArtistString(tag.JoinedAlbumArtists);
            }

            if (!string.IsNullOrWhiteSpace(tag.FirstPerformer))
            {
                return tag.FirstPerformer.Trim();
            }

            if (!string.IsNullOrWhiteSpace(tag.FirstAlbumArtist))
            {
                return tag.FirstAlbumArtist.Trim();
            }

            return "Unknown Artist";
        }
    }
}
