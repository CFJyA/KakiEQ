using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class AlbumDetailViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        public string AlbumTitle { get; }

        private string _artistName = string.Empty;
        public string ArtistName
        {
            get => _artistName;
            set { _artistName = value; OnPropertyChanged(); }
        }

        private string? _coverImagePath;
        public string? CoverImagePath
        {
            get => _coverImagePath;
            set { _coverImagePath = value; OnPropertyChanged(); }
        }

        public int TotalSongs => Tracks.Count;
        public string TotalSongsText => $"PISTAS ({Tracks.Count})";

        private string _totalLength = string.Empty;
        public string TotalLength
        {
            get => _totalLength;
            set { _totalLength = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Track> Tracks { get; } = new();
        public ObservableCollection<AlbumItem> MoreByArtist { get; } = new();

        public ICommand GoBackCommand { get; }
        public ICommand PlayAllCommand { get; }

        public AlbumDetailViewModel(DatabaseService dbService, string albumTitle, string? artistName = null)
        {
            _dbService = dbService;
            AlbumTitle = albumTitle;
            if (!string.IsNullOrEmpty(artistName))
            {
                _artistName = artistName;
            }

            GoBackCommand = new RelayCommand(() =>
            {
                MainViewModel.Instance?.NavigateToAlbums();
            });

            PlayAllCommand = new RelayCommand(async () =>
            {
                if (Tracks.Any())
                {
                    await MainViewModel.Instance!.PlayTrackAsync(Tracks.First(), Tracks.ToList());
                }
            });

            _ = LoadAlbumDetailsAsync();
        }

        public async Task LoadAlbumDetailsAsync()
        {
            var allTracks = await _dbService.GetAllTracksAsync();

            var albumTracks = allTracks.Where(t => t.Album.Equals(AlbumTitle, StringComparison.OrdinalIgnoreCase) &&
                                                   (string.IsNullOrEmpty(_artistName) || ArtistHelper.TrackHasArtist(t, _artistName)))
                                       .OrderBy(t => t.TrackNumber).ToList();
            if (albumTracks.Count == 0)
            {
                albumTracks = allTracks.Where(t => t.Album.Equals(AlbumTitle, StringComparison.OrdinalIgnoreCase))
                                       .OrderBy(t => t.TrackNumber).ToList();
            }
            var firstTrack = albumTracks.FirstOrDefault();

            string artist = firstTrack?.Artist ?? string.Empty;
            string? cover = firstTrack?.CoverImagePath;

            TimeSpan totalSpan = TimeSpan.FromSeconds(albumTracks.Sum(t => t.Duration.TotalSeconds));
            string lengthStr = $"{(int)totalSpan.TotalMinutes}m {totalSpan.Seconds}s";

            var artistTracks = allTracks.Where(t => t.Artist == artist && t.Album != AlbumTitle);
            var uniqueAlbums = artistTracks.GroupBy(t => t.Album).Select(g => new AlbumItem
            {
                Title = g.Key,
                Artist = artist,
                CoverImagePath = g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath
            }).OrderBy(a => a.Title).ToList();

            RunOnUIThread(() =>
            {
                ArtistName = artist;
                CoverImagePath = cover;
                TotalLength = lengthStr;

                Tracks.Clear();
                foreach (var t in albumTracks)
                {
                    Tracks.Add(t);
                }

                MoreByArtist.Clear();
                foreach (var a in uniqueAlbums)
                {
                    MoreByArtist.Add(a);
                }

                OnPropertyChanged(nameof(Tracks));
                OnPropertyChanged(nameof(TotalSongs));
                OnPropertyChanged(nameof(TotalSongsText));
            });
        }
    }
}

