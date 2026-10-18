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
    public class ArtistDetailViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        public string ArtistName { get; }

        private string _genre = string.Empty;
        public string Genre
        {
            get => _genre;
            set { _genre = value; OnPropertyChanged(); }
        }

        private string? _bannerImagePath;
        public string? BannerImagePath
        {
            get => _bannerImagePath;
            set { _bannerImagePath = value; OnPropertyChanged(); }
        }

        public int TotalAlbums => Albums.Count;
        public string TotalAlbumsText => $"ÁLBUMES ({Albums.Count})";
        public int TotalSongs => _allArtistTracks?.Count ?? 0;

        private string _totalLength = string.Empty;
        public string TotalLength
        {
            get => _totalLength;
            set { _totalLength = value; OnPropertyChanged(); }
        }

        public ObservableCollection<AlbumItem> Albums { get; } = new();
        public ObservableCollection<Track> SelectedAlbumTracks { get; } = new();

        private AlbumItem? _selectedAlbum;
        public AlbumItem? SelectedAlbum
        {
            get => _selectedAlbum;
            set
            {
                _selectedAlbum = value;
                OnPropertyChanged();
                LoadAlbumTracks(value?.Title);
            }
        }

        private ObservableCollection<Track>? _allArtistTracks;

        public ICommand GoBackCommand { get; }
        public ICommand PlayAlbumCommand { get; }
        public ICommand PlayAllArtistCommand { get; }

        public ArtistDetailViewModel(DatabaseService dbService, string artistName)
        {
            _dbService = dbService;
            ArtistName = artistName;

            GoBackCommand = new RelayCommand(() =>
            {
                MainViewModel.Instance?.NavigateToArtists();
            });

            PlayAlbumCommand = new RelayCommand(async () =>
            {
                if (SelectedAlbumTracks.Any())
                {
                    await MainViewModel.Instance!.PlayTrackAsync(SelectedAlbumTracks.First(), SelectedAlbumTracks.ToList());
                }
            });

            PlayAllArtistCommand = new RelayCommand(async () =>
            {
                if (_allArtistTracks != null && _allArtistTracks.Any())
                {
                    await MainViewModel.Instance!.PlayTrackAsync(_allArtistTracks.First(), _allArtistTracks.ToList());
                }
            });

            _ = LoadArtistDetailsAsync();
        }

        public async Task LoadArtistDetailsAsync()
        {
            var tracks = await _dbService.GetAllTracksAsync();
            var artistTracks = tracks.Where(t => Helpers.ArtistHelper.TrackHasArtist(t, ArtistName)).ToList();

            var uniqueAlbums = artistTracks.GroupBy(t => t.Album).Select(g => new AlbumItem
            {
                Title = g.Key,
                Artist = ArtistName,
                CoverImagePath = g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath
            }).OrderBy(a => a.Title).ToList();

            string genreStr = artistTracks.FirstOrDefault()?.Genre ?? "Unknown Genre";
            string? customPhoto = _dbService.GetArtistPhotoPath(ArtistName);
            string? banner = customPhoto ?? artistTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;

            TimeSpan totalSpan = TimeSpan.FromSeconds(artistTracks.Sum(t => t.Duration.TotalSeconds));
            string lengthStr = $"{(int)totalSpan.TotalMinutes}m {totalSpan.Seconds}s";

            RunOnUIThread(() =>
            {
                _allArtistTracks = new ObservableCollection<Track>(artistTracks);
                Genre = genreStr;
                BannerImagePath = banner;
                TotalLength = lengthStr;

                Albums.Clear();
                foreach (var album in uniqueAlbums)
                {
                    Albums.Add(album);
                }

                OnPropertyChanged(nameof(Albums));
                OnPropertyChanged(nameof(TotalAlbums));
                OnPropertyChanged(nameof(TotalAlbumsText));
                OnPropertyChanged(nameof(TotalSongs));

                if (Albums.Any())
                {
                    SelectedAlbum = Albums.First();
                }
            });
        }

        private void LoadAlbumTracks(string? albumTitle)
        {
            RunOnUIThread(() =>
            {
                SelectedAlbumTracks.Clear();
                if (string.IsNullOrEmpty(albumTitle) || _allArtistTracks == null) return;

                var tracks = _allArtistTracks.Where(t => t.Album == albumTitle).OrderBy(t => t.TrackNumber).ToList();
                foreach (var t in tracks)
                {
                    SelectedAlbumTracks.Add(t);
                }
                OnPropertyChanged(nameof(SelectedAlbumTracks));
            });
        }

        public void UpdateArtistPhoto(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !System.IO.File.Exists(sourceFilePath)) return;
            try
            {
                string savedPath = _dbService.SaveArtistPhoto(ArtistName, sourceFilePath);
                RunOnUIThread(() =>
                {
                    BannerImagePath = savedPath;
                    OnPropertyChanged(nameof(BannerImagePath));
                });

                // Notify global views (ArtistsView, etc.) in real time
                MainViewModel.Instance?.NotifyArtistPhotoChanged(ArtistName, savedPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistDetail] Error updating photo: {ex.Message}");
            }
        }
    }
}

