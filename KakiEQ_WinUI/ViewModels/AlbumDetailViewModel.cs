using System;
using System.Collections.Generic;
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

        private string _releaseType = "Álbum";
        public string ReleaseType
        {
            get => _releaseType;
            set
            {
                _releaseType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReleaseTypeBadgeText));
            }
        }

        public string ReleaseTypeBadgeText => _releaseType.ToUpperInvariant();

        private string? _coverImagePath;
        public string? CoverImagePath
        {
            get => _coverImagePath;
            set
            {
                _coverImagePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CoverHeroSource));
            }
        }

        public Microsoft.UI.Xaml.Media.ImageSource? CoverHeroSource => ImageHelper.GetCachedHeroCover(_coverImagePath);

        public int TotalSongs => Tracks.Count;
        public string TotalSongsText => $"PISTAS ({Tracks.Count})";

        private string _totalLength = string.Empty;
        public string TotalLength
        {
            get => _totalLength;
            set { _totalLength = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Track> Tracks { get; } = new();
        public ObservableCollection<Track> DisplayTracks { get; } = new();
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
            var (orderedTracks, displayTracks, artist, cover, lengthStr, uniqueAlbums, relType) = await Task.Run(async () =>
            {
                var allTracks = await _dbService.GetAllTracksAsync();
                var allAlbums = AlbumHelper.BuildAlbumItems(allTracks);
                var matchedAlbum = AlbumHelper.FindAlbumItem(allAlbums, AlbumTitle, _artistName);

                var aTracks = matchedAlbum?.Tracks.ToList() ?? new List<Track>();

                if (aTracks.Count > 0 && DiscMetadataHelper.EnsureTracksDiscLoaded(aTracks))
                {
                    await _dbService.InsertTracksAsync(allTracks);
                }

                var (ordTracks, dispTracks) = DiscMetadataHelper.BuildAlbumTrackLists(aTracks);

                string art = !string.IsNullOrWhiteSpace(matchedAlbum?.Artist)
                    ? matchedAlbum.Artist
                    : (!string.IsNullOrWhiteSpace(_artistName) ? _artistName : string.Empty);

                string? cov = matchedAlbum?.CoverImagePath
                           ?? ordTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;
                string relType = matchedAlbum?.ReleaseType
                              ?? ReleaseTypeHelper.DetectReleaseType(AlbumTitle, ordTracks.Count, ordTracks.FirstOrDefault()?.ReleaseType);

                TimeSpan totalSpan = TimeSpan.FromSeconds(ordTracks.Sum(t => t.Duration.TotalSeconds));
                string lStr = $"{(int)totalSpan.TotalMinutes}m {totalSpan.Seconds}s";

                // Collaborating artists support for "Más de este artista"
                var albumArtists = ArtistHelper.SplitArtists(art);
                var uAlbums = allAlbums
                    .Where(a => !AlbumHelper.IsSameAlbum(a, matchedAlbum) &&
                                albumArtists.Any(singleArt => AlbumHelper.AlbumBelongsToArtist(a, singleArt)))
                    .ToList();

                return (ordTracks, dispTracks, art, cov, lStr, uAlbums, relType);
            });

            RunOnUIThread(() =>
            {
                ArtistName = artist;
                CoverImagePath = cover;
                TotalLength = lengthStr;
                ReleaseType = uniqueAlbums != null ? relType : ReleaseTypeHelper.TypeAlbum;

                Tracks.Clear();
                foreach (var t in orderedTracks)
                {
                    Tracks.Add(t);
                }

                DisplayTracks.Clear();
                foreach (var item in displayTracks)
                {
                    DisplayTracks.Add(item);
                }

                MoreByArtist.Clear();
                if (uniqueAlbums != null)
                {
                    foreach (var a in uniqueAlbums)
                    {
                        MoreByArtist.Add(a);
                    }
                }

                OnPropertyChanged(nameof(Tracks));
                OnPropertyChanged(nameof(DisplayTracks));
                OnPropertyChanged(nameof(TotalSongs));
                OnPropertyChanged(nameof(TotalSongsText));
            });

            HarmonicAnalysisHelper.EnqueueTracksForAnalysis(orderedTracks, _dbService, MainWindow.Instance?.DispatcherQueue);
        }
    }
}

