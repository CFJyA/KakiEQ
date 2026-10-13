using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Audio;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private static MainViewModel? _instance;
        public static MainViewModel? Instance => _instance;

        private readonly DatabaseService _dbService;
        private readonly LibraryService _libService;
        private readonly SettingsService _settingsService;
        private readonly BassAudioEngine _audioEngine;
        private readonly DispatcherQueueTimer _timer;
        private readonly DispatcherQueue _dispatcherQueue;

        public DatabaseService DbService => _dbService;
        public LibraryService LibService => _libService;
        public SettingsService SettingsService => _settingsService;
        public BassAudioEngine AudioEngine => _audioEngine;

        // --- Current View & Navigation ---
        private object? _currentView;
        public object? CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        private bool _isLightTheme;
        public bool IsLightTheme
        {
            get => _isLightTheme;
            set
            {
                if (_isLightTheme != value)
                {
                    _isLightTheme = value;
                    OnPropertyChanged();
                    RefreshNavBrushes();
                }
            }
        }

        private static readonly SolidColorBrush NavInactiveBg = new(Windows.UI.Color.FromArgb(0, 0, 0, 0)); // Transparent

        private SolidColorBrush NavActiveBg => IsLightTheme
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 237, 231, 246)) // #EDE7F6
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 30, 114));  // #381E72

        private SolidColorBrush NavActiveFg => IsLightTheme
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 124, 77, 255)) // #7C4DFF
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 208, 188, 255)); // #D0BCFF

        private SolidColorBrush NavInactiveFg => IsLightTheme
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 85, 85, 85))   // #555555
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)); // #B4B4B4

        private string _currentSectionTitle = "Inicio";
        public string CurrentSectionTitle
        {
            get => _currentSectionTitle;
            set
            {
                _currentSectionTitle = value;
                OnPropertyChanged();
                RefreshNavBrushes();
            }
        }

        public void RefreshNavBrushes()
        {
            OnPropertyChanged(nameof(NavHomeBg));
            OnPropertyChanged(nameof(NavHomeFg));
            OnPropertyChanged(nameof(NavSongsBg));
            OnPropertyChanged(nameof(NavSongsFg));
            OnPropertyChanged(nameof(NavAlbumsBg));
            OnPropertyChanged(nameof(NavAlbumsFg));
            OnPropertyChanged(nameof(NavArtistsBg));
            OnPropertyChanged(nameof(NavArtistsFg));
            OnPropertyChanged(nameof(NavPlaylistsBg));
            OnPropertyChanged(nameof(NavPlaylistsFg));
            OnPropertyChanged(nameof(NavEqBg));
            OnPropertyChanged(nameof(NavEqFg));
        }

        public SolidColorBrush NavHomeBg => CurrentSectionTitle == "Inicio" ? NavActiveBg : NavInactiveBg;
        public SolidColorBrush NavHomeFg => CurrentSectionTitle == "Inicio" ? NavActiveFg : NavInactiveFg;

        public SolidColorBrush NavSongsBg => CurrentSectionTitle == "Canciones" ? NavActiveBg : NavInactiveBg;
        public SolidColorBrush NavSongsFg => CurrentSectionTitle == "Canciones" ? NavActiveFg : NavInactiveFg;

        public SolidColorBrush NavAlbumsBg => CurrentSectionTitle == "Álbumes" ? NavActiveBg : NavInactiveBg;
        public SolidColorBrush NavAlbumsFg => CurrentSectionTitle == "Álbumes" ? NavActiveFg : NavInactiveFg;

        public SolidColorBrush NavArtistsBg => CurrentSectionTitle == "Artistas" ? NavActiveBg : NavInactiveBg;
        public SolidColorBrush NavArtistsFg => CurrentSectionTitle == "Artistas" ? NavActiveFg : NavInactiveFg;

        public SolidColorBrush NavPlaylistsBg => CurrentSectionTitle == "Playlists" ? NavActiveBg : NavInactiveBg;
        public SolidColorBrush NavPlaylistsFg => CurrentSectionTitle == "Playlists" ? NavActiveFg : NavInactiveFg;

        public SolidColorBrush NavEqBg => CurrentSectionTitle == "Ecualizador" ? NavActiveBg : NavInactiveBg;
        public SolidColorBrush NavEqFg => CurrentSectionTitle == "Ecualizador" ? NavActiveFg : NavInactiveFg;

        // --- Sidebar State ---
        private bool _isSidebarExpanded = true;
        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set
            {
                _isSidebarExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SidebarVisibility));
                OnPropertyChanged(nameof(SidebarToggleIconGlyph));
                OnPropertyChanged(nameof(SidebarToggleTooltip));
            }
        }

        public Visibility SidebarVisibility => IsSidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        public string SidebarToggleIconGlyph => IsSidebarExpanded ? "\uE76B" : "\uE76C"; // < when expanded, > when collapsed
        public string SidebarToggleTooltip => IsSidebarExpanded ? "Ocultar barra lateral" : "Mostrar barra lateral";

        // --- Scanning / Library state ---
        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanningVisibility));
            }
        }

        public Visibility ScanningVisibility => IsScanning ? Visibility.Visible : Visibility.Collapsed;

        private string _scanStatusText = string.Empty;
        public string ScanStatusText
        {
            get => _scanStatusText;
            set { _scanStatusText = value; OnPropertyChanged(); }
        }

        // --- Player State ---
        private Track? _currentTrack;
        public Track? CurrentTrack
        {
            get => _currentTrack;
            set
            {
                _currentTrack = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTrack));
                _ = UpdateLikeStateAsync();
            }
        }

        public bool HasTrack => CurrentTrack != null;

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayPauseIconGlyph));
                OnPropertyChanged(nameof(PlayPauseTooltip));
            }
        }

        public string PlayPauseIconGlyph => IsPlaying ? "\uE769" : "\uE768"; // Pause / Play Segoe glyphs
        public string PlayPauseTooltip => IsPlaying ? "Pausar" : "Reproducir";

        private static readonly SolidColorBrush AccentBrush = new(Windows.UI.Color.FromArgb(255, 187, 134, 252));
        private static readonly SolidColorBrush InactiveBrush = new(Windows.UI.Color.FromArgb(255, 119, 119, 119));
        private static readonly SolidColorBrush HeartBrush = new(Windows.UI.Color.FromArgb(255, 255, 64, 129));

        private bool _isCurrentTrackLiked;
        public bool IsCurrentTrackLiked
        {
            get => _isCurrentTrackLiked;
            set
            {
                _isCurrentTrackLiked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LikeBrush));
            }
        }

        public SolidColorBrush LikeBrush => IsCurrentTrackLiked ? HeartBrush : InactiveBrush;

        private bool _isShuffle;
        public bool IsShuffle
        {
            get => _isShuffle;
            set
            {
                _isShuffle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShuffleBrush));
            }
        }

        public SolidColorBrush ShuffleBrush => IsShuffle ? AccentBrush : InactiveBrush;

        private bool _isRepeat;
        public bool IsRepeat
        {
            get => _isRepeat;
            set
            {
                _isRepeat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RepeatBrush));
            }
        }

        public SolidColorBrush RepeatBrush => IsRepeat ? AccentBrush : InactiveBrush;

        private double _volume = 80.0;
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 100);
                _audioEngine.SetVolume((float)(_volume / 100.0));
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeIconGlyph));
            }
        }

        public string VolumeIconGlyph => _volume switch
        {
            <= 0 => "\uE74F", // Mute
            < 33 => "\uE992", // Low
            < 66 => "\uE993", // Medium
            _ => "\uE995"     // High
        };

        private double _currentPositionSeconds;
        public double CurrentPositionSeconds
        {
            get => _currentPositionSeconds;
            set
            {
                _currentPositionSeconds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPositionFormatted));
            }
        }

        public string CurrentPositionFormatted => TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"mm\:ss");

        private double _totalDurationSeconds;
        public double TotalDurationSeconds
        {
            get => _totalDurationSeconds;
            set
            {
                _totalDurationSeconds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalDurationFormatted));
            }
        }

        public string TotalDurationFormatted => TimeSpan.FromSeconds(TotalDurationSeconds).ToString(@"mm\:ss");

        private bool _isNowPlayingExpanded;
        public bool IsNowPlayingExpanded
        {
            get => _isNowPlayingExpanded;
            set
            {
                _isNowPlayingExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NowPlayingExpandedVisibility));
            }
        }

        public Visibility NowPlayingExpandedVisibility => IsNowPlayingExpanded ? Visibility.Visible : Visibility.Collapsed;

        // --- Playlists & Queue ---
        private ObservableCollection<Playlist> _globalPlaylists = new();
        public ObservableCollection<Playlist> GlobalPlaylists
        {
            get => _globalPlaylists;
            set { _globalPlaylists = value; OnPropertyChanged(); }
        }

        private ObservableCollection<Track> _currentQueue = new();
        public ObservableCollection<Track> CurrentQueue
        {
            get => _currentQueue;
            set
            {
                _currentQueue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QueueCountText));
            }
        }

        public string QueueCountText => $"COLA ({CurrentQueue.Count})";

        private int _currentQueueIndex = -1;
        public int CurrentQueueIndex
        {
            get => _currentQueueIndex;
            set { _currentQueueIndex = value; OnPropertyChanged(); }
        }

        // --- Navigation Commands ---
        public ICommand ToggleSidebarCommand { get; }
        public ICommand NavigateHomeCommand { get; }
        public ICommand NavigateSongsCommand { get; }
        public ICommand NavigateArtistsCommand { get; }
        public ICommand NavigateAlbumsCommand { get; }
        public ICommand NavigatePlaylistsCommand { get; }
        public ICommand NavigateEqualizerCommand { get; }

        // --- Player Controls Commands ---
        public ICommand PlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand ToggleShuffleCommand { get; }
        public ICommand ToggleRepeatCommand { get; }
        public ICommand LikeTrackCommand { get; }
        public ICommand OpenNowPlayingCommand { get; }
        public ICommand CloseNowPlayingCommand { get; }
        public ICommand AddTrackToPlaylistCommand { get; }

        public MainViewModel()
        {
            _instance = this;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _dbService = new DatabaseService();
            _libService = new LibraryService(_dbService);
            _settingsService = new SettingsService();
            _audioEngine = new BassAudioEngine();

            var savedSettings = _settingsService.LoadSettings();
            _volume = savedSettings.Volume * 100.0;
            _audioEngine.SetVolume(savedSettings.Volume);

            // Timer for tracking playback progression
            _timer = _dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Tick += (s, e) =>
            {
                if (_audioEngine.IsPlaying)
                {
                    CurrentPositionSeconds = _audioEngine.CurrentPosition.TotalSeconds;
                }
            };

            // Setup track ended auto next
            _audioEngine.TrackEnded += (s, e) =>
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    if (IsRepeat && CurrentTrack != null)
                    {
                        await PlayTrackAsync(CurrentTrack);
                    }
                    else
                    {
                        await PlayNextAsync();
                    }
                });
            };

            // Navigation
            ToggleSidebarCommand = new RelayCommand(() =>
            {
                IsSidebarExpanded = !IsSidebarExpanded;
            });

            NavigateHomeCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Inicio";
                CurrentView = new HomeViewModel(_dbService);
            });

            NavigateSongsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Canciones";
                CurrentView = new LibraryViewModel(_dbService);
            });

            NavigateArtistsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Artistas";
                CurrentView = new ArtistsViewModel(_dbService);
            });

            NavigateAlbumsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Álbumes";
                CurrentView = new AlbumsViewModel(_dbService);
            });

            NavigatePlaylistsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Playlists";
                CurrentView = new PlaylistsViewModel(_dbService);
            });

            NavigateEqualizerCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Ecualizador";
                CurrentView = new EqualizerViewModel(_audioEngine, _settingsService);
            });

            // Player Commands
            PlayPauseCommand = new RelayCommand(async () =>
            {
                if (CurrentTrack == null) return;

                if (IsPlaying)
                {
                    await _audioEngine.PauseAsync();
                    IsPlaying = false;
                    _timer.Stop();
                }
                else
                {
                    await _audioEngine.PlayAsync();
                    IsPlaying = true;
                    _timer.Start();
                }
            });

            NextCommand = new RelayCommand(async () => await PlayNextAsync());
            PreviousCommand = new RelayCommand(async () => await PlayPreviousAsync());

            ToggleShuffleCommand = new RelayCommand(() =>
            {
                IsShuffle = !IsShuffle;
            });

            ToggleRepeatCommand = new RelayCommand(() =>
            {
                IsRepeat = !IsRepeat;
            });

            LikeTrackCommand = new RelayCommand(async trackObj =>
            {
                var trackToLike = trackObj as Track ?? CurrentTrack;
                if (trackToLike != null)
                {
                    bool isLiked = await _dbService.IsTrackInPlaylistAsync("Yollotl", trackToLike);
                    if (isLiked)
                    {
                        await _dbService.RemoveTrackFromPlaylistAsync("Yollotl", trackToLike);
                    }
                    else
                    {
                        await _dbService.AddTrackToPlaylistAsync("Yollotl", trackToLike);
                    }

                    if (trackToLike == CurrentTrack)
                    {
                        await UpdateLikeStateAsync();
                    }
                    await LoadGlobalPlaylistsAsync();
                }
            });

            OpenNowPlayingCommand = new RelayCommand(() => IsNowPlayingExpanded = true);
            CloseNowPlayingCommand = new RelayCommand(() => IsNowPlayingExpanded = false);

            AddTrackToPlaylistCommand = new RelayCommand(async param =>
            {
                if (param is (Playlist playlist, Track track))
                {
                    await _dbService.AddTrackToPlaylistAsync(playlist.Name, track);
                    await LoadGlobalPlaylistsAsync();
                }
            });

            // Set initial view to Home
            CurrentView = new HomeViewModel(_dbService);
            _ = LoadGlobalPlaylistsAsync();
        }

        public void NavigateToArtist(string artistName)
        {
            CurrentSectionTitle = artistName;
            CurrentView = new ArtistDetailViewModel(_dbService, artistName);
        }

        public void NavigateToAlbum(string albumTitle)
        {
            CurrentSectionTitle = albumTitle;
            CurrentView = new AlbumDetailViewModel(_dbService, albumTitle);
        }

        public void NavigateToPlaylist(Playlist playlist)
        {
            CurrentSectionTitle = playlist.Name;
            CurrentView = new PlaylistDetailViewModel(_dbService, playlist);
        }

        public void NavigateToPlaylists()
        {
            CurrentSectionTitle = "Playlists";
            CurrentView = new PlaylistsViewModel(_dbService);
        }

        public void NavigateToAlbums()
        {
            CurrentSectionTitle = "Álbumes";
            CurrentView = new AlbumsViewModel(_dbService);
        }

        public void NavigateToArtists()
        {
            CurrentSectionTitle = "Artistas";
            CurrentView = new ArtistsViewModel(_dbService);
        }

        public async Task LoadGlobalPlaylistsAsync()
        {
            var playlists = await _dbService.GetAllPlaylistsAsync();
            _dispatcherQueue.TryEnqueue(() =>
            {
                GlobalPlaylists.Clear();
                foreach (var p in playlists.Where(x => !x.IsCreateCard))
                {
                    GlobalPlaylists.Add(p);
                }
            });
        }

        public async Task PlayTrackAsync(Track track, List<Track>? contextQueue = null)
        {
            if (contextQueue != null)
            {
                CurrentQueue = new ObservableCollection<Track>(contextQueue);
                CurrentQueueIndex = contextQueue.IndexOf(track);
            }
            else if (!CurrentQueue.Contains(track))
            {
                CurrentQueue.Add(track);
                CurrentQueueIndex = CurrentQueue.IndexOf(track);
            }
            else
            {
                CurrentQueueIndex = CurrentQueue.IndexOf(track);
            }

            CurrentTrack = track;
            TotalDurationSeconds = track.Duration.TotalSeconds;
            CurrentPositionSeconds = 0;

            await _audioEngine.LoadFileAsync(track.FilePath);
            await _audioEngine.PlayAsync();

            IsPlaying = true;
            _timer.Start();
        }

        public async Task PlayNextAsync()
        {
            if (CurrentQueue == null || CurrentQueue.Count == 0) return;

            if (IsShuffle)
            {
                var rng = new Random();
                CurrentQueueIndex = rng.Next(CurrentQueue.Count);
            }
            else
            {
                CurrentQueueIndex++;
                if (CurrentQueueIndex >= CurrentQueue.Count)
                {
                    CurrentQueueIndex = 0;
                }
            }

            var nextTrack = CurrentQueue[CurrentQueueIndex];
            await PlayTrackAsync(nextTrack);
        }

        public async Task PlayPreviousAsync()
        {
            if (CurrentQueue == null || CurrentQueue.Count == 0) return;

            // If more than 3 seconds elapsed, restart current song
            if (CurrentPositionSeconds > 3.0 && CurrentTrack != null)
            {
                SeekTo(0);
                return;
            }

            CurrentQueueIndex--;
            if (CurrentQueueIndex < 0)
            {
                CurrentQueueIndex = CurrentQueue.Count - 1;
            }

            var prevTrack = CurrentQueue[CurrentQueueIndex];
            await PlayTrackAsync(prevTrack);
        }

        public void SeekTo(double seconds)
        {
            CurrentPositionSeconds = seconds;
            _audioEngine.SetPosition(TimeSpan.FromSeconds(seconds));
        }

        private async Task UpdateLikeStateAsync()
        {
            if (_currentTrack != null)
            {
                IsCurrentTrackLiked = await _dbService.IsTrackInPlaylistAsync("Yollotl", _currentTrack);
            }
            else
            {
                IsCurrentTrackLiked = false;
            }
        }

        public async Task ScanLibraryFolderAsync(string folderPath)
        {
            IsScanning = true;
            ScanStatusText = "Buscando canciones...";

            var progress = new Progress<(int current, int total, string file)>(p =>
            {
                ScanStatusText = $"Indexando ({p.current}/{p.total}): {p.file}";
            });

            await _libService.ScanFolderAsync(folderPath, progress);

            var settings = _settingsService.LoadSettings();
            settings.MusicFolderPath = folderPath;
            _settingsService.SaveSettings(settings);

            IsScanning = false;
            ScanStatusText = string.Empty;

            await LoadGlobalPlaylistsAsync();
            if (CurrentView is LibraryViewModel libVm)
            {
                await libVm.LoadLibraryAsync();
            }
            else if (CurrentView is HomeViewModel homeVm)
            {
                await homeVm.LoadSuggestionsAsync();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

