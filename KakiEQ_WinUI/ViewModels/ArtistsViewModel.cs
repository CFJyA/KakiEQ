using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class ArtistsViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;
        private readonly List<ArtistItem> _allArtists = new();
        private string _searchQuery = string.Empty;

        public ObservableCollection<ArtistItem> Artists { get; } = new();

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    FilterArtists();
                }
            }
        }

        public string TotalArtistsCountFormatted => $"{Artists.Count} {(Artists.Count == 1 ? "artista" : "artistas")}";

        public ArtistsViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            _ = LoadArtistsAsync();
        }

        public async Task LoadArtistsAsync()
        {
            var tracks = await _dbService.GetAllTracksAsync();
            var artistTracksMap = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in tracks)
            {
                var artists = Helpers.ArtistHelper.SplitArtists(track.Artist);
                foreach (var artist in artists)
                {
                    if (!artistTracksMap.TryGetValue(artist, out var list))
                    {
                        list = new List<Track>();
                        artistTracksMap[artist] = list;
                    }
                    list.Add(track);
                }
            }

            var uniqueArtists = new List<ArtistItem>();
            foreach (var kvp in artistTracksMap)
            {
                string artistName = kvp.Key;
                var aTracks = kvp.Value;
                string? customPhoto = _dbService.GetArtistPhotoPath(artistName);
                string? cover = customPhoto ?? aTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;

                uniqueArtists.Add(new ArtistItem
                {
                    Name = artistName,
                    CoverImagePath = cover,
                    TrackCount = aTracks.Count,
                    AlbumCount = aTracks.Select(t => t.Album).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                });
            }

            uniqueArtists = uniqueArtists.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

            RunOnUIThread(() =>
            {
                _allArtists.Clear();
                _allArtists.AddRange(uniqueArtists);
                FilterArtists();
            });
        }

        private void FilterArtists()
        {
            string query = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(query)
                ? _allArtists
                : _allArtists.Where(a => a.Name != null && a.Name.ToLowerInvariant().Contains(query)).ToList();

            Artists.Clear();
            foreach (var artist in filtered)
            {
                Artists.Add(artist);
            }
            OnPropertyChanged(nameof(TotalArtistsCountFormatted));
        }

        public void NotifyArtistPhotoChanged(string artistName, string photoPath)
        {
            if (string.IsNullOrWhiteSpace(artistName)) return;

            RunOnUIThread(() =>
            {
                var match = _allArtists.FirstOrDefault(a => a.Name.Equals(artistName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.CoverImagePath = photoPath;
                }

                var visibleMatch = Artists.FirstOrDefault(a => a.Name.Equals(artistName, StringComparison.OrdinalIgnoreCase));
                if (visibleMatch != null)
                {
                    visibleMatch.CoverImagePath = photoPath;
                }
            });
        }
    }
}
