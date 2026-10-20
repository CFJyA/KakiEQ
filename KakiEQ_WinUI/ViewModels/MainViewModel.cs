using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Audio;
using KakiEQ_WinUI.Services.Data;
using KakiEQ_WinUI.Services.SystemIntegration;

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
        private readonly SmtcService _smtcService;
        private readonly DispatcherQueueTimer _timer;
        private readonly DispatcherQueue _dispatcherQueue;

        public DatabaseService DbService => _dbService;
        public LibraryService LibService => _libService;
        public SettingsService SettingsService => _settingsService;
        public BassAudioEngine AudioEngine => _audioEngine;
        public SmtcService SmtcService => _smtcService;

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
                UpdateCurrentTrackTechnicalInfo();
                _ = UpdateLikeStateAsync();
                _ = LoadLyricsForCurrentTrackAsync();
                _ = UpdateBlurredCoverAsync(value);
            }
        }

        public bool HasTrack => CurrentTrack != null;

        private ImageSource? _currentBlurredCover;
        public ImageSource? CurrentBlurredCover
        {
            get => _currentBlurredCover;
            set { _currentBlurredCover = value; OnPropertyChanged(); }
        }

        private async Task UpdateBlurredCoverAsync(Track? track)
        {
            if (track != null && !string.IsNullOrEmpty(track.CoverImagePath) && File.Exists(track.CoverImagePath))
            {
                // Show cover art as immediate preview
                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        CurrentBlurredCover = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(track.CoverImagePath)));
                    }
                    catch
                    {
                        CurrentBlurredCover = null;
                    }
                });

                string? blurPath = await Helpers.ImageBlurHelper.GetBlurredCoverAsync(track.CoverImagePath);
                if (!string.IsNullOrEmpty(blurPath) && File.Exists(blurPath))
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            CurrentBlurredCover = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(blurPath)));
                        }
                        catch
                        {
                        }
                    });
                }
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    CurrentBlurredCover = null;
                });
            }
        }

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
                _smtcService?.UpdatePlaybackState(value);
            }
        }

        public string PlayPauseIconGlyph => IsPlaying ? "\uE769" : "\uF5B0"; // Pause / Play Solid glyph
        public string PlayPauseTooltip => IsPlaying ? "Pausar" : "Reproducir";

        private static readonly SolidColorBrush AccentBrush = new(Windows.UI.Color.FromArgb(255, 187, 134, 252));
        private static readonly SolidColorBrush InactiveBrush = new(Windows.UI.Color.FromArgb(255, 180, 180, 192));
        private static readonly SolidColorBrush HeartBrush = new(Windows.UI.Color.FromArgb(255, 255, 64, 129));

        // --- NowPlaying Drawer & Lyrics State ---
        private bool _isNowPlayingQueueVisible = false;
        public bool IsNowPlayingQueueVisible
        {
            get => _isNowPlayingQueueVisible;
            set
            {
                _isNowPlayingQueueVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NowPlayingQueueVisibility));
                OnPropertyChanged(nameof(QueueToggleBrush));
                OnPropertyChanged(nameof(QueueToggleForegroundBrush));
            }
        }

        public Visibility NowPlayingQueueVisibility => IsNowPlayingQueueVisible ? Visibility.Visible : Visibility.Collapsed;
        private static readonly SolidColorBrush QueueClosedBrush = new(Windows.UI.Color.FromArgb(255, 28, 28, 36));
        public SolidColorBrush QueueToggleBrush => IsNowPlayingQueueVisible ? AccentBrush : QueueClosedBrush;
        public SolidColorBrush QueueToggleForegroundBrush => IsNowPlayingQueueVisible ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 24)) : new SolidColorBrush(Microsoft.UI.Colors.White);

        private bool _isLyricsVisible = false;
        public bool IsLyricsVisible
        {
            get => _isLyricsVisible;
            set
            {
                _isLyricsVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LyricsVisibility));
                OnPropertyChanged(nameof(AlbumCoverVisibility));
                OnPropertyChanged(nameof(LyricsBrush));
                OnPropertyChanged(nameof(KaraokeButtonVisibility));
                if (value && CurrentLyricsLines.Count == 0)
                {
                    _ = LoadLyricsForCurrentTrackAsync();
                }
            }
        }

        public Visibility LyricsVisibility => IsLyricsVisible ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AlbumCoverVisibility => IsLyricsVisible ? Visibility.Collapsed : Visibility.Visible;
        public SolidColorBrush LyricsBrush => IsLyricsVisible ? AccentBrush : InactiveBrush;

        public ObservableCollection<LrcLine> CurrentLyricsLines { get; } = new();
        public bool HasLyrics => CurrentLyricsLines.Count > 0;
        public Visibility HasLyricsVisibility => HasLyrics ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoLyricsVisibility => !HasLyrics ? Visibility.Visible : Visibility.Collapsed;
        private int _activeLyricIndex = -1;

        public event EventHandler<int>? LyricLineChanged;
        public Action? ExitFullScreenIfActive { get; set; }

        private bool _isFullScreen;
        public bool IsFullScreen
        {
            get => _isFullScreen;
            set
            {
                _isFullScreen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullScreenGlyph));
                OnPropertyChanged(nameof(FullScreenTooltip));
                OnPropertyChanged(nameof(KaraokeButtonVisibility));
            }
        }
        public string FullScreenGlyph => IsFullScreen ? "\uE73F" : "\uE740";
                public string FullScreenTooltip => IsFullScreen ? "Salir de pantalla completa" : "Pantalla completa";

        private bool _isKaraokeActive;
        private bool _isKaraokeProcessing;
        private int _karaokeProgressPercent;
        private CancellationTokenSource? _karaokeCts;

        public bool IsKaraokeActive
        {
            get => _isKaraokeActive;
            private set
            {
                if (_isKaraokeActive != value)
                {
                    _isKaraokeActive = value;
                    _audioEngine.IsKaraokeMode = value;
                    OnPropertyChanged();
                    UpdateKaraokeUiProperties();
                }
            }
        }

        public bool IsKaraokeProcessing
        {
            get => _isKaraokeProcessing;
            private set
            {
                if (_isKaraokeProcessing != value)
                {
                    _isKaraokeProcessing = value;
                    OnPropertyChanged();
                    UpdateKaraokeUiProperties();
                }
            }
        }

        public int KaraokeProgressPercent
        {
            get => _karaokeProgressPercent;
            private set
            {
                if (_karaokeProgressPercent != value)
                {
                    _karaokeProgressPercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(KaraokeButtonText));
                    OnPropertyChanged(nameof(KaraokeTooltip));
                }
            }
        }

        public Visibility KaraokeButtonVisibility => (IsFullScreen && IsLyricsVisible) ? Visibility.Visible : Visibility.Collapsed;

        public string KaraokeButtonText
        {
            get
            {
                if (IsKaraokeProcessing)
                    return KaraokeProgressPercent > 0 ? $"Separando IA ({KaraokeProgressPercent}%)" : "Separando IA...";
                if (IsKaraokeActive)
                    return "Modo Karaoke (Activo)";
                return "Modo Karaoke";
            }
        }

        public string KaraokeIconGlyph => IsKaraokeProcessing ? "\uE895" : "\uE720";

        public SolidColorBrush KaraokeButtonBackground => (IsKaraokeActive || IsKaraokeProcessing)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(70, 187, 134, 252)) 
            : new SolidColorBrush(Windows.UI.Color.FromArgb(50, 24, 24, 32));

        public SolidColorBrush KaraokeButtonBorderBrush => (IsKaraokeActive || IsKaraokeProcessing)
            ? AccentBrush 
            : new SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));

        public SolidColorBrush KaraokePillForeground => (IsKaraokeActive || IsKaraokeProcessing) ? AccentBrush : new SolidColorBrush(Microsoft.UI.Colors.White);
        public SolidColorBrush KaraokeIconBrush => (IsKaraokeActive || IsKaraokeProcessing) ? AccentBrush : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 208, 208, 220));
        public string KaraokeTooltip
        {
            get
            {
                if (IsKaraokeProcessing)
                    return $"Separando voz con IA ({KaraokeProgressPercent}%)...";
                if (IsKaraokeActive)
                    return "Modo Karaoke activo (Pista instrumental de estudio). Clic para volver a la canción original.";
                if (KaraokeService.IsTrackReady(CurrentTrack))
                    return "Modo Karaoke: Pista lista (Clic para activar al instante en 0 ms)";
                return "Modo Karaoke: Separa y reproduce el instrumental de estudio con IA";
            }
        }

        public ICommand ToggleKaraokeCommand { get; }

        private void UpdateKaraokeUiProperties()
        {
            OnPropertyChanged(nameof(KaraokeButtonText));
            OnPropertyChanged(nameof(KaraokeIconGlyph));
            OnPropertyChanged(nameof(KaraokeButtonBackground));
            OnPropertyChanged(nameof(KaraokeButtonBorderBrush));
            OnPropertyChanged(nameof(KaraokePillForeground));
            OnPropertyChanged(nameof(KaraokeIconBrush));
            OnPropertyChanged(nameof(KaraokeTooltip));
        }

        public async Task ToggleKaraokeAsync()
        {
            if (CurrentTrack == null) return;

            // 1. If currently active -> Turn OFF and restore original track
            if (IsKaraokeActive)
            {
                if (IsKaraokeProcessing)
                {
                    _karaokeCts?.Cancel();
                    IsKaraokeProcessing = false;
                }

                if (!string.IsNullOrEmpty(CurrentTrack.FilePath) && File.Exists(CurrentTrack.FilePath))
                {
                    await _audioEngine.SwitchStreamPreservingPositionAsync(CurrentTrack.FilePath);
                }

                IsKaraokeActive = false;
                MainWindow.Instance?.ShowToast("Modo Karaoke desactivado (Audio original)");
                return;
            }

            // 2. If already processed/cached -> Switch INSTANTLY (0 ms)!
            string? cachedPath = KaraokeService.GetInstrumentalPath(CurrentTrack);
            if (!string.IsNullOrEmpty(cachedPath) && File.Exists(cachedPath))
            {
                bool switched = await _audioEngine.SwitchStreamPreservingPositionAsync(cachedPath);
                if (switched)
                {
                    IsKaraokeActive = true;
                    MainWindow.Instance?.ShowToast("✨ Modo Karaoke activado (Instrumental de estudio)");
                    return;
                }
            }

            // 3. Not processed yet -> Run AI separation with live progress
            if (IsKaraokeProcessing)
            {
                MainWindow.Instance?.ShowToast("La separación de voz con IA ya está en curso...");
                return;
            }

            var trackToProcess = CurrentTrack;
            IsKaraokeProcessing = true;
            KaraokeProgressPercent = 0;
            MainWindow.Instance?.ShowToast("⏳ Separando instrumental con IA en segundo plano... Puedes seguir escuchando.");

            _karaokeCts?.Cancel();
            _karaokeCts = new CancellationTokenSource();
            var ct = _karaokeCts.Token;

            var progress = new Progress<int>(pct =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (IsKaraokeProcessing)
                    {
                        KaraokeProgressPercent = pct;
                    }
                });
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    string? resultPath = await KaraokeService.SeparateTrackAsync(
                        trackToProcess,
                        progress,
                        status =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Karaoke] Status: {status}");
                        },
                        ct);

                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        IsKaraokeProcessing = false;

                        if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                        {
                            if (CurrentTrack?.Id == trackToProcess.Id)
                            {
                                bool switched = await _audioEngine.SwitchStreamPreservingPositionAsync(resultPath);
                                if (switched)
                                {
                                    IsKaraokeActive = true;
                                    MainWindow.Instance?.ShowToast("✨ ¡Karaoke listo! Calidad de estudio activada.");
                                }
                            }
                            else
                            {
                                MainWindow.Instance?.ShowToast($"✨ Pista de Karaoke lista para '{trackToProcess.Title}'");
                            }
                        }
                        else if (!ct.IsCancellationRequested)
                        {
                            MainWindow.Instance?.ShowToast("No se pudo generar la pista instrumental con IA.");
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    _dispatcherQueue.TryEnqueue(() => { IsKaraokeProcessing = false; });
                }
                catch (Exception ex)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        IsKaraokeProcessing = false;
                        MainWindow.Instance?.ShowToast($"Error al procesar Karaoke: {ex.Message}");
                    });
                }
            }, ct);
        }

        public async Task PrepareKaraokeWithAiAsync(Track track)
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath)) return;

            if (KaraokeService.IsTrackReady(track))
            {
                MainWindow.Instance?.ShowToast($"'{track.Title}' ya tiene su pista de Karaoke lista.");
                return;
            }

            MainWindow.Instance?.ShowToast($"⏳ Preparando Karaoke con IA para '{track.Title}'...");

            try
            {
                string? result = await KaraokeService.SeparateTrackAsync(track);
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(result))
                    {
                        MainWindow.Instance?.ShowToast($"✨ ¡Pista de Karaoke lista para '{track.Title}'!");
                    }
                    else
                    {
                        MainWindow.Instance?.ShowToast($"No se pudo generar el Karaoke para '{track.Title}'.");
                    }
                });
            }
            catch (Exception ex)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    MainWindow.Instance?.ShowToast($"Error al procesar: {ex.Message}");
                });
            }
        }

        private bool _isWindowMaximized;
        public bool IsWindowMaximized
        {
            get => _isWindowMaximized;
            set
            {
                if (_isWindowMaximized != value)
                {
                    _isWindowMaximized = value;
                    OnPropertyChanged();
                }
            }
        }

        public void UpdateActiveLyric()
        {
            if (CurrentLyricsLines.Count == 0) return;
            var pos = TimeSpan.FromSeconds(CurrentPositionSeconds);
            var list = CurrentLyricsLines.ToList();
            int newIndex = Services.Audio.LrcService.FindActiveIndex(list, pos);
            if (newIndex != _activeLyricIndex)
            {
                if (_activeLyricIndex >= 0 && _activeLyricIndex < CurrentLyricsLines.Count)
                {
                    CurrentLyricsLines[_activeLyricIndex].IsActive = false;
                }
                _activeLyricIndex = newIndex;
                if (_activeLyricIndex >= 0 && _activeLyricIndex < CurrentLyricsLines.Count)
                {
                    CurrentLyricsLines[_activeLyricIndex].IsActive = true;
                    LyricLineChanged?.Invoke(this, _activeLyricIndex);
                }
            }
        }

        public async Task LoadLyricsForCurrentTrackAsync()
        {
            var track = CurrentTrack;
            if (track == null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    CurrentLyricsLines.Clear();
                    _activeLyricIndex = -1;
                    OnPropertyChanged(nameof(HasLyrics));
                    OnPropertyChanged(nameof(HasLyricsVisibility));
                    OnPropertyChanged(nameof(NoLyricsVisibility));
                });
                return;
            }

            var lrcRaw = await Services.Audio.LrcService.LoadRawLrcForTrackAsync(track);
            var parsed = Services.Audio.LrcService.Parse(lrcRaw);

            _dispatcherQueue.TryEnqueue(() =>
            {
                CurrentLyricsLines.Clear();
                _activeLyricIndex = -1;
                foreach (var line in parsed)
                {
                    CurrentLyricsLines.Add(line);
                }
                OnPropertyChanged(nameof(HasLyrics));
                OnPropertyChanged(nameof(HasLyricsVisibility));
                OnPropertyChanged(nameof(NoLyricsVisibility));
            });
        }

        private bool _isCurrentTrackLiked;
        public bool IsCurrentTrackLiked
        {
            get => _isCurrentTrackLiked;
            set
            {
                _isCurrentTrackLiked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LikeBrush));
                OnPropertyChanged(nameof(LikeGlyph));
                OnPropertyChanged(nameof(LikeTooltipText));
            }
        }

        public SolidColorBrush LikeBrush => IsCurrentTrackLiked ? HeartBrush : InactiveBrush;
        public string LikeGlyph => IsCurrentTrackLiked ? "\uEB52" : "\uEB51";
        public string LikeTooltipText => IsCurrentTrackLiked ? "Quitar de Favoritos (Yollotl)" : "Añadir a Favoritos (Yollotl)";

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
                OnPropertyChanged(nameof(BottomPlayerBarVisibility));
            }
        }

        public Visibility NowPlayingExpandedVisibility => IsNowPlayingExpanded ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BottomPlayerBarVisibility => IsNowPlayingExpanded ? Visibility.Collapsed : Visibility.Visible;

        // --- Audio Technical Specifications (NowPlaying only) ---
        private bool _showAudioTechnicalInfo;
        public bool ShowAudioTechnicalInfo
        {
            get => _showAudioTechnicalInfo;
            set
            {
                if (_showAudioTechnicalInfo != value)
                {
                    _showAudioTechnicalInfo = value;
                    OnPropertyChanged();
                    if (value && string.IsNullOrEmpty(CurrentTrackAudioFormat) && CurrentTrack != null)
                    {
                        UpdateCurrentTrackTechnicalInfo();
                    }
                    OnPropertyChanged(nameof(AudioTechInfoVisibility));
                }
            }
        }

        private string _currentTrackAudioFormat = string.Empty;
        public string CurrentTrackAudioFormat
        {
            get => _currentTrackAudioFormat;
            set { _currentTrackAudioFormat = value; OnPropertyChanged(); }
        }

        private string _currentTrackBitDepth = string.Empty;
        public string CurrentTrackBitDepth
        {
            get => _currentTrackBitDepth;
            set { _currentTrackBitDepth = value; OnPropertyChanged(); }
        }

        private string _currentTrackBitrate = string.Empty;
        public string CurrentTrackBitrate
        {
            get => _currentTrackBitrate;
            set { _currentTrackBitrate = value; OnPropertyChanged(); }
        }

        private string _currentTrackBaseDecibels = string.Empty;
        public string CurrentTrackBaseDecibels
        {
            get => _currentTrackBaseDecibels;
            set { _currentTrackBaseDecibels = value; OnPropertyChanged(); }
        }

        public Visibility AudioTechInfoVisibility => (ShowAudioTechnicalInfo && CurrentTrack != null && !string.IsNullOrEmpty(CurrentTrackAudioFormat))
            ? Visibility.Visible
            : Visibility.Collapsed;

        public void UpdateCurrentTrackTechnicalInfo()
        {
            if (CurrentTrack == null)
            {
                CurrentTrackAudioFormat = string.Empty;
                CurrentTrackBitDepth = string.Empty;
                CurrentTrackBitrate = string.Empty;
                CurrentTrackBaseDecibels = string.Empty;
                OnPropertyChanged(nameof(AudioTechInfoVisibility));
                return;
            }

            AudioTechnicalInfoHelper.EnsureTrackTechnicalInfo(CurrentTrack);

            CurrentTrackAudioFormat = AudioTechnicalInfoHelper.FormatFormat(CurrentTrack.AudioFormat);
            CurrentTrackBitDepth = AudioTechnicalInfoHelper.FormatBits(CurrentTrack.BitsPerSample);
            CurrentTrackBitrate = AudioTechnicalInfoHelper.FormatBitrate(CurrentTrack.Bitrate);
            CurrentTrackBaseDecibels = AudioTechnicalInfoHelper.FormatDecibels(CurrentTrack.BaseDecibels);
            OnPropertyChanged(nameof(AudioTechInfoVisibility));
        }

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

        // --- Cached ViewModels for Instant, Zero-Latency Navigation ---
        private HomeViewModel? _homeVm;
        private LibraryViewModel? _libraryVm;
        private ArtistsViewModel? _artistsVm;
        private AlbumsViewModel? _albumsVm;
        private PlaylistsViewModel? _playlistsVm;
        private EqualizerViewModel? _equalizerVm;

        public HomeViewModel HomeVm => _homeVm ??= new HomeViewModel(_dbService);
        public LibraryViewModel LibraryVm => _libraryVm ??= new LibraryViewModel(_dbService);
        public ArtistsViewModel ArtistsVm => _artistsVm ??= new ArtistsViewModel(_dbService);
        public AlbumsViewModel AlbumsVm => _albumsVm ??= new AlbumsViewModel(_dbService);
        public PlaylistsViewModel PlaylistsVm => _playlistsVm ??= new PlaylistsViewModel(_dbService);
        public EqualizerViewModel EqualizerVm => _equalizerVm ??= new EqualizerViewModel(_audioEngine, _settingsService);

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
        public ICommand ToggleNowPlayingQueueCommand { get; }
        public ICommand ToggleLyricsCommand { get; }
        public ICommand AddTrackToPlaylistCommand { get; }

        public MainViewModel()
        {
            _instance = this;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _dbService = new DatabaseService();
            _libService = new LibraryService(_dbService);
            _settingsService = new SettingsService();
            _audioEngine = new BassAudioEngine();
            _smtcService = new SmtcService();

            var savedSettings = _settingsService.LoadSettings();
            _volume = savedSettings.Volume * 100.0;
            _audioEngine.SetVolume(savedSettings.Volume);
            _audioEngine.CrossfadeEnabled = savedSettings.CrossfadeEnabled;
            _audioEngine.CrossfadeDurationSeconds = savedSettings.CrossfadeDurationSeconds;
            _isLightTheme = !savedSettings.IsDarkMode;
            _showAudioTechnicalInfo = savedSettings.ShowAudioTechnicalInfo;
            RefreshNavBrushes();

            // Wire SMTC Events
            _smtcService.PlayRequested += () =>
            {
                if (!IsPlaying && CurrentTrack != null) PlayPauseCommand?.Execute(null);
            };
            _smtcService.PauseRequested += () =>
            {
                if (IsPlaying) PlayPauseCommand?.Execute(null);
            };
            _smtcService.NextRequested += () =>
            {
                NextCommand?.Execute(null);
            };
            _smtcService.PreviousRequested += () =>
            {
                PreviousCommand?.Execute(null);
            };

            // Timer for tracking playback progression, lyrics synchronization, and crossfade trigger
            _timer = _dispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(150);
            _timer.Tick += (s, e) =>
            {
                if (_audioEngine.IsPlaying)
                {
                    CurrentPositionSeconds = _audioEngine.CurrentPosition.TotalSeconds;
                    _audioEngine.CheckCrossfadeProgress();

                    if (IsLyricsVisible && CurrentLyricsLines.Count > 0)
                    {
                        UpdateActiveLyric();
                    }
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

            // Setup crossfade transition before track finishes
            _audioEngine.TrackCrossfadeTriggered += (s, e) =>
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    if (IsRepeat && CurrentTrack != null)
                    {
                        await PlayTrackAsync(CurrentTrack, isCrossfade: true);
                    }
                    else
                    {
                        await PlayNextAsync(isCrossfade: true);
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
                CurrentView = HomeVm;
            });

            NavigateSongsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Canciones";
                CurrentView = LibraryVm;
            });

            NavigateArtistsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Artistas";
                CurrentView = ArtistsVm;
            });

            NavigateAlbumsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Álbumes";
                CurrentView = AlbumsVm;
            });

            NavigatePlaylistsCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Playlists";
                CurrentView = PlaylistsVm;
            });

            NavigateEqualizerCommand = new RelayCommand(() =>
            {
                CurrentSectionTitle = "Ecualizador";
                CurrentView = EqualizerVm;
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

            OpenNowPlayingCommand = new RelayCommand(() =>
            {
                IsNowPlayingQueueVisible = false; // Initially collapsed per specifications
                IsNowPlayingExpanded = true;
            });
            CloseNowPlayingCommand = new RelayCommand(() =>
            {
                IsNowPlayingExpanded = false;
                ExitFullScreenIfActive?.Invoke();
            });
            ToggleNowPlayingQueueCommand = new RelayCommand(() =>
            {
                IsNowPlayingQueueVisible = !IsNowPlayingQueueVisible;
            });
            ToggleLyricsCommand = new RelayCommand(() =>
            {
                IsLyricsVisible = !IsLyricsVisible;
            });
            ToggleKaraokeCommand = new RelayCommand(async () =>
            {
                await ToggleKaraokeAsync();
            });

            AddTrackToPlaylistCommand = new RelayCommand(async param =>
            {
                if (param is (Playlist playlist, Track track))
                {
                    await _dbService.AddTrackToPlaylistAsync(playlist.Name, track);
                    await LoadGlobalPlaylistsAsync();
                }
            });

            // Set initial view to Home
            CurrentSectionTitle = "Inicio";
            CurrentView = HomeVm;
            RefreshNavBrushes();
            _ = LoadGlobalPlaylistsAsync();

            KaraokeService.TrackKaraokeReady += (s, track) =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (CurrentTrack?.Id == track.Id)
                    {
                        UpdateKaraokeUiProperties();
                    }
                });
            };

            _ = Task.Run(async () =>
            {
                var allTracks = await _dbService.GetAllTracksAsync();
                KaraokeService.EnqueueTracksForPreload(allTracks);
            });
        }

        public void NavigateToArtist(string artistName)
        {
            CurrentSectionTitle = artistName;
            CurrentView = new ArtistDetailViewModel(_dbService, artistName);
        }

        public void NavigateToAlbum(string albumTitle, string? artistName = null)
        {
            CurrentSectionTitle = albumTitle;
            CurrentView = new AlbumDetailViewModel(_dbService, albumTitle, artistName);
        }

        public void NavigateToPlaylist(Playlist playlist)
        {
            CurrentSectionTitle = playlist.Name;
            CurrentView = new PlaylistDetailViewModel(_dbService, playlist);
        }

        public void NavigateToPlaylists()
        {
            CurrentSectionTitle = "Playlists";
            CurrentView = PlaylistsVm;
        }

        public void NavigateToAlbums()
        {
            CurrentSectionTitle = "Álbumes";
            CurrentView = AlbumsVm;
        }

        public void NavigateToArtists()
        {
            CurrentSectionTitle = "Artistas";
            CurrentView = ArtistsVm;
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

        public async Task PlayTrackAsync(Track track, List<Track>? contextQueue = null, bool isCrossfade = false)
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

            if (IsKaraokeProcessing)
            {
                _karaokeCts?.Cancel();
                IsKaraokeProcessing = false;
            }

            string fileToPlay = track.FilePath;
            if (IsKaraokeActive)
            {
                string? instPath = KaraokeService.GetInstrumentalPath(track);
                if (!string.IsNullOrEmpty(instPath) && File.Exists(instPath))
                {
                    fileToPlay = instPath;
                }
                else
                {
                    IsKaraokeActive = false;
                }
            }

            await _audioEngine.LoadFileAsync(fileToPlay, isCrossfade);
            if (!isCrossfade)
            {
                await _audioEngine.PlayAsync();
            }

            IsPlaying = true;
            _timer.Start();

            _smtcService?.UpdateMetadata(track, true);
            _ = _dbService.RecordTrackPlayAsync(track);
            _homeVm?.OnTrackPlayedRealtime(track);

            // Proactively prioritize current track & upcoming track for instant Karaoke
            KaraokeService.PrioritizeTrack(track);
            if (CurrentQueue != null && CurrentQueueIndex >= 0 && CurrentQueueIndex + 1 < CurrentQueue.Count)
            {
                var upcoming = CurrentQueue[CurrentQueueIndex + 1];
                KaraokeService.PrioritizeTrack(upcoming);
            }
        }

        public async Task PlayNextAsync(bool isCrossfade = false)
        {
            if (CurrentQueue == null || CurrentQueue.Count == 0)
            {
                // If queue is empty, pick a random song from library
                var allLibTracks = await _dbService.GetAllTracksAsync();
                if (allLibTracks.Count > 0)
                {
                    var randomTrack = allLibTracks[Random.Shared.Next(allLibTracks.Count)];
                    CurrentQueue = new ObservableCollection<Track> { randomTrack };
                    CurrentQueueIndex = 0;
                    await PlayTrackAsync(randomTrack, isCrossfade: isCrossfade);
                }
                return;
            }

            if (IsShuffle)
            {
                CurrentQueueIndex = Random.Shared.Next(CurrentQueue.Count);
            }
            else
            {
                // Infinite playback: when reaching the last song in an album/playlist/artist, pick a random track from library so music never stops
                if (CurrentQueueIndex + 1 >= CurrentQueue.Count)
                {
                    var allLibTracks = await _dbService.GetAllTracksAsync();
                    if (allLibTracks.Count > 0)
                    {
                        var candidates = allLibTracks.Where(t => t.FilePath != CurrentTrack?.FilePath).ToList();
                        if (candidates.Count == 0) candidates = allLibTracks;
                        var nextRandomTrack = candidates[Random.Shared.Next(candidates.Count)];

                        CurrentQueue.Add(nextRandomTrack);
                        CurrentQueueIndex = CurrentQueue.Count - 1;
                    }
                    else
                    {
                        CurrentQueueIndex = 0;
                    }
                }
                else
                {
                    CurrentQueueIndex++;
                }
            }

            if (CurrentQueueIndex >= 0 && CurrentQueueIndex < CurrentQueue.Count)
            {
                var nextTrack = CurrentQueue[CurrentQueueIndex];
                await PlayTrackAsync(nextTrack, isCrossfade: isCrossfade);
            }
        }

        public void AddTrackNext(Track track)
        {
            if (track == null) return;
            int insertIndex = CurrentQueueIndex >= 0 && CurrentQueueIndex < CurrentQueue.Count
                ? CurrentQueueIndex + 1
                : CurrentQueue.Count;
            CurrentQueue.Insert(insertIndex, track);
        }

        public void AddTrackToQueue(Track track)
        {
            if (track == null) return;
            CurrentQueue.Add(track);
            if (CurrentTrack == null)
            {
                _ = PlayTrackAsync(track);
            }
        }

        public void RemoveTrackFromQueue(Track track)
        {
            if (track == null) return;
            CurrentQueue.Remove(track);
            SyncQueueAfterReorder();
        }

        public void ClearQueue()
        {
            var cur = CurrentTrack;
            CurrentQueue.Clear();
            if (cur != null)
            {
                CurrentQueue.Add(cur);
                CurrentQueueIndex = 0;
            }
            else
            {
                CurrentQueueIndex = -1;
            }
        }

        public void SyncQueueAfterReorder()
        {
            if (CurrentTrack != null)
            {
                CurrentQueueIndex = CurrentQueue.IndexOf(CurrentTrack);
            }
            else
            {
                CurrentQueueIndex = -1;
            }
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
            if (IsLyricsVisible && CurrentLyricsLines.Count > 0)
            {
                UpdateActiveLyric();
            }
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
            if (_libraryVm != null) await _libraryVm.LoadLibraryAsync();
            if (_homeVm != null) await _homeVm.LoadSuggestionsAsync();
            if (_albumsVm != null) await _albumsVm.LoadAlbumsAsync();
            if (_artistsVm != null) await _artistsVm.LoadArtistsAsync();
            if (_playlistsVm != null) await _playlistsVm.LoadPlaylistsAsync();

            _ = Task.Run(async () =>
            {
                var allTracks = await _dbService.GetAllTracksAsync();
                KaraokeService.EnqueueTracksForPreload(allTracks);
            });
        }

        public void NotifyArtistPhotoChanged(string artistName, string photoPath)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _artistsVm?.NotifyArtistPhotoChanged(artistName, photoPath);
            });
        }

        public void NotifyLyricsChanged(Track track)
        {
            if (track == null) return;
            _dispatcherQueue.TryEnqueue(async () =>
            {
                if (CurrentTrack != null && (CurrentTrack.Id == track.Id || (!string.IsNullOrEmpty(CurrentTrack.FilePath) && CurrentTrack.FilePath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase))))
                {
                    await LoadLyricsForCurrentTrackAsync();
                }
            });
        }

        public void NotifyTrackMetadataChanged(Track updatedTrack, bool lyricsUpdated = false)
        {
            if (updatedTrack == null) return;

            _dispatcherQueue.TryEnqueue(async () =>
            {
                // 1. CurrentTrack (Now Playing Bar, Overlay, Background Blur)
                if (CurrentTrack != null && (CurrentTrack.Id == updatedTrack.Id || (!string.IsNullOrEmpty(CurrentTrack.FilePath) && CurrentTrack.FilePath.Equals(updatedTrack.FilePath, StringComparison.OrdinalIgnoreCase))))
                {
                    CurrentTrack.Title = updatedTrack.Title;
                    CurrentTrack.Artist = updatedTrack.Artist;
                    CurrentTrack.Album = updatedTrack.Album;
                    CurrentTrack.Genre = updatedTrack.Genre;
                    CurrentTrack.Year = updatedTrack.Year;
                    CurrentTrack.TrackNumber = updatedTrack.TrackNumber;

                    if (!string.IsNullOrEmpty(updatedTrack.CoverImagePath))
                    {
                        CurrentTrack.CoverImagePath = updatedTrack.CoverImagePath;
                        await UpdateBlurredCoverAsync(CurrentTrack);
                    }

                    OnPropertyChanged(nameof(CurrentTrack));
                    UpdateCurrentTrackTechnicalInfo();
                    _smtcService?.UpdateMetadata(CurrentTrack, IsPlaying);

                    if (lyricsUpdated)
                    {
                        await LoadLyricsForCurrentTrackAsync();
                    }
                }

                // 2. Update in CurrentQueue
                foreach (var qTrack in CurrentQueue)
                {
                    if (qTrack.Id == updatedTrack.Id || (!string.IsNullOrEmpty(qTrack.FilePath) && qTrack.FilePath.Equals(updatedTrack.FilePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        qTrack.Title = updatedTrack.Title;
                        qTrack.Artist = updatedTrack.Artist;
                        qTrack.Album = updatedTrack.Album;
                        qTrack.Genre = updatedTrack.Genre;
                        qTrack.Year = updatedTrack.Year;
                        qTrack.TrackNumber = updatedTrack.TrackNumber;
                        if (!string.IsNullOrEmpty(updatedTrack.CoverImagePath))
                        {
                            qTrack.CoverImagePath = updatedTrack.CoverImagePath;
                        }
                    }
                }

                // 3. Update Home ViewModel real-time
                _homeVm?.NotifyTrackMetadataChanged(updatedTrack);

                // 4. Reload other active/cached views in the background
                if (_libraryVm != null) await _libraryVm.LoadLibraryAsync();
                if (_albumsVm != null) await _albumsVm.LoadAlbumsAsync();
                if (_artistsVm != null) await _artistsVm.LoadArtistsAsync();
                if (_playlistsVm != null) await _playlistsVm.LoadPlaylistsAsync();

                // 5. If viewing detail views, refresh them too
                if (CurrentView is AlbumDetailViewModel adVm)
                {
                    await adVm.LoadAlbumDetailsAsync();
                }
                else if (CurrentView is ArtistDetailViewModel artVm)
                {
                    await artVm.LoadArtistDetailsAsync();
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

