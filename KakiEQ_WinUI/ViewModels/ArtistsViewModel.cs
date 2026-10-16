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

        private async Task LoadArtistsAsync()
        {
            var tracks = await _dbService.GetAllTracksAsync();
            var uniqueArtists = tracks.GroupBy(t => t.Artist).Select(g => new ArtistItem
            {
                Name = g.Key,
                CoverImagePath = g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath,
                TrackCount = g.Count(),
                AlbumCount = g.Select(t => t.Album).Distinct().Count()
            }).OrderBy(a => a.Name).ToList();

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
    }
}
