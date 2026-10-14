using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class PlaylistDetailViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        public Playlist CurrentPlaylist { get; }
        public int TotalSongs => Tracks.Count;
        public string TotalSongsText => $"PISTAS ({Tracks.Count})";
        public string SongCountText => $"{Tracks.Count} canciones";

        private string _totalLength = string.Empty;
        public string TotalLength
        {
            get => _totalLength;
            set { _totalLength = value; OnPropertyChanged(); }
        }

        public bool CanDelete => true;
        public Visibility DeleteVisibility => Visibility.Visible;

        public ObservableCollection<Track> Tracks { get; } = new();

        public ICommand GoBackCommand { get; }
        public ICommand PlayAllCommand { get; }
        public ICommand RemoveTrackCommand { get; }
        public ICommand DeletePlaylistCommand { get; }

        public PlaylistDetailViewModel(DatabaseService dbService, Playlist playlist)
        {
            _dbService = dbService;
            CurrentPlaylist = playlist;

            GoBackCommand = new RelayCommand(() =>
            {
                MainViewModel.Instance?.NavigateToPlaylists();
            });

            PlayAllCommand = new RelayCommand(async () =>
            {
                if (Tracks.Any())
                {
                    await MainViewModel.Instance!.PlayTrackAsync(Tracks.First(), Tracks.ToList());
                }
            });

            RemoveTrackCommand = new RelayCommand(async trackObj =>
            {
                if (trackObj is Track track)
                {
                    await _dbService.RemoveTrackFromPlaylistAsync(CurrentPlaylist.Name, track);
                    await LoadPlaylistDetailsAsync();
                    if (MainViewModel.Instance != null)
                    {
                        await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
                    }
                }
            });

            DeletePlaylistCommand = new RelayCommand(async () =>
            {
                if (!CanDelete) return;
                await _dbService.DeletePlaylistAsync(CurrentPlaylist.Id);
                if (MainViewModel.Instance != null)
                {
                    await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
                    MainViewModel.Instance.NavigateToPlaylists();
                }
            });

            _ = LoadPlaylistDetailsAsync();
        }

        public async Task ReorderTracksAsync()
        {
            for (int i = 0; i < Tracks.Count; i++)
            {
                Tracks[i].DisplayIndex = i + 1;
            }

            CurrentPlaylist.TrackIds = Tracks.Select(t => t.Id).ToList();
            CurrentPlaylist.TrackFilePaths = Tracks.Where(t => !string.IsNullOrEmpty(t.FilePath)).Select(t => t.FilePath).ToList();
            CurrentPlaylist.TrackCount = Tracks.Count;

            await _dbService.UpdatePlaylistTracksOrderAsync(CurrentPlaylist.Name, Tracks);

            TimeSpan totalSpan = TimeSpan.FromSeconds(Tracks.Sum(t => t.Duration.TotalSeconds));
            TotalLength = $"{(int)totalSpan.TotalMinutes}m {totalSpan.Seconds}s";

            OnPropertyChanged(nameof(TotalSongs));
            OnPropertyChanged(nameof(TotalSongsText));
            OnPropertyChanged(nameof(SongCountText));
            OnPropertyChanged(nameof(TotalLength));
        }

        public async Task RefreshDetailsAsync()
        {
            await LoadPlaylistDetailsAsync();
            OnPropertyChanged(nameof(CurrentPlaylist));
        }

        private async Task LoadPlaylistDetailsAsync()
        {
            var playlistTracks = await _dbService.GetTracksForPlaylistAsync(CurrentPlaylist.Name);

            RunOnUIThread(() =>
            {
                Tracks.Clear();
                for (int i = 0; i < playlistTracks.Count; i++)
                {
                    playlistTracks[i].DisplayIndex = i + 1;
                    Tracks.Add(playlistTracks[i]);
                }

                TimeSpan totalSpan = TimeSpan.FromSeconds(playlistTracks.Sum(t => t.Duration.TotalSeconds));
                TotalLength = $"{(int)totalSpan.TotalMinutes}m {totalSpan.Seconds}s";

                OnPropertyChanged(nameof(Tracks));
                OnPropertyChanged(nameof(TotalSongs));
                OnPropertyChanged(nameof(TotalSongsText));
                OnPropertyChanged(nameof(SongCountText));
                OnPropertyChanged(nameof(TotalLength));
                OnPropertyChanged(nameof(CurrentPlaylist));
            });
        }
    }
}

