using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class AlbumsViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;
        private readonly List<AlbumItem> _allAlbums = new();
        private string _searchQuery = string.Empty;

        public ObservableCollection<AlbumItem> Albums { get; } = new();

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    FilterAlbums();
                }
            }
        }

        public string TotalAlbumsCountFormatted => $"{Albums.Count} {(Albums.Count == 1 ? "álbum" : "álbumes")}";

        public AlbumsViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            _ = LoadAlbumsAsync();
        }

        private async Task LoadAlbumsAsync()
        {
            var tracks = await _dbService.GetAllTracksAsync();
            var uniqueAlbums = tracks.GroupBy(t => t.Album).Select(g =>
            {
                var coverPath = g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;
                return new AlbumItem
                {
                    Title = g.Key,
                    Artist = g.First().Artist,
                    CoverImagePath = coverPath,
                    DominantColorBrush = ColorHelper.GetDefaultBrush()
                };
            }).OrderBy(a => a.Title).ToList();

            RunOnUIThread(() =>
            {
                _allAlbums.Clear();
                _allAlbums.AddRange(uniqueAlbums);
                FilterAlbums();
            });
        }

        private void FilterAlbums()
        {
            string query = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(query)
                ? _allAlbums
                : _allAlbums.Where(a => 
                    (a.Title != null && a.Title.ToLowerInvariant().Contains(query)) ||
                    (a.Artist != null && a.Artist.ToLowerInvariant().Contains(query))).ToList();

            Albums.Clear();
            foreach (var album in filtered)
            {
                Albums.Add(album);
            }
            OnPropertyChanged(nameof(TotalAlbumsCountFormatted));
        }
    }
}
