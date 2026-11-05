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

        public async Task LoadAlbumsAsync()
        {
            var uniqueAlbums = await Task.Run(async () =>
            {
                var tracks = await _dbService.GetAllTracksAsync();

                var result = new List<AlbumItem>();
                var albumGroups = tracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.Album))
                    .GroupBy(t => t.Album.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (var group in albumGroups)
                {
                    var allArtists = group.SelectMany(t => ArtistHelper.SplitArtists(t.Artist)).ToList();

                    string dominantArtist = allArtists
                        .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(ag => ag.Count())
                        .FirstOrDefault()?.Key
                        ?? group.FirstOrDefault()?.Artist
                        ?? "Desconocido";

                    var matchingTracks = group.Where(t => ArtistHelper.TrackHasArtist(t, dominantArtist)).ToList();
                    var otherTracks = group.Where(t => !ArtistHelper.TrackHasArtist(t, dominantArtist)).ToList();

                    var coverPath = matchingTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath
                                 ?? group.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;

                    result.Add(new AlbumItem
                    {
                        Title = group.Key,
                        Artist = dominantArtist,
                        CoverImagePath = coverPath
                    });

                    if (otherTracks.Count > 0)
                    {
                        var otherGroups = otherTracks.GroupBy(t => ArtistHelper.SplitArtists(t.Artist).FirstOrDefault() ?? t.Artist.Trim(), StringComparer.OrdinalIgnoreCase);
                        foreach (var og in otherGroups)
                        {
                            var otherCover = og.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;
                            result.Add(new AlbumItem
                            {
                                Title = group.Key,
                                Artist = og.Key,
                                CoverImagePath = otherCover
                            });
                        }
                    }
                }

                return result.OrderBy(a => a.Title).ToList();
            });

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
