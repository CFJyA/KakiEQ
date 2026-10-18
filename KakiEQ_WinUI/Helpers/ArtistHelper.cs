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
    }
}
