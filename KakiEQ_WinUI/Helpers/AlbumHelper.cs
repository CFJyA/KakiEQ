using System;
using System.Collections.Generic;
using System.Linq;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Helpers
{
    /// <summary>
    /// Centralizes case-sensitive album grouping and artist separation across views, viewmodels, and the metadata editor.
    /// </summary>
    public static class AlbumHelper
    {
        private sealed class AlbumCluster
        {
            public List<Track> Tracks { get; } = new();
            public HashSet<string> KeyArtists { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string CanonicalKey { get; set; } = string.Empty;
            public string PrimaryArtist { get; set; } = string.Empty;
            public bool HasExplicitAlbumArtist { get; set; }
        }

        /// <summary>
        /// Groups tracks into distinct <see cref="AlbumItem"/> instances using case-sensitive album titles
        /// and artist clustering so homonymous albums by different artists or casing never mix.
        /// </summary>
        public static List<AlbumItem> BuildAlbumItems(IEnumerable<Track> tracks)
        {
            if (tracks == null) return new List<AlbumItem>();

            var result = new List<AlbumItem>();

            var titleGroups = tracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Album))
                .GroupBy(t => t.Album.Trim(), StringComparer.Ordinal);

            foreach (var titleGroup in titleGroups)
            {
                string albumTitle = titleGroup.Key;
                var clusters = ClusterTracksWithinAlbum(titleGroup);

                foreach (var cluster in clusters)
                {
                    var (orderedTracks, _) = DiscMetadataHelper.BuildAlbumTrackLists(cluster.Tracks);
                    string albumArtist = ResolveClusterArtist(cluster, orderedTracks);
                    string? coverPath = orderedTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;

                    string? explicitReleaseType = orderedTracks
                        .FirstOrDefault(t => !string.IsNullOrEmpty(t.ReleaseType) && t.ReleaseType != ReleaseTypeHelper.TypeAlbum)
                        ?.ReleaseType;

                    string dominantReleaseType = explicitReleaseType
                        ?? ReleaseTypeHelper.DetectReleaseType(albumTitle, orderedTracks.Count, orderedTracks.FirstOrDefault()?.ReleaseType);

                    result.Add(new AlbumItem
                    {
                        Title = albumTitle,
                        Artist = albumArtist,
                        CoverImagePath = coverPath,
                        ReleaseType = dominantReleaseType,
                        TrackCount = orderedTracks.Count,
                        Tracks = orderedTracks
                    });
                }
            }

            return result
                .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.Title, StringComparer.Ordinal)
                .ThenBy(a => a.Artist, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Finds the specific <see cref="AlbumItem"/> matching a case-sensitive album title and optional artist context.
        /// </summary>
        public static AlbumItem? FindAlbumItem(IEnumerable<Track> tracks, string? albumTitle, string? artistName = null)
        {
            if (tracks == null || string.IsNullOrWhiteSpace(albumTitle))
                return null;

            return FindAlbumItem(BuildAlbumItems(tracks), albumTitle, artistName);
        }

        /// <summary>
        /// Finds the specific <see cref="AlbumItem"/> inside pre-built album items matching a case-sensitive title and optional artist context.
        /// </summary>
        public static AlbumItem? FindAlbumItem(IReadOnlyList<AlbumItem> albumItems, string? albumTitle, string? artistName = null)
        {
            if (albumItems == null || albumItems.Count == 0 || string.IsNullOrWhiteSpace(albumTitle))
                return null;

            string normalizedTitle = albumTitle.Trim();
            var titleMatches = albumItems
                .Where(a => string.Equals(a.Title, normalizedTitle, StringComparison.Ordinal))
                .ToList();

            if (titleMatches.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(artistName))
                return titleMatches[0];

            string normalizedArtist = artistName.Trim();

            var exactMatch = titleMatches.FirstOrDefault(a => ArtistStringsMatch(a.Artist, normalizedArtist));
            if (exactMatch != null)
                return exactMatch;

            var targetArtists = ArtistHelper.SplitArtists(normalizedArtist);
            if (targetArtists.Count > 0)
            {
                var allTargetMatch = titleMatches.FirstOrDefault(a =>
                    targetArtists.All(target =>
                        ArtistHelper.SplitArtists(a.Artist).Any(aa => aa.Equals(target, StringComparison.OrdinalIgnoreCase)) ||
                        a.Tracks.Any(t => ArtistHelper.TrackHasArtist(t, target))));
                if (allTargetMatch != null)
                    return allTargetMatch;

                var anyTargetMatch = titleMatches.FirstOrDefault(a =>
                    targetArtists.Any(target =>
                        ArtistHelper.SplitArtists(a.Artist).Any(aa => aa.Equals(target, StringComparison.OrdinalIgnoreCase)) ||
                        a.Tracks.Any(t => ArtistHelper.TrackHasArtist(t, target))));
                if (anyTargetMatch != null)
                    return anyTargetMatch;
            }

            return null;
        }

        /// <summary>
        /// Compares two artist strings order-insensitively across all separated artists.
        /// </summary>
        public static bool ArtistStringsMatch(string? artistA, string? artistB)
        {
            var listA = string.IsNullOrWhiteSpace(artistA) ? new List<string>() : ArtistHelper.SplitArtists(artistA);
            var listB = string.IsNullOrWhiteSpace(artistB) ? new List<string>() : ArtistHelper.SplitArtists(artistB);

            if (listA.Count == 0 && listB.Count == 0) return true;
            if (listA.Count == 0 || listB.Count == 0) return false;

            var setA = new HashSet<string>(listA, StringComparer.OrdinalIgnoreCase);
            var setB = new HashSet<string>(listB, StringComparer.OrdinalIgnoreCase);
            return setA.SetEquals(setB);
        }

        /// <summary>
        /// Returns true if the album is credited to or features the specified artist.
        /// </summary>
        public static bool AlbumBelongsToArtist(AlbumItem? album, string? artistName)
        {
            if (album == null || string.IsNullOrWhiteSpace(artistName))
                return false;

            string trimmedArtist = artistName.Trim();
            if (ArtistHelper.SplitArtists(album.Artist).Any(a => a.Equals(trimmedArtist, StringComparison.OrdinalIgnoreCase)))
                return true;

            return album.Tracks.Any(t => ArtistHelper.TrackHasArtist(t, trimmedArtist));
        }

        /// <summary>
        /// Returns true when two <see cref="AlbumItem"/> instances refer to the same release (exact title and matching artist).
        /// </summary>
        public static bool IsSameAlbum(AlbumItem? a, AlbumItem? b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.Title?.Trim(), b.Title?.Trim(), StringComparison.Ordinal) &&
                   ArtistStringsMatch(a.Artist, b.Artist);
        }

        private static List<AlbumCluster> ClusterTracksWithinAlbum(IEnumerable<Track> tracksInAlbum)
        {
            var explicitClusters = new List<AlbumCluster>();
            var explicitByKey = new Dictionary<string, AlbumCluster>(StringComparer.OrdinalIgnoreCase);
            var untaggedTracks = new List<Track>();

            // Pass 1: Cluster tracks with explicit AlbumArtist by order-insensitive artist set.
            foreach (var track in tracksInAlbum)
            {
                if (!string.IsNullOrWhiteSpace(track.AlbumArtist))
                {
                    var artists = ExtractMeaningfulArtists(track.AlbumArtist);
                    string key = BuildCanonicalArtistKey(artists);
                    if (!string.IsNullOrEmpty(key))
                    {
                        if (!explicitByKey.TryGetValue(key, out var cluster))
                        {
                            cluster = CreateCluster(artists, key, hasExplicitAlbumArtist: true);
                            explicitByKey[key] = cluster;
                            explicitClusters.Add(cluster);
                        }
                        cluster.Tracks.Add(track);
                        continue;
                    }
                }

                untaggedTracks.Add(track);
            }

            // Pass 2: Attach tracks with empty AlbumArtist that belong to an explicit AlbumArtist cluster
            // (exact artist-set match, or track artists are a superset including featured guests).
            var remainingUntagged = new List<Track>();
            foreach (var track in untaggedTracks)
            {
                var trackArtists = ExtractMeaningfulArtists(track.Artist);
                var trackSet = new HashSet<string>(trackArtists, StringComparer.OrdinalIgnoreCase);
                string trackPrimary = trackArtists.FirstOrDefault() ?? string.Empty;

                AlbumCluster? match = null;
                if (trackSet.Count > 0 && explicitClusters.Count > 0)
                {
                    var matchingClusters = explicitClusters
                        .Where(c => c.KeyArtists.IsSubsetOf(trackSet))
                        .ToList();

                    if (matchingClusters.Count == 1)
                    {
                        match = matchingClusters[0];
                    }
                    else if (matchingClusters.Count > 1)
                    {
                        // Prefer exact set match, then primary artist match, then largest matching cluster.
                        match = matchingClusters.FirstOrDefault(c => c.KeyArtists.SetEquals(trackSet))
                             ?? matchingClusters.FirstOrDefault(c =>
                                    !string.IsNullOrEmpty(trackPrimary) &&
                                    c.PrimaryArtist.Equals(trackPrimary, StringComparison.OrdinalIgnoreCase))
                             ?? matchingClusters
                                    .OrderByDescending(c => c.KeyArtists.Count)
                                    .ThenByDescending(c => c.Tracks.Count)
                                    .FirstOrDefault();
                    }
                }

                if (match != null)
                {
                    match.Tracks.Add(track);
                }
                else
                {
                    remainingUntagged.Add(track);
                }
            }

            if (remainingUntagged.Count == 0)
                return explicitClusters;

            // Pass 3: Group remaining tracks without explicit AlbumArtist.
            var untaggedByKey = new Dictionary<string, AlbumCluster>(StringComparer.OrdinalIgnoreCase);
            var untaggedBuckets = new List<AlbumCluster>();

            foreach (var track in remainingUntagged)
            {
                var artists = ExtractMeaningfulArtists(track.Artist);
                string key = BuildCanonicalArtistKey(artists);
                if (!untaggedByKey.TryGetValue(key, out var bucket))
                {
                    bucket = CreateCluster(artists, key, hasExplicitAlbumArtist: false);
                    untaggedByKey[key] = bucket;
                    untaggedBuckets.Add(bucket);
                }
                bucket.Tracks.Add(track);
            }

            // 3a. Merge buckets sharing the exact same primary artist when no explicit AlbumArtist separates them
            // (keeps "Artist" + "Artist; Guest" together under one album, e.g. "Dañado" by Ivan Cornejo).
            var mergedUntagged = new List<AlbumCluster>();
            foreach (var bucket in untaggedBuckets)
            {
                if (!string.IsNullOrEmpty(bucket.PrimaryArtist))
                {
                    var existingSamePrimary = mergedUntagged.FirstOrDefault(c =>
                        c.PrimaryArtist.Equals(bucket.PrimaryArtist, StringComparison.OrdinalIgnoreCase));

                    if (existingSamePrimary != null)
                    {
                        existingSamePrimary.Tracks.AddRange(bucket.Tracks);
                        continue;
                    }
                }

                mergedUntagged.Add(bucket);
            }

            // 3b. Merge a smaller solo bucket (e.g., an un-tagged interlude by Artist B) when its artist
            // appears on every track of a single larger multi-artist bucket (e.g., Artist A; Artist B).
            var finalUntagged = new List<AlbumCluster>(mergedUntagged);
            foreach (var candidate in mergedUntagged.Where(b => b.KeyArtists.Count == 1).ToList())
            {
                var supersets = finalUntagged
                    .Where(other => !ReferenceEquals(other, candidate) &&
                                    other.KeyArtists.Count > 1 &&
                                    candidate.KeyArtists.IsProperSubsetOf(other.KeyArtists) &&
                                    other.Tracks.Count > candidate.Tracks.Count &&
                                    other.Tracks.Count >= 2 &&
                                    candidate.KeyArtists.All(a => other.Tracks.All(t => ArtistHelper.TrackHasArtist(t, a))))
                    .ToList();

                if (supersets.Count == 1)
                {
                    supersets[0].Tracks.AddRange(candidate.Tracks);
                    finalUntagged.Remove(candidate);
                }
            }

            explicitClusters.AddRange(finalUntagged);
            return explicitClusters;
        }

        private static AlbumCluster CreateCluster(List<string> artists, string key, bool hasExplicitAlbumArtist)
        {
            var cluster = new AlbumCluster
            {
                CanonicalKey = key,
                PrimaryArtist = artists.FirstOrDefault() ?? string.Empty,
                HasExplicitAlbumArtist = hasExplicitAlbumArtist
            };

            foreach (var artist in artists)
            {
                cluster.KeyArtists.Add(artist);
            }

            return cluster;
        }

        private static List<string> ExtractMeaningfulArtists(string? rawArtist)
        {
            return ArtistHelper.SplitArtists(rawArtist ?? string.Empty)
                .Where(a => !string.IsNullOrWhiteSpace(a) &&
                            !a.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static string BuildCanonicalArtistKey(List<string> artists)
        {
            if (artists.Count == 0) return string.Empty;
            return string.Join("\u001F", artists
                .Select(a => a.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal));
        }

        private static string ResolveClusterArtist(AlbumCluster cluster, List<Track> orderedTracks)
        {
            var explicitArtists = orderedTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.AlbumArtist))
                .Select(t => ArtistHelper.NormalizeArtistString(t.AlbumArtist))
                .Where(a => !string.IsNullOrWhiteSpace(a) &&
                            !a.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (explicitArtists.Count > 0)
            {
                return explicitArtists
                    .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .First().Key;
            }

            var normalizedTrackArtists = orderedTracks
                .Select(t => ArtistHelper.NormalizeArtistString(t.Artist ?? string.Empty))
                .Where(a => !string.IsNullOrWhiteSpace(a) &&
                            !a.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (normalizedTrackArtists.Count == 0)
                return "Desconocido";

            var groupedArtistStrings = normalizedTrackArtists
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (groupedArtistStrings.Count == 1)
                return groupedArtistStrings[0].Key;

            // Keep artists common to every track in the cluster (drops guest features like "; Eslabon Armado").
            var mostFrequentArtists = ArtistHelper.SplitArtists(groupedArtistStrings[0].Key);
            var commonArtists = mostFrequentArtists
                .Where(a => orderedTracks.All(t => ArtistHelper.TrackHasArtist(t, a)))
                .ToList();

            if (commonArtists.Count > 0)
                return string.Join("; ", commonArtists);

            var allIndividualArtists = orderedTracks
                .SelectMany(t => ExtractMeaningfulArtists(t.Artist))
                .ToList();

            return allIndividualArtists
                .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key
                ?? groupedArtistStrings[0].Key;
        }
    }
}
