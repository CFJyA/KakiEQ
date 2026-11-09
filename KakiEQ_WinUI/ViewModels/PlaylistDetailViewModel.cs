using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public enum PlaylistSortMode
    {
        Custom,
        Bpm,
        Harmonic
    }

    public class PlaylistDetailViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;
        private readonly List<Track> _originalCustomTracks = new();
        private bool _bpmSortAscending = true;
        private bool _isSorting;

        public Playlist CurrentPlaylist { get; }
        public int TotalSongs => Tracks.Count;
        public string TotalSongsText => $"PISTAS ({Tracks.Count})";
        public string SongCountText => $"{Tracks.Count} canciones";

        private PlaylistSortMode _activeSortMode = PlaylistSortMode.Custom;
        public PlaylistSortMode ActiveSortMode
        {
            get => _activeSortMode;
            set
            {
                if (_activeSortMode != value)
                {
                    _activeSortMode = value;
                    OnPropertyChanged();
                    RefreshSortVisualProperties();
                }
            }
        }

        private string _sortByBpmLabel = "BPM";
        public string SortByBpmLabel
        {
            get => _sortByBpmLabel;
            set { _sortByBpmLabel = value; OnPropertyChanged(); }
        }

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
        public ICommand SortCustomCommand { get; }
        public ICommand SortByBpmCommand { get; }
        public ICommand SortHarmonicallyCommand { get; }

        // --- Uniform Theme Brushes for Sort Buttons ---
        private static bool IsLight => MainViewModel.Instance?.IsLightTheme == true;

        private static SolidColorBrush ActiveBgBrush => IsLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 237, 231, 246)) // #EDE7F6
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 30, 114));   // #381E72

        private static SolidColorBrush ActiveBorderBrush => IsLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 124, 77, 255))  // #7C4DFF
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252)); // #BB86FC

        private static SolidColorBrush ActiveFgBrush => IsLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 20, 140))   // #4A148C
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 208, 188, 255)); // #D0BCFF

        private static SolidColorBrush InactiveBgBrush => IsLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 240, 244)) // #F0F0F4
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 34, 40));    // #222228

        private static SolidColorBrush InactiveBorderBrush => IsLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 214, 214, 214)) // #D6D6D6
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 48, 58));    // #30303A

        private static SolidColorBrush InactiveFgBrush => IsLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 85, 85))    // #555555
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 192)); // #B4B4C0

        public SolidColorBrush CustomSortBgBrush => _activeSortMode == PlaylistSortMode.Custom ? ActiveBgBrush : InactiveBgBrush;
        public SolidColorBrush CustomSortBorderBrush => _activeSortMode == PlaylistSortMode.Custom ? ActiveBorderBrush : InactiveBorderBrush;
        public SolidColorBrush CustomSortFgBrush => _activeSortMode == PlaylistSortMode.Custom ? ActiveFgBrush : InactiveFgBrush;

        public SolidColorBrush BpmSortBgBrush => _activeSortMode == PlaylistSortMode.Bpm ? ActiveBgBrush : InactiveBgBrush;
        public SolidColorBrush BpmSortBorderBrush => _activeSortMode == PlaylistSortMode.Bpm ? ActiveBorderBrush : InactiveBorderBrush;
        public SolidColorBrush BpmSortFgBrush => _activeSortMode == PlaylistSortMode.Bpm ? ActiveFgBrush : InactiveFgBrush;

        public SolidColorBrush HarmonicSortBgBrush => _activeSortMode == PlaylistSortMode.Harmonic ? ActiveBgBrush : InactiveBgBrush;
        public SolidColorBrush HarmonicSortBorderBrush => _activeSortMode == PlaylistSortMode.Harmonic ? ActiveBorderBrush : InactiveBorderBrush;
        public SolidColorBrush HarmonicSortFgBrush => _activeSortMode == PlaylistSortMode.Harmonic ? ActiveFgBrush : InactiveFgBrush;

        private void RefreshSortVisualProperties()
        {
            OnPropertyChanged(nameof(CustomSortBgBrush));
            OnPropertyChanged(nameof(CustomSortBorderBrush));
            OnPropertyChanged(nameof(CustomSortFgBrush));
            OnPropertyChanged(nameof(BpmSortBgBrush));
            OnPropertyChanged(nameof(BpmSortBorderBrush));
            OnPropertyChanged(nameof(BpmSortFgBrush));
            OnPropertyChanged(nameof(HarmonicSortBgBrush));
            OnPropertyChanged(nameof(HarmonicSortBorderBrush));
            OnPropertyChanged(nameof(HarmonicSortFgBrush));
            OnPropertyChanged(nameof(SortByBpmLabel));
        }

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
                    _originalCustomTracks.RemoveAll(t => t.Id == track.Id || (!string.IsNullOrEmpty(t.FilePath) && t.FilePath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase)));
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

            SortCustomCommand = new RelayCommand(() =>
            {
                RestoreCustomOrder();
            });

            SortByBpmCommand = new RelayCommand(async () =>
            {
                await SortPlaylistByBpmAsync();
            });

            SortHarmonicallyCommand = new RelayCommand(async () =>
            {
                await SortPlaylistHarmonicallyAsync();
            });

            _ = LoadPlaylistDetailsAsync();
        }

        public void RestoreCustomOrder()
        {
            if (_isSorting) return;
            ActiveSortMode = PlaylistSortMode.Custom;
            SortByBpmLabel = "BPM";
            _bpmSortAscending = true;

            RunOnUIThread(() =>
            {
                Tracks.Clear();
                for (int i = 0; i < _originalCustomTracks.Count; i++)
                {
                    _originalCustomTracks[i].DisplayIndex = i + 1;
                    Tracks.Add(_originalCustomTracks[i]);
                }
                UpdateTimeStats();
            });
        }

        private async Task EnsurePlaylistHarmonicDataReadyAsync(List<Track> snapshot)
        {
            var missing = snapshot.Where(t => t.Bpm <= 0 || string.IsNullOrWhiteSpace(t.CamelotKey)).ToList();
            if (missing.Count == 0)
                return;

            await Task.Run(async () =>
            {
                var analyzed = new List<(Track track, double bpm, string key)>();
                foreach (var t in missing)
                {
                    var (bpm, key) = HarmonicAnalysisHelper.EnsureTrackHarmonicSync(t);
                    analyzed.Add((t, bpm, key));
                }

                RunOnUIThread(() =>
                {
                    foreach (var item in analyzed)
                    {
                        if (item.bpm > 0) item.track.Bpm = item.bpm;
                        if (!string.IsNullOrEmpty(item.key)) item.track.CamelotKey = item.key;
                    }
                });

                await _dbService.SaveHarmonicDataBatchAsync(missing);
            });
        }

        public async Task SortPlaylistByBpmAsync()
        {
            if (_isSorting || Tracks.Count <= 1) return;
            _isSorting = true;

            try
            {
                var snapshot = (_activeSortMode == PlaylistSortMode.Custom ? Tracks.ToList() : _originalCustomTracks.ToList());
                await EnsurePlaylistHarmonicDataReadyAsync(snapshot);

                bool asc;
                if (_activeSortMode == PlaylistSortMode.Bpm)
                {
                    _bpmSortAscending = !_bpmSortAscending;
                    asc = _bpmSortAscending;
                }
                else
                {
                    asc = true;
                    _bpmSortAscending = true;
                }

                var sorted = asc
                    ? snapshot.OrderBy(t => t.Bpm > 0 ? 0 : 1)
                              .ThenBy(t => t.Bpm)
                              .ThenBy(t => t.Title)
                              .ToList()
                    : snapshot.OrderBy(t => t.Bpm > 0 ? 0 : 1)
                              .ThenByDescending(t => t.Bpm)
                              .ThenBy(t => t.Title)
                              .ToList();

                RunOnUIThread(() =>
                {
                    SortByBpmLabel = asc ? "BPM ↑" : "BPM ↓";
                    ActiveSortMode = PlaylistSortMode.Bpm;

                    Tracks.Clear();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        sorted[i].DisplayIndex = i + 1;
                        Tracks.Add(sorted[i]);
                    }
                    UpdateTimeStats();
                });
                // No sobreescribe la base de datos para no perder el orden original del usuario
            }
            finally
            {
                _isSorting = false;
            }
        }

        public async Task SortPlaylistHarmonicallyAsync()
        {
            if (_isSorting || Tracks.Count <= 1) return;
            _isSorting = true;

            try
            {
                var snapshot = (_activeSortMode == PlaylistSortMode.Custom ? Tracks.ToList() : _originalCustomTracks.ToList());
                await EnsurePlaylistHarmonicDataReadyAsync(snapshot);

                var sorted = await Task.Run(() => HarmonicAnalysisHelper.SortTracksHarmonically(snapshot));

                RunOnUIThread(() =>
                {
                    SortByBpmLabel = "BPM";
                    _bpmSortAscending = true;
                    ActiveSortMode = PlaylistSortMode.Harmonic;

                    Tracks.Clear();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        sorted[i].DisplayIndex = i + 1;
                        Tracks.Add(sorted[i]);
                    }
                    UpdateTimeStats();
                });
                // No sobreescribe la base de datos para no perder el orden original del usuario
            }
            finally
            {
                _isSorting = false;
            }
        }

        public async Task ReorderTracksAsync()
        {
            for (int i = 0; i < Tracks.Count; i++)
            {
                Tracks[i].DisplayIndex = i + 1;
            }

            // Cuando el usuario arrastra manualmente un ítem, define su nuevo orden personalizado permanente
            _originalCustomTracks.Clear();
            _originalCustomTracks.AddRange(Tracks);
            _activeSortMode = PlaylistSortMode.Custom;
            _sortByBpmLabel = "BPM";
            _bpmSortAscending = true;
            RefreshSortVisualProperties();

            CurrentPlaylist.TrackIds = Tracks.Select(t => t.Id).ToList();
            CurrentPlaylist.TrackFilePaths = Tracks.Where(t => !string.IsNullOrEmpty(t.FilePath)).Select(t => t.FilePath).ToList();
            CurrentPlaylist.TrackCount = Tracks.Count;

            await _dbService.UpdatePlaylistTracksOrderAsync(CurrentPlaylist.Name, Tracks);
            UpdateTimeStats();
        }

        private void UpdateTimeStats()
        {
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
                _originalCustomTracks.Clear();
                _originalCustomTracks.AddRange(playlistTracks);

                _activeSortMode = PlaylistSortMode.Custom;
                _sortByBpmLabel = "BPM";
                _bpmSortAscending = true;
                RefreshSortVisualProperties();

                Tracks.Clear();
                for (int i = 0; i < playlistTracks.Count; i++)
                {
                    playlistTracks[i].DisplayIndex = i + 1;
                    Tracks.Add(playlistTracks[i]);
                }

                UpdateTimeStats();
                OnPropertyChanged(nameof(CurrentPlaylist));
            });

            HarmonicAnalysisHelper.EnqueueTracksForAnalysis(playlistTracks, _dbService, MainWindow.Instance?.DispatcherQueue);
        }
    }
}
