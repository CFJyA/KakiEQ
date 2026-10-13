using System;
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

        public ObservableCollection<ArtistItem> Artists { get; } = new();

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
                Artists.Clear();
                foreach (var artist in uniqueArtists)
                {
                    Artists.Add(artist);
                }
                OnPropertyChanged(nameof(Artists));
            });
        }
    }
}

