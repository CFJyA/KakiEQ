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
            set
            {
                _bannerImagePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BannerImageSource));
            }
        }

        public Microsoft.UI.Xaml.Media.ImageSource? BannerImageSource => ImageHelper.GetCachedHeroCover(_bannerImagePath);

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
        public ObservableCollection<Track> SelectedAlbumDisplayTracks { get; } = new();

        private AlbumItem? _selectedAlbum;
        public AlbumItem? SelectedAlbum
        {
            get => _selectedAlbum;
            set
            {
                if (!ReferenceEquals(_selectedAlbum, value))
                {
                    if (_selectedAlbum != null)
                        _selectedAlbum.IsSelected = false;
                    _selectedAlbum = value;
                    if (_selectedAlbum != null)
                        _selectedAlbum.IsSelected = true;
                }
                else if (_selectedAlbum != null)
                {
                    _selectedAlbum.IsSelected = true;
                }

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
            string? previouslySelectedAlbumTitle = SelectedAlbum?.Title;

            var (artistTracks, uniqueAlbums, genreStr, banner, lengthStr) = await Task.Run(async () =>
            {
                var tracks = await _dbService.GetAllTracksAsync();
                var aTracks = tracks.Where(t => Helpers.ArtistHelper.TrackHasArtist(t, ArtistName)).ToList();

                if (DiscMetadataHelper.EnsureTracksDiscLoaded(aTracks))
                {
                    await _dbService.InsertTracksAsync(tracks);
                }

                var uAlbums = aTracks.GroupBy(t => t.Album).Select(g => new AlbumItem
                {
                    Title = g.Key,
                    Artist = ArtistName,
                    CoverImagePath = g.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath
                }).OrderBy(a => a.Title).ToList();

                string gStr = aTracks.FirstOrDefault()?.Genre ?? "Unknown Genre";
                string? customPhoto = _dbService.GetArtistPhotoPath(ArtistName);
                string? bPath = customPhoto ?? aTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;

                TimeSpan totalSpan = TimeSpan.FromSeconds(aTracks.Sum(t => t.Duration.TotalSeconds));
                string lStr = $"{(int)totalSpan.TotalMinutes}m {totalSpan.Seconds}s";
                return (aTracks, uAlbums, gStr, bPath, lStr);
            });

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
                    var match = !string.IsNullOrEmpty(previouslySelectedAlbumTitle)
                        ? Albums.FirstOrDefault(a => a.Title.Equals(previouslySelectedAlbumTitle, StringComparison.OrdinalIgnoreCase))
                        : null;
                    SelectedAlbum = match ?? Albums.First();
                }
            });
        }

        private void LoadAlbumTracks(string? albumTitle)
        {
            RunOnUIThread(() =>
            {
                SelectedAlbumTracks.Clear();
                SelectedAlbumDisplayTracks.Clear();
                if (string.IsNullOrEmpty(albumTitle) || _allArtistTracks == null) return;

                var rawAlbumTracks = _allArtistTracks.Where(t => t.Album == albumTitle).ToList();
                var (orderedTracks, displayTracks) = DiscMetadataHelper.BuildAlbumTrackLists(rawAlbumTracks);

                foreach (var t in orderedTracks)
                {
                    SelectedAlbumTracks.Add(t);
                }
                foreach (var item in displayTracks)
                {
                    SelectedAlbumDisplayTracks.Add(item);
                }

                OnPropertyChanged(nameof(SelectedAlbumTracks));
                OnPropertyChanged(nameof(SelectedAlbumDisplayTracks));

                HarmonicAnalysisHelper.EnqueueTracksForAnalysis(orderedTracks, _dbService, MainWindow.Instance?.DispatcherQueue);
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

