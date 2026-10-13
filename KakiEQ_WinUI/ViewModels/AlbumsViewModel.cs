using System;
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

        public ObservableCollection<AlbumItem> Albums { get; } = new();

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
                Albums.Clear();
                foreach (var album in uniqueAlbums)
                {
                    Albums.Add(album);
                }
                OnPropertyChanged(nameof(Albums));
            });
        }
    }
}

