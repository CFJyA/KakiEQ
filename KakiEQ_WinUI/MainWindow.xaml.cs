using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using Polyline = Microsoft.UI.Xaml.Shapes.Polyline;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        public MainViewModel ViewModel => MainViewModel.Instance ?? new MainViewModel();

        public void OpenKronatlCodex(KronatlSeason season)
        {
            CodexOverlay?.Open(season);
        }

        private Services.SystemIntegration.TrayIconService? _trayService;
        private Views.SettingsWindow? _settingsWindow;
        private Views.MetadataWindow? _metadataWindow;
        private Views.MiniPlayerWindow? _miniPlayerWindow;
        private bool _isExiting = false;
        private OverlappedPresenterState _lastWindowState = OverlappedPresenterState.Restored;
        private readonly System.Collections.Generic.Dictionary<Type, Page> _persistentPages = new();
        private FrameworkElement? _activePageElement;

        // Now Playing real-time FFT spectrum analyzer fields
        private const int NpSpectrumBandCount = 36;
        private readonly float[] _npRawSpectrum = new float[NpSpectrumBandCount];
        private readonly float[] _npSmoothSpectrum = new float[NpSpectrumBandCount];
        private readonly System.Collections.Generic.List<Microsoft.UI.Xaml.Shapes.Rectangle> _npSpectrumBars = new();
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _nowPlayingSpectrumTimer;

        // Interactive 2D Harmonic Constellation Map state (Retained-Mode 120 FPS Scene)
        private sealed class ConstellationNode
        {
            public Track Track { get; init; } = null!;
            public string NormalizedCamelot { get; init; } = string.Empty;
            public int CamelotNumber { get; init; }
            public char CamelotLetter { get; init; }
            public double EffectiveBpm { get; init; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Radius { get; set; }
            public SolidColorBrush FillBrush { get; init; } = null!;
            public Ellipse? StarEllipse { get; set; }
            public Border? StepBadge { get; set; }
            public TextBlock? StepBadgeText { get; set; }
            public bool IsInRoute { get; set; }
            public int RouteStep { get; set; }
        }

        private static readonly SolidColorBrush ConstWhiteStrokeBrush = new(Color.FromArgb(255, 255, 255, 255));
        private static readonly SolidColorBrush ConstCyanStrokeBrush = new(Color.FromArgb(255, 3, 218, 198));
        private static readonly SolidColorBrush ConstDimStarStrokeBrush = new(Color.FromArgb(110, 255, 255, 255));
        private static readonly SolidColorBrush ConstRayStrongBrush = new(Color.FromArgb(95, 3, 218, 198));
        private static readonly SolidColorBrush ConstRaySoftBrush = new(Color.FromArgb(42, 3, 218, 198));
        private static readonly SolidColorBrush ConstRouteLineBrush = new(Color.FromArgb(230, 187, 134, 252));
        private static readonly SolidColorBrush ConstDragLineBrush = new(Color.FromArgb(180, 3, 218, 198));
        private static readonly SolidColorBrush ConstHaloFillBrush = new(Color.FromArgb(45, 187, 134, 252));
        private static readonly SolidColorBrush ConstHaloStrokeBrush = new(Color.FromArgb(210, 187, 134, 252));
        private static readonly SolidColorBrush ConstStepBadgeBgBrush = new(Color.FromArgb(235, 14, 14, 22));
        private static readonly SolidColorBrush ConstStepBadgeBorderBrush = new(Color.FromArgb(255, 187, 134, 252));
        private static readonly SolidColorBrush ConstStepBadgeFgBrush = new(Colors.White);
        private static readonly SolidColorBrush ConstRing120Brush = new(Color.FromArgb(36, 255, 255, 255));
        private static readonly SolidColorBrush ConstRingDimBrush = new(Color.FromArgb(20, 255, 255, 255));
        private static readonly SolidColorBrush ConstRingLabelBrush = new(Color.FromArgb(110, 180, 180, 200));
        private static readonly SolidColorBrush ConstCompatHarmBgBrush = new(Color.FromArgb(44, 3, 218, 198));
        private static readonly SolidColorBrush ConstCompatHarmFgBrush = new(Color.FromArgb(255, 3, 218, 198));
        private static readonly SolidColorBrush ConstCompatCreativeBgBrush = new(Color.FromArgb(40, 253, 203, 110));
        private static readonly SolidColorBrush ConstCompatCreativeFgBrush = new(Color.FromArgb(255, 253, 203, 110));
        private static readonly SolidColorBrush ConstCompatPlayingBgBrush = new(Color.FromArgb(48, 187, 134, 252));
        private static readonly SolidColorBrush ConstCompatPlayingFgBrush = new(Color.FromArgb(255, 187, 134, 252));

        private const int MaxConstellationRays = 28;
        private readonly List<ConstellationNode> _constellationNodes = new();
        private readonly List<Track> _constellationRoute = new();
        private readonly List<Line> _constellationRayPool = new();
        private Polyline? _constellationRoutePolyline;
        private Line? _constellationDragLine;
        private Ellipse? _constellationCurrentHalo;
        private ConstellationNode? _activePlayingConstellationNode;
        private ConstellationNode? _hoveredConstellationNode;
        private bool _isDraggingConstellationRoute;
        private bool _constellationAddedNodeOnPress;
        private Point _constellationPointerPos;
        private Point _constellationPressStartPos;
        private ConstellationNode? _constellationPressedNode;
        private bool _isLoadingConstellationData;
        private double _lastConstellationWidth;
        private double _lastConstellationHeight;
        private bool _sidebarAutoCollapsed;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            // Setup Custom TitleBar with Native Windows Caption Buttons
            // Use AppTitleBarDragRegion (Columns 0 & 1) so Column 2 buttons (Metadatos & Ajustes) receive full pointer hover events
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBarDragRegion);

            // Configure Window Size and Position
            var appWindow = GetAppWindow();
            if (appWindow != null)
            {
                appWindow.Resize(new SizeInt32(1150, 760));
                UpdateWindowIcon(appWindow);

                var settings = ViewModel.SettingsService.LoadSettings();
                bool isLight = !settings.IsDarkMode;
                if (Content is FrameworkElement fe)
                {
                    fe.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;
                }
                UpdateThemeVisuals(isLight);

                // Intercept close event to offer background execution
                appWindow.Closing += AppWindow_Closing;

                if (appWindow.Presenter is OverlappedPresenter initialOp)
                {
                    _lastWindowState = initialOp.State;
                }

                // Sync FullScreen state, Maximized state, and trigger smooth Window State Reflow Animation
                appWindow.Changed += (s, args) =>
                {
                    if (args.DidPresenterChange)
                    {
                        ViewModel.IsFullScreen = (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen);
                    }
                    if (appWindow.Presenter is OverlappedPresenter op)
                    {
                        var currentState = op.State;
                        bool isMax = (currentState == OverlappedPresenterState.Maximized);
                        ViewModel.IsWindowMaximized = isMax;

                        if (currentState != _lastWindowState)
                        {
                            _lastWindowState = currentState;
                            if (currentState == OverlappedPresenterState.Maximized || currentState == OverlappedPresenterState.Restored)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    Helpers.FluidAnimationHelper.PlayWindowStateReflowAnimation(_activePageElement ?? ContentFrame.Content as FrameworkElement, isMax);
                                    foreach (var mountedPage in _persistentPages.Values)
                                    {
                                        if (!ReferenceEquals(mountedPage, _activePageElement))
                                        {
                                            Helpers.FluidAnimationHelper.PlayWindowStateReflowAnimation(mountedPage, isMax);
                                            Helpers.FluidAnimationHelper.SetPageActiveState(mountedPage, false, false);
                                        }
                                    }
                                });
                            }
                        }
                    }
                };

                ViewModel.ExitFullScreenIfActive = () =>
                {
                    if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                    {
                        appWindow.SetPresenter(AppWindowPresenterKind.Default);
                        ViewModel.IsFullScreen = false;
                    }
                };
            }

            // Smooth auto-scroll synchronized lyrics to center active line
            ViewModel.LyricLineChanged += (s, activeIndex) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ScrollToActiveLyric(activeIndex);
                });
            };

            // Initialize System Tray Icon
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            _trayService = new Services.SystemIntegration.TrayIconService(hwnd);
            _trayService.OpenRequested += RestoreFromTray;
            _trayService.PlayPauseRequested += () => ViewModel.PlayPauseCommand.Execute(null);
            _trayService.NextRequested += () => ViewModel.NextCommand.Execute(null);
            _trayService.PreviousRequested += () => ViewModel.PreviousCommand.Execute(null);
            _trayService.ExitRequested += ExitApplication;

            // Monitor ViewModel changes for Frame Page navigation, fluid overlays, and tray tooltip
            ViewModel.PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.CurrentView))
                {
                    NavigateToView(ViewModel.CurrentView);
                }
                else if (e.PropertyName == nameof(ViewModel.IsNowPlayingExpanded))
                {
                    if (ViewModel.IsNowPlayingExpanded)
                    {
                        Helpers.FluidAnimationHelper.AnimateOverlayOpen(NowPlayingOverlay);
                        StartNowPlayingSpectrumTimer();
                        if (ViewModel.IsConstellationVisible)
                        {
                            _ = RefreshConstellationDataAndRenderAsync();
                        }
                    }
                    else
                    {
                        _nowPlayingSpectrumTimer?.Stop();
                        await Helpers.FluidAnimationHelper.AnimateOverlayCloseAsync(NowPlayingOverlay, 175);
                    }
                }
                else if (e.PropertyName == nameof(ViewModel.IsNowPlayingQueueVisible))
                {
                    Helpers.FluidAnimationHelper.AnimateDrawerToggle(NowPlayingQueueDrawer, ViewModel.IsNowPlayingQueueVisible);
                }
                else if (e.PropertyName == nameof(ViewModel.IsLyricsVisible) || e.PropertyName == nameof(ViewModel.IsConstellationVisible))
                {
                    FrameworkElement activePanel = ViewModel.IsConstellationVisible
                        ? NowPlayingConstellationPanel
                        : (ViewModel.IsLyricsVisible ? NowPlayingLyricsPanel : NowPlayingCoverPanel);
                    Helpers.FluidAnimationHelper.PlayPageEntranceAnimation(activePanel);
                    if (ViewModel.IsConstellationVisible)
                    {
                        _ = RefreshConstellationDataAndRenderAsync();
                    }
                }
                else if (e.PropertyName == nameof(ViewModel.IsSidebarExpanded))
                {
                    if (ViewModel.IsSidebarExpanded)
                    {
                        Helpers.FluidAnimationHelper.PlayPageEntranceAnimation(SidebarContainer);
                    }
                }
                else if (e.PropertyName == nameof(ViewModel.CurrentTrack))
                {
                    var track = ViewModel.CurrentTrack;
                    if (track != null)
                    {
                        _trayService.UpdateTooltip($"Kaki EQ: {track.Title} - {track.Artist}");
                    }
                    else
                    {
                        _trayService.UpdateTooltip("Kaki EQ - Music Player");
                    }

                    if (ViewModel.IsConstellationVisible)
                    {
                        _ = RefreshConstellationDataAndRenderAsync();
                    }
                }
            };

            // Navigate to initial Page
            NavigateToView(ViewModel.CurrentView);

            // Pre-mount primary pages in the visual tree during idle ticks so first switch is 0ms
            _ = PrewarmPrimaryPagesAsync();

            // Check if initial music folder needs to be selected
            _ = CheckInitialMusicFolderAsync();
        }

        private Page? GetOrMountPrimaryPage(Type pageType, object viewModel)
        {
            if (PagesHostGrid == null) return null;

            if (_persistentPages.TryGetValue(pageType, out var existingPage))
            {
                if (!ReferenceEquals(existingPage.DataContext, viewModel))
                {
                    existingPage.DataContext = viewModel;
                }
                return existingPage;
            }

            Page? newPage = pageType switch
            {
                Type t when t == typeof(Views.HomeView) => new Views.HomeView { DataContext = viewModel },
                Type t when t == typeof(Views.LibraryView) => new Views.LibraryView { DataContext = viewModel },
                Type t when t == typeof(Views.AlbumsView) => new Views.AlbumsView { DataContext = viewModel },
                Type t when t == typeof(Views.ArtistsView) => new Views.ArtistsView { DataContext = viewModel },
                Type t when t == typeof(Views.PlaylistsView) => new Views.PlaylistsView { DataContext = viewModel },
                Type t when t == typeof(Views.EqualizerView) => new Views.EqualizerView { DataContext = viewModel },
                Type t when t == typeof(Views.MultiOutputView) => new Views.MultiOutputView { DataContext = viewModel },
                Type t when t == typeof(Views.KronatlView) => new Views.KronatlView { DataContext = viewModel },
                _ => null
            };

            if (newPage != null)
            {
                newPage.HorizontalAlignment = HorizontalAlignment.Stretch;
                newPage.VerticalAlignment = VerticalAlignment.Stretch;
                Helpers.FluidAnimationHelper.SetPageActiveState(newPage, false, false);
                _persistentPages[pageType] = newPage;
                PagesHostGrid.Children.Add(newPage);
            }

            return newPage;
        }

        private async Task PrewarmPrimaryPagesAsync()
        {
            await Task.Delay(320);

            (Type pageType, Func<object> getVm)[] pagesToWarm = new (Type, Func<object>)[]
            {
                (typeof(Views.LibraryView), () => ViewModel.LibraryVm),
                (typeof(Views.ArtistsView), () => ViewModel.ArtistsVm),
                (typeof(Views.AlbumsView), () => ViewModel.AlbumsVm),
                (typeof(Views.PlaylistsView), () => ViewModel.PlaylistsVm),
                (typeof(Views.KronatlView), () => ViewModel.KronatlVm),
            };

            foreach (var (pageType, getVm) in pagesToWarm)
            {
                await Task.Delay(90);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (!_persistentPages.ContainsKey(pageType))
                    {
                        var page = GetOrMountPrimaryPage(pageType, getVm());
                        if (page != null && !ReferenceEquals(_activePageElement, page))
                        {
                            Helpers.FluidAnimationHelper.SetPageActiveState(page, false, false);
                        }
                    }
                });
            }
        }

        private void NavigateToView(object? viewModel)
        {
            if (viewModel == null || PagesHostGrid == null || ContentFrame == null) return;

            bool isDetailView = viewModel is AlbumDetailViewModel or ArtistDetailViewModel or PlaylistDetailViewModel;

            if (!isDetailView)
            {
                Type? primaryPageType = viewModel switch
                {
                    HomeViewModel => typeof(Views.HomeView),
                    LibraryViewModel => typeof(Views.LibraryView),
                    AlbumsViewModel => typeof(Views.AlbumsView),
                    ArtistsViewModel => typeof(Views.ArtistsView),
                    PlaylistsViewModel => typeof(Views.PlaylistsView),
                    EqualizerViewModel => typeof(Views.EqualizerView),
                    MultiOutputViewModel => typeof(Views.MultiOutputView),
                    KronatlViewModel => typeof(Views.KronatlView),
                    _ => null
                };

                if (primaryPageType == null) return;

                var targetPage = GetOrMountPrimaryPage(primaryPageType, viewModel);
                if (targetPage == null) return;

                if (ReferenceEquals(_activePageElement, targetPage) && ContentFrame.Visibility == Visibility.Collapsed)
                {
                    return;
                }

                // Hide Detail Frame without unloading primary pages
                if (ContentFrame.Visibility == Visibility.Visible)
                {
                    Helpers.FluidAnimationHelper.SetPageActiveState(ContentFrame, false, false);
                    ContentFrame.Visibility = Visibility.Collapsed;
                }

                // Deactivate other primary pages while keeping them mounted in the visual tree
                foreach (var kvp in _persistentPages)
                {
                    if (!ReferenceEquals(kvp.Value, targetPage))
                    {
                        Helpers.FluidAnimationHelper.SetPageActiveState(kvp.Value, false, false);
                    }
                }

                // Activate target primary page with zero visual tree rebuild
                _activePageElement = targetPage;
                Helpers.FluidAnimationHelper.SetPageActiveState(targetPage, true, animateEntrance: true);

                if (viewModel is KronatlViewModel kronatlVm)
                {
                    _ = kronatlVm.LoadSeasonsAsync(silent: true);
                }
                return;
            }

            Type? detailPageType = viewModel switch
            {
                AlbumDetailViewModel => typeof(Views.AlbumDetailView),
                ArtistDetailViewModel => typeof(Views.ArtistDetailView),
                PlaylistDetailViewModel => typeof(Views.PlaylistDetailView),
                _ => null
            };

            if (detailPageType == null) return;

            // Keep all primary pages mounted in the visual tree (zero container teardown!) while hiding them visually
            foreach (var kvp in _persistentPages)
            {
                Helpers.FluidAnimationHelper.SetPageActiveState(kvp.Value, false, false);
            }

            ContentFrame.Visibility = Visibility.Visible;
            _activePageElement = ContentFrame;
            Helpers.FluidAnimationHelper.SetPageActiveState(ContentFrame, true, animateEntrance: true);

            ContentFrame.Navigate(
                detailPageType,
                viewModel,
                new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        }

        public AppWindow? GetAppWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        public bool IsMaximized
        {
            get
            {
                var appWindow = GetAppWindow();
                if (appWindow?.Presenter is OverlappedPresenter op)
                {
                    return op.State == OverlappedPresenterState.Maximized;
                }
                return false;
            }
        }

        private async Task CheckInitialMusicFolderAsync()
        {
            await Task.Delay(500); // Give UI time to render

            var settings = ViewModel.SettingsService.LoadSettings();
            if (string.IsNullOrEmpty(settings.MusicFolderPath) || !Directory.Exists(settings.MusicFolderPath))
            {
                // Try default Windows Music folder first
                string myMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (Directory.Exists(myMusic) && Directory.GetFiles(myMusic, "*.*", SearchOption.AllDirectories).Length > 0)
                {
                    await ViewModel.ScanLibraryFolderAsync(myMusic);
                }
            }
            else
            {
                // Scan saved music folder in background
                _ = ViewModel.ScanLibraryFolderAsync(settings.MusicFolderPath);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExiting) return;

            args.Cancel = true;

            var settings = ViewModel.SettingsService.LoadSettings();
            if (settings.RunInBackgroundPreference == "Background")
            {
                sender.Hide();
                _trayService?.EnsureCreated();
                return;
            }
            if (settings.RunInBackgroundPreference == "Exit")
            {
                ExitApplication();
                return;
            }

            // Ask user whether to exit or run in background
            var rememberCheckBox = new CheckBox
            {
                Content = "Recordar mi elección para siempre",
                Margin = new Thickness(0, 14, 0, 0),
                FontSize = 12
            };

            var contentStack = new StackPanel { Spacing = 8, MaxWidth = 420 };
            contentStack.Children.Add(new TextBlock
            {
                Text = "¿Deseas cerrar Kaki EQ o mantenerlo reproduciéndose en segundo plano?",
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            contentStack.Children.Add(new TextBlock
            {
                Text = "Al ejecutarse en segundo plano, la música seguirá sonando y podrás controlarla desde la bandeja del sistema (área de iconos ocultos) y desde el control de medios nativo de Windows.",
                FontSize = 11,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170)),
                TextWrapping = TextWrapping.Wrap
            });
            contentStack.Children.Add(rememberCheckBox);

            var dialog = new ContentDialog
            {
                Title = "¿Cerrar o Segundo Plano?",
                Content = contentStack,
                PrimaryButtonText = "Segundo plano",
                SecondaryButtonText = "Cerrar aplicación",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            ContentDialogResult result = ContentDialogResult.None;
            try
            {
                result = await dialog.ShowAsync();
            }
            catch { }
            if (result == ContentDialogResult.Primary)
            {
                if (rememberCheckBox.IsChecked == true)
                {
                    settings.RunInBackgroundPreference = "Background";
                    ViewModel.SettingsService.SaveSettings(settings);
                }

                sender.Hide();
                _trayService?.EnsureCreated();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                if (rememberCheckBox.IsChecked == true)
                {
                    settings.RunInBackgroundPreference = "Exit";
                    ViewModel.SettingsService.SaveSettings(settings);
                }

                ExitApplication();
            }
        }

        public void RestoreFromTray()
        {
            var appWindow = GetAppWindow();
            if (appWindow != null)
            {
                appWindow.Show();
                IntPtr hwnd = WindowNative.GetWindowHandle(this);
                SetForegroundWindow(hwnd);
            }
        }

        public void ExitApplication()
        {
            _isExiting = true;
            _trayService?.Dispose();
            if (_settingsWindow != null)
            {
                _settingsWindow.AllowClose = true;
                _settingsWindow.Close();
            }
            if (_metadataWindow != null)
            {
                _metadataWindow.AllowClose = true;
                _metadataWindow.Close();
            }
            _miniPlayerWindow?.Close();
            Application.Current.Exit();
        }

        public void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new Views.SettingsWindow();
                _settingsWindow.Closed += (s, ev) => _settingsWindow = null;
            }
            _settingsWindow.ShowWindow();
        }

        public void OpenMetadata_Click(object sender, RoutedEventArgs e)
        {
            OpenMetadataWindow(ViewModel.CurrentTrack);
        }

        public void OpenMetadataWindow(Track? track = null)
        {
            if (_metadataWindow == null)
            {
                _metadataWindow = new Views.MetadataWindow();
                _metadataWindow.Closed += (s, ev) => _metadataWindow = null;
            }
            _metadataWindow.ShowWindow(track);
        }

        public void OpenMiniPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (Views.MiniPlayerWindow.CurrentInstance != null)
            {
                Views.MiniPlayerWindow.CurrentInstance.BringToForeground();
                return;
            }


            _miniPlayerWindow = new Views.MiniPlayerWindow(ViewModel.CurrentTrack?.FilePath);
            _miniPlayerWindow.Closed += (s, ev) => _miniPlayerWindow = null;
            _miniPlayerWindow.Activate();
            _miniPlayerWindow.BringToForeground();
        }

        private async void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                await ViewModel.ScanLibraryFolderAsync(folder.Path);
            }
        }

        private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider && Math.Abs(e.NewValue - ViewModel.CurrentPositionSeconds) > 1.8)
            {
                ViewModel.SeekTo(e.NewValue);
            }
        }

        private void PlayerAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var track = ViewModel?.CurrentTrack;
            if (track == null || sender is not FrameworkElement anchor) return;
            Helpers.PlaylistDialogHelper.ShowAddToPlaylistFlyout(anchor, track, this.Content.XamlRoot);
        }

        private void QueueItem_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Track track)
            {
                _ = ViewModel.PlayTrackAsync(track);
            }
        }

        private void Queue_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            ViewModel.SyncQueueAfterReorder();
        }

        private void RemoveQueueTrack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Track track)
            {
                ViewModel.RemoveTrackFromQueue(track);
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearQueue();
        }

        private async void SaveQueueAsPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.CurrentQueue == null || ViewModel.CurrentQueue.Count == 0)
            {
                ShowToast("La cola de reproducción está vacía", "\uE783");
                return;
            }

            var playlist = await Helpers.PlaylistDialogHelper.ShowCreatePlaylistDialogAsync(
                this.Content.XamlRoot,
                initialTracksToAdd: ViewModel.CurrentQueue);

            if (playlist != null)
            {
                ShowToast($"Playlist '{playlist.Name}' guardada con éxito", "\uE73E");
            }
        }

        private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            var appWindow = GetAppWindow();
            if (appWindow != null)
            {
                if (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    appWindow.SetPresenter(AppWindowPresenterKind.Default);
                    ViewModel.IsFullScreen = false;
                }
                else
                {
                    appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                    ViewModel.IsFullScreen = true;
                }
            }
        }

        private void LyricLine_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is LrcLine line)
            {
                ViewModel.SeekTo(line.Timestamp.TotalSeconds);
            }
        }

        private ScrollViewer? _lyricsScrollViewer;
        private ListViewItem? _lastAnimatedLyricContainer;

        private void ScrollToActiveLyric(int activeIndex)
        {
            try
            {
                if (LyricsListView == null || activeIndex < 0 || activeIndex >= ViewModel.CurrentLyricsLines.Count)
                    return;

                if (_lyricsScrollViewer == null)
                {
                    _lyricsScrollViewer = FindChild<ScrollViewer>(LyricsListView);
                }

                var container = LyricsListView.ContainerFromIndex(activeIndex) as ListViewItem;
                if (_lastAnimatedLyricContainer != null && !ReferenceEquals(_lastAnimatedLyricContainer, container))
                {
                    Helpers.FluidAnimationHelper.AnimateLyricLineFocus(_lastAnimatedLyricContainer, false);
                }
                if (container != null)
                {
                    Helpers.FluidAnimationHelper.AnimateLyricLineFocus(container, true);
                    _lastAnimatedLyricContainer = container;
                }

                if (_lyricsScrollViewer != null)
                {
                    if (container != null && container.ActualHeight > 0)
                    {
                        var transform = container.TransformToVisual(_lyricsScrollViewer);
                        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                        double currentVisualY = point.Y;
                        double viewportHeight = _lyricsScrollViewer.ViewportHeight;
                        double desiredVisualY = (viewportHeight - container.ActualHeight) / 2.0;
                        double delta = currentVisualY - desiredVisualY;
                        double targetOffset = _lyricsScrollViewer.VerticalOffset + delta;
                        targetOffset = Math.Max(0, Math.Min(targetOffset, _lyricsScrollViewer.ScrollableHeight));

                        _lyricsScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
                    }
                    else
                    {
                        LyricsListView.ScrollIntoView(ViewModel.CurrentLyricsLines[activeIndex]);
                    }
                }
                else
                {
                    LyricsListView.ScrollIntoView(ViewModel.CurrentLyricsLines[activeIndex]);
                }
            }
            catch { }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void OpenLyricsMetadata_Click(object sender, RoutedEventArgs e)
        {
            var track = ViewModel.CurrentTrack;
            if (track == null) return;
            OpenMetadataWindow(track);
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            if (Content is FrameworkElement fe)
            {
                bool isCurrentlyLight = fe.RequestedTheme == ElementTheme.Light;
                bool newIsLight = !isCurrentlyLight;
                fe.RequestedTheme = newIsLight ? ElementTheme.Light : ElementTheme.Dark;
                ViewModel.IsLightTheme = newIsLight;
                UpdateThemeVisuals(newIsLight);
            }
        }

        private void UpdateThemeVisuals(bool isLight)
        {
            try
            {
                var logoUri = new Uri(isLight 
                    ? "ms-appx:///Assets/Kaki_Oficial_Negro.png" 
                    : "ms-appx:///Assets/Kaki_Oficial_Blanco.png");
                AppLogoImage.Source = new BitmapImage(logoUri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating logo image: {ex.Message}");
            }

            var appWindow = GetAppWindow();
            var titleBar = appWindow?.TitleBar;
            if (titleBar != null)
            {
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonForegroundColor = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                titleBar.ButtonHoverBackgroundColor = isLight ? Windows.UI.Color.FromArgb(30, 0, 0, 0) : Windows.UI.Color.FromArgb(30, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                titleBar.ButtonPressedBackgroundColor = isLight ? Windows.UI.Color.FromArgb(50, 0, 0, 0) : Windows.UI.Color.FromArgb(50, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = isLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveForegroundColor = isLight ? Windows.UI.Color.FromArgb(128, 0, 0, 0) : Windows.UI.Color.FromArgb(128, 255, 255, 255);
            }
        }

        private void UpdateWindowIcon(AppWindow appWindow)
        {
            try
            {
                bool isSystemLight = false;
                try
                {
                    var val = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", null);
                    if (val is int intVal && intVal == 1)
                    {
                        isSystemLight = true;
                    }
                }
                catch { }

                string iconFile = isSystemLight ? "Kaki_Oficial_Negro.ico" : "Kaki_Oficial_Blanco.ico";
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", iconFile);
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting window icon: {ex.Message}");
            }
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            bool isLight = ViewModel?.IsLightTheme ?? false;
            var textPrimaryBrush = isLight
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 240, 240, 240));
            var textSecondaryBrush = isLight
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 105, 105, 105))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180));
            var accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252));
            var cardBgBrush = isLight
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 244, 246))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 28, 32));
            var borderBrush = isLight
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 222, 222, 226))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 48, 54));

            var stack = new StackPanel { Spacing = 14, MaxWidth = 440 };

            stack.Children.Add(new TextBlock
            {
                Text = "Kaki EQ v2.0 - WinUI 3 Edition",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 16,
                Foreground = accentBrush
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Reproductor de audio de alta fidelidad con ecualización gráfica paramétrica DSP de 10 bandas y estética Material Design 3 (Itztli / Yolpaki).",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = textPrimaryBrush
            });

            stack.Children.Add(new TextBlock
            {
                Text = "• Motor de audio: ManagedBass (BASS DirectX)\n• Plataforma: Windows App SDK / WinUI 3 / .NET 8\n• Frecuencias: 32 Hz a 16 kHz (-15 dB a +15 dB)\n• Diseñado para Windows 11 con Mica Backdrop",
                FontSize = 12,
                Foreground = textSecondaryBrush
            });

            // Codex Yolpaki Developer Card
            var devCard = new Border
            {
                Background = cardBgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 6, 0, 0)
            };

            var devGrid = new Grid { ColumnSpacing = 16 };
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            devGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            BitmapImage cyLogoBitmap;
            string cyLogoDiskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "CY_L_Oficial_Transparente.png");
            if (File.Exists(cyLogoDiskPath))
            {
                cyLogoBitmap = new BitmapImage(new Uri(cyLogoDiskPath));
            }
            else
            {
                cyLogoBitmap = new BitmapImage(new Uri("ms-appx:///Assets/CY_L_Oficial_Transparente.png"));
            }

            var cyLogoImage = new Image
            {
                Source = cyLogoBitmap,
                Width = 56,
                Height = 56,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cyLogoImage, 0);
            devGrid.Children.Add(cyLogoImage);

            var devInfoStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            devInfoStack.Children.Add(new TextBlock
            {
                Text = "Desarrollado por Codex Yolpaki",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 14,
                Foreground = textPrimaryBrush
            });
            devInfoStack.Children.Add(new TextBlock
            {
                Text = "Innovación en software, audio digital y diseño de experiencias interactivas.",
                FontSize = 11,
                Foreground = textSecondaryBrush,
                TextWrapping = TextWrapping.Wrap
            });
            devInfoStack.Children.Add(new TextBlock
            {
                Text = "© 2026 Codex Yolpaki • Todos los derechos reservados",
                FontSize = 10,
                Foreground = textSecondaryBrush,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(devInfoStack, 1);
            devGrid.Children.Add(devInfoStack);

            devCard.Child = devGrid;
            stack.Children.Add(devCard);

            var aboutDialog = new ContentDialog
            {
                Title = "Acerca de Kaki EQ",
                Content = stack,
                CloseButtonText = "Aceptar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            try
            {
                await aboutDialog.ShowAsync();
            }
            catch { }
        }

        public async void ShowToast(string message, string glyph = "\uE73E")
        {
            try
            {
                var toastBorder = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(230, 30, 22, 42)),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252)),
                    BorderThickness = new Thickness(1, 1, 1, 1),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(18, 10, 18, 10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 110),
                    Opacity = 0
                };

                var toastPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                toastPanel.Children.Add(new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 14,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
                });
                toastPanel.Children.Add(new TextBlock
                {
                    Text = message,
                    FontSize = 13,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                });
                toastBorder.Child = toastPanel;

                // Add to the root grid
                if (Content is Grid rootGrid)
                {
                    Grid.SetRowSpan(toastBorder, 2);
                    Canvas.SetZIndex(toastBorder, 200);
                    rootGrid.Children.Add(toastBorder);

                    // Animate in
                    toastBorder.Opacity = 1;
                    await Task.Delay(2500);
                    toastBorder.Opacity = 0;
                    await Task.Delay(300);
                    rootGrid.Children.Remove(toastBorder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Toast Error] {ex.Message}");
            }
        }

        private void StartNowPlayingSpectrumTimer()
        {
            if (_nowPlayingSpectrumTimer == null && DispatcherQueue != null)
            {
                _nowPlayingSpectrumTimer = DispatcherQueue.CreateTimer();
                _nowPlayingSpectrumTimer.Interval = TimeSpan.FromMilliseconds(25);
                _nowPlayingSpectrumTimer.Tick += (_, _) => UpdateNowPlayingSpectrumVisuals();
            }
            _nowPlayingSpectrumTimer?.Start();
        }

        private void UpdateNowPlayingSpectrumVisuals()
        {
            if (NowPlayingSpectrumCanvas == null || NowPlayingSpectrumCanvas.ActualWidth <= 40 || NowPlayingSpectrumCanvas.ActualHeight <= 8)
                return;

            if (!ViewModel.IsNowPlayingExpanded || !ViewModel.IsSpectrumAnalyzerEnabled)
                return;

            while (_npSpectrumBars.Count < NpSpectrumBandCount)
            {
                int idx = _npSpectrumBars.Count;
                double t = idx / (double)(NpSpectrumBandCount - 1);
                byte r = (byte)(187 + (3 - 187) * t);
                byte g = (byte)(134 + (218 - 134) * t);
                byte b = (byte)(252 + (198 - 252) * t);

                var bar = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    RadiusX = 2.5,
                    RadiusY = 2.5,
                    Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(205, r, g, b)),
                    Width = 5,
                    Height = 3
                };
                _npSpectrumBars.Add(bar);
                NowPlayingSpectrumCanvas.Children.Add(bar);
            }

            bool hasSignal = ViewModel.AudioEngine.GetSpectrumBands(_npRawSpectrum);
            double width = NowPlayingSpectrumCanvas.ActualWidth;
            double height = NowPlayingSpectrumCanvas.ActualHeight;
            double slotW = width / NpSpectrumBandCount;
            double barW = Math.Clamp(slotW * 0.48, 3.0, 12.0);
            double maxBarH = Math.Max(6.0, height - 2.0);

            for (int i = 0; i < NpSpectrumBandCount; i++)
            {
                float target = hasSignal ? _npRawSpectrum[i] : 0f;
                float coeff = target > _npSmoothSpectrum[i] ? 0.55f : 0.18f;
                _npSmoothSpectrum[i] += (target - _npSmoothSpectrum[i]) * coeff;

                double barH = Math.Max(3.0, _npSmoothSpectrum[i] * maxBarH);
                double left = i * slotW + (slotW - barW) * 0.5;
                double top = height - barH;

                var bar = _npSpectrumBars[i];
                bar.Width = barW;
                bar.Height = barH;
                Canvas.SetLeft(bar, left);
                Canvas.SetTop(bar, top);
            }
        }

        // =====================================================================
        // WINDOW & NOW-PLAYING RESPONSIVE LAYOUT ENGINE
        // =====================================================================

        private void RootWindowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            if (w <= 0) return;

            // 1. Auto-collapse Sidebar on narrow windows so workspace keeps breathing room
            if (w < 800 && ViewModel.IsSidebarExpanded)
            {
                _sidebarAutoCollapsed = true;
                ViewModel.IsSidebarExpanded = false;
            }
            else if (w >= 920 && _sidebarAutoCollapsed && !ViewModel.IsSidebarExpanded)
            {
                _sidebarAutoCollapsed = false;
                ViewModel.IsSidebarExpanded = true;
            }

            // 2. TitleBar compact pills on very thin windows
            Visibility titleLabelsVis = w < 560 ? Visibility.Collapsed : Visibility.Visible;
            if (TitleBarMetadataText != null) TitleBarMetadataText.Visibility = titleLabelsVis;
            if (TitleBarSettingsText != null) TitleBarSettingsText.Visibility = titleLabelsVis;

            // 3. Floating Bottom Player Bar responsive tiers
            if (FloatingPlayerBar != null && PlayerLeftCol != null && PlayerRightCol != null)
            {
                if (w >= 900)
                {
                    FloatingPlayerBar.Margin = new Thickness(24, 0, 24, 14);
                    PlayerLeftCol.Width = new GridLength(240);
                    PlayerRightCol.Width = new GridLength(240);
                    if (PlayerAddToPlaylistBtn != null) PlayerAddToPlaylistBtn.Visibility = Visibility.Visible;
                    if (PlayerEqShortcutBtn != null) PlayerEqShortcutBtn.Visibility = Visibility.Visible;
                    if (PlayerMultiOutShortcutBtn != null) PlayerMultiOutShortcutBtn.Visibility = Visibility.Visible;
                    if (PlayerVolumeIcon != null) PlayerVolumeIcon.Visibility = Visibility.Visible;
                    if (PlayerVolumeSlider != null)
                    {
                        PlayerVolumeSlider.Visibility = Visibility.Visible;
                        PlayerVolumeSlider.Width = 75;
                    }
                }
                else if (w >= 660)
                {
                    FloatingPlayerBar.Margin = new Thickness(12, 0, 12, 10);
                    PlayerLeftCol.Width = new GridLength(165);
                    PlayerRightCol.Width = GridLength.Auto;
                    if (PlayerAddToPlaylistBtn != null) PlayerAddToPlaylistBtn.Visibility = Visibility.Visible;
                    if (PlayerEqShortcutBtn != null) PlayerEqShortcutBtn.Visibility = Visibility.Collapsed;
                    if (PlayerMultiOutShortcutBtn != null) PlayerMultiOutShortcutBtn.Visibility = Visibility.Collapsed;
                    if (PlayerVolumeIcon != null) PlayerVolumeIcon.Visibility = Visibility.Visible;
                    if (PlayerVolumeSlider != null)
                    {
                        PlayerVolumeSlider.Visibility = Visibility.Visible;
                        PlayerVolumeSlider.Width = 54;
                    }
                }
                else
                {
                    FloatingPlayerBar.Margin = new Thickness(8, 0, 8, 8);
                    PlayerLeftCol.Width = new GridLength(Math.Max(92, w * 0.22));
                    PlayerRightCol.Width = GridLength.Auto;
                    if (PlayerAddToPlaylistBtn != null) PlayerAddToPlaylistBtn.Visibility = Visibility.Collapsed;
                    if (PlayerEqShortcutBtn != null) PlayerEqShortcutBtn.Visibility = Visibility.Collapsed;
                    if (PlayerMultiOutShortcutBtn != null) PlayerMultiOutShortcutBtn.Visibility = Visibility.Collapsed;
                    if (PlayerVolumeIcon != null) PlayerVolumeIcon.Visibility = Visibility.Collapsed;
                    if (PlayerVolumeSlider != null) PlayerVolumeSlider.Visibility = Visibility.Collapsed;
                }
            }

            // 4. NowPlayingOverlay responsive adaptations
            if (NowPlayingRootLayoutGrid != null)
            {
                bool compactNp = w < 780;
                NowPlayingRootLayoutGrid.Padding = compactNp ? new Thickness(16, 12, 16, 14) : new Thickness(36, 20, 36, 24);

                if (NowPlayingTechBadgeGrid != null)
                {
                    NowPlayingTechBadgeGrid.Visibility = (w >= 820 && ViewModel.AudioTechInfoVisibility == Visibility.Visible)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                Visibility npBtnLabels = w < 620 ? Visibility.Collapsed : Visibility.Visible;
                if (NowPlayingBackText != null) NowPlayingBackText.Visibility = npBtnLabels;
                if (NowPlayingQueueBtnText != null) NowPlayingQueueBtnText.Visibility = npBtnLabels;

                if (NowPlayingQueueDrawer != null)
                {
                    NowPlayingQueueDrawer.Width = w < 900 ? Math.Max(220, Math.Round(w * 0.40)) : 360;
                }

                // Album Cover + Tag Box layout (side-by-side vs stacked for narrow screens)
                if (NowPlayingCoverPanel != null && NowPlayingCoverArtBox != null && NowPlayingTagBox != null)
                {
                    if (compactNp)
                    {
                        double coverSize = Math.Clamp(w - 96, 180, 260);
                        NowPlayingCoverArtBox.Width = coverSize;
                        NowPlayingCoverArtBox.Height = coverSize;
                        NowPlayingCoverPanel.HorizontalAlignment = HorizontalAlignment.Center;
                        NowPlayingCoverPanel.VerticalAlignment = VerticalAlignment.Center;
                        NowPlayingCoverPanel.Margin = new Thickness(12, 0, 12, 8);
                        Grid.SetRow(NowPlayingTagBox, 1);
                        Grid.SetColumn(NowPlayingTagBox, 0);
                        NowPlayingTagBox.Margin = new Thickness(0, 12, 0, 0);
                        NowPlayingTagBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                        NowPlayingTagBox.MaxWidth = Math.Max(220, w - 56);
                    }
                    else
                    {
                        NowPlayingCoverArtBox.Width = 380;
                        NowPlayingCoverArtBox.Height = 380;
                        NowPlayingCoverPanel.HorizontalAlignment = HorizontalAlignment.Left;
                        NowPlayingCoverPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        NowPlayingCoverPanel.Margin = new Thickness(32, 0, 0, 16);
                        Grid.SetRow(NowPlayingTagBox, 0);
                        Grid.SetColumn(NowPlayingTagBox, 1);
                        NowPlayingTagBox.Margin = new Thickness(-30, 0, 0, 28);
                        NowPlayingTagBox.HorizontalAlignment = HorizontalAlignment.Left;
                        NowPlayingTagBox.MaxWidth = 380;
                    }
                }

                // Lyrics side cover collapses cleanly under 740px so verses use 100% width
                if (NowPlayingLyricsLeftPanel != null)
                {
                    NowPlayingLyricsLeftPanel.Visibility = w < 740 ? Visibility.Collapsed : Visibility.Visible;
                }

                // Bottom NowPlaying Transport & Action capsules wrap cleanly under 780px
                if (NowPlayingLeftTransportPill != null && NowPlayingRightActionsPill != null)
                {
                    if (compactNp)
                    {
                        NowPlayingLeftTransportPill.HorizontalAlignment = HorizontalAlignment.Center;
                        Grid.SetRow(NowPlayingRightActionsPill, 1);
                        Grid.SetColumn(NowPlayingRightActionsPill, 0);
                        Grid.SetColumnSpan(NowPlayingRightActionsPill, 2);
                        NowPlayingRightActionsPill.HorizontalAlignment = HorizontalAlignment.Center;
                        NowPlayingRightActionsPill.Margin = new Thickness(0, 8, 0, 0);
                    }
                    else
                    {
                        NowPlayingLeftTransportPill.HorizontalAlignment = HorizontalAlignment.Left;
                        Grid.SetRow(NowPlayingRightActionsPill, 0);
                        Grid.SetColumn(NowPlayingRightActionsPill, 1);
                        Grid.SetColumnSpan(NowPlayingRightActionsPill, 1);
                        NowPlayingRightActionsPill.HorizontalAlignment = HorizontalAlignment.Right;
                        NowPlayingRightActionsPill.Margin = new Thickness(0);
                    }
                }

                Visibility npVolVis = w < 540 ? Visibility.Collapsed : Visibility.Visible;
                if (NowPlayingVolumeIcon != null) NowPlayingVolumeIcon.Visibility = npVolVis;
                if (NowPlayingVolumeSlider != null)
                {
                    NowPlayingVolumeSlider.Visibility = npVolVis;
                    NowPlayingVolumeSlider.Width = w < 900 ? 64 : 85;
                }
            }
        }

        private void NowPlayingConstellationPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            if (w <= 0) return;

            if (ConstellationActionsPanel != null)
            {
                if (w < 680)
                {
                    Grid.SetRow(ConstellationActionsPanel, 1);
                    Grid.SetColumn(ConstellationActionsPanel, 0);
                    Grid.SetColumnSpan(ConstellationActionsPanel, 2);
                    ConstellationActionsPanel.Margin = new Thickness(0, 8, 0, 0);
                    ConstellationActionsPanel.HorizontalAlignment = HorizontalAlignment.Left;
                }
                else
                {
                    Grid.SetRow(ConstellationActionsPanel, 0);
                    Grid.SetColumn(ConstellationActionsPanel, 1);
                    Grid.SetColumnSpan(ConstellationActionsPanel, 1);
                    ConstellationActionsPanel.Margin = new Thickness(10, 0, 0, 0);
                    ConstellationActionsPanel.HorizontalAlignment = HorizontalAlignment.Right;
                }
            }

            if (ConstellationLegendBorder != null)
            {
                ConstellationLegendBorder.Visibility = w < 500 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        // =====================================================================
        // INTERACTIVE 2D HARMONIC CONSTELLATION MAP (RETAINED-MODE 120 FPS ENGINE)
        // =====================================================================

        private async Task RefreshConstellationDataAndRenderAsync()
        {
            if (_isLoadingConstellationData) return;
            _isLoadingConstellationData = true;
            try
            {
                var dbTracks = await ViewModel.GetAllLibraryTracksAsync();
                var merged = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);

                if (ViewModel.CurrentTrack != null && !string.IsNullOrWhiteSpace(ViewModel.CurrentTrack.FilePath))
                {
                    merged[ViewModel.CurrentTrack.FilePath] = ViewModel.CurrentTrack;
                }
                if (ViewModel.CurrentQueue != null)
                {
                    foreach (var q in ViewModel.CurrentQueue)
                    {
                        if (!string.IsNullOrWhiteSpace(q.FilePath) && !merged.ContainsKey(q.FilePath))
                            merged[q.FilePath] = q;
                    }
                }
                foreach (var t in dbTracks)
                {
                    if (!string.IsNullOrWhiteSpace(t.FilePath) && !merged.ContainsKey(t.FilePath))
                        merged[t.FilePath] = t;
                }

                _constellationNodes.Clear();
                var priorityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (ViewModel.CurrentTrack != null && !string.IsNullOrWhiteSpace(ViewModel.CurrentTrack.FilePath))
                    priorityKeys.Add(ViewModel.CurrentTrack.FilePath);
                if (ViewModel.CurrentQueue != null)
                {
                    foreach (var q in ViewModel.CurrentQueue)
                    {
                        if (!string.IsNullOrWhiteSpace(q.FilePath)) priorityKeys.Add(q.FilePath);
                    }
                }

                var analyzed = new List<ConstellationNode>();
                foreach (var t in merged.Values)
                {
                    string cam = CamelotColorConverter.NormalizeCamelotKey(t.CamelotKey);

                    if (!HarmonicAnalysisHelper.TryParseCamelot(cam, out int num, out char letter))
                        continue;

                    double bpm = t.Bpm > 20 ? t.Bpm : 115.0;
                    analyzed.Add(new ConstellationNode
                    {
                        Track = t,
                        NormalizedCamelot = cam,
                        CamelotNumber = num,
                        CamelotLetter = letter,
                        EffectiveBpm = bpm,
                        FillBrush = CamelotColorConverter.GetBackgroundBrush(cam)
                    });
                }

                // Keep up to 180 stars (prioritizing active track, queue, and top played) for 120 FPS interactivity
                var selected = analyzed
                    .OrderByDescending(n => priorityKeys.Contains(n.Track.FilePath) ? 1 : 0)
                    .ThenByDescending(n => n.Track.PlayCount)
                    .Take(180);

                _constellationNodes.AddRange(selected);

                if (ConstellationStatsText != null)
                {
                    ConstellationStatsText.Text = $"{_constellationNodes.Count} estrellas Camelot × BPM • Arrastra entre nodos para trazar ruta DJ";
                }
                if (ConstellationEmptyState != null)
                {
                    ConstellationEmptyState.Visibility = _constellationNodes.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
                }

                _hoveredConstellationNode = null;
                RebuildConstellationScene();
                UpdateConstellationInspector(null);
            }
            catch
            {
            }
            finally
            {
                _isLoadingConstellationData = false;
            }
        }

        private void ConstellationCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ViewModel.IsConstellationVisible &&
                (Math.Abs(e.NewSize.Width - _lastConstellationWidth) >= 1.5 ||
                 Math.Abs(e.NewSize.Height - _lastConstellationHeight) >= 1.5))
            {
                RebuildConstellationScene();
            }
        }

        private ConstellationNode? HitTestConstellationNode(Point pt, double maxHitRadius = 20.0)
        {
            ConstellationNode? best = null;
            double bestDistSq = maxHitRadius * maxHitRadius;
            for (int i = 0; i < _constellationNodes.Count; i++)
            {
                var node = _constellationNodes[i];
                double dx = pt.X - node.X;
                double dy = pt.Y - node.Y;
                double dSq = dx * dx + dy * dy;
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    best = node;
                }
            }
            return best;
        }

        private void ConstellationCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (ConstellationCanvas == null || _constellationNodes.Count == 0) return;

            var pt = e.GetCurrentPoint(ConstellationCanvas).Position;
            _constellationPointerPos = pt;
            _constellationPressStartPos = pt;
            _isDraggingConstellationRoute = true;
            _constellationAddedNodeOnPress = false;
            _constellationPressedNode = HitTestConstellationNode(pt, 22.0);
            ConstellationCanvas.CapturePointer(e.Pointer);

            if (_constellationPressedNode != null)
            {
                var track = _constellationPressedNode.Track;
                if (!_constellationRoute.Any(t => string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _constellationRoute.Add(track);
                    _constellationAddedNodeOnPress = true;
                }
                _hoveredConstellationNode = _constellationPressedNode;
                UpdateConstellationRouteAndNodesVisuals();
                UpdateConstellationRays();
                UpdateConstellationInspector(_hoveredConstellationNode);
            }
        }

        private void ConstellationCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (ConstellationCanvas == null || _constellationNodes.Count == 0) return;

            var pt = e.GetCurrentPoint(ConstellationCanvas).Position;
            _constellationPointerPos = pt;
            var hit = HitTestConstellationNode(pt, _isDraggingConstellationRoute ? 22.0 : 18.0);

            bool routeChanged = false;
            if (_isDraggingConstellationRoute)
            {
                if (hit != null && !_constellationRoute.Any(t => string.Equals(t.FilePath, hit.Track.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    _constellationRoute.Add(hit.Track);
                    routeChanged = true;
                }
                if (_constellationDragLine != null && _constellationDragLine.Visibility == Visibility.Visible)
                {
                    _constellationDragLine.X2 = pt.X;
                    _constellationDragLine.Y2 = pt.Y;
                }
            }

            bool hoverChanged = !ReferenceEquals(hit, _hoveredConstellationNode);
            if (!hoverChanged && !routeChanged)
            {
                return;
            }

            var prevHover = _hoveredConstellationNode;
            _hoveredConstellationNode = hit;

            if (routeChanged)
            {
                UpdateConstellationRouteAndNodesVisuals();
            }
            else if (hoverChanged)
            {
                UpdateSingleConstellationNodeVisual(prevHover);
                UpdateSingleConstellationNodeVisual(hit);
            }

            UpdateConstellationRays();
            UpdateConstellationInspector(hit);
        }

        private void ConstellationCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingConstellationRoute) return;

            var pt = e.GetCurrentPoint(ConstellationCanvas).Position;
            double dx = pt.X - _constellationPressStartPos.X;
            double dy = pt.Y - _constellationPressStartPos.Y;
            bool isStaticClick = (dx * dx + dy * dy) < 36.0;

            if (isStaticClick && _constellationPressedNode != null && !_constellationAddedNodeOnPress)
            {
                _constellationRoute.RemoveAll(t =>
                    string.Equals(t.FilePath, _constellationPressedNode.Track.FilePath, StringComparison.OrdinalIgnoreCase));
            }

            _isDraggingConstellationRoute = false;
            _constellationPressedNode = null;
            _constellationAddedNodeOnPress = false;
            try { ConstellationCanvas.ReleasePointerCapture(e.Pointer); } catch { }

            UpdateConstellationRouteAndNodesVisuals();
            UpdateConstellationRays();
            UpdateConstellationInspector(_hoveredConstellationNode);
        }

        private void ConstellationCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingConstellationRoute && _hoveredConstellationNode != null)
            {
                var prevHover = _hoveredConstellationNode;
                _hoveredConstellationNode = null;
                UpdateSingleConstellationNodeVisual(prevHover);
                UpdateConstellationRays();
                UpdateConstellationInspector(null);
            }
        }

        private void ConstellationPresetBoost_Click(object sender, RoutedEventArgs e)
        {
            GeneratePresetConstellationRoute(mode: 1);
        }

        private void ConstellationPresetSmooth_Click(object sender, RoutedEventArgs e)
        {
            GeneratePresetConstellationRoute(mode: 0);
        }

        private void ConstellationPresetChill_Click(object sender, RoutedEventArgs e)
        {
            GeneratePresetConstellationRoute(mode: -1);
        }

        private void ConstellationClearRoute_Click(object sender, RoutedEventArgs e)
        {
            _constellationRoute.Clear();
            UpdateConstellationRouteAndNodesVisuals();
            UpdateConstellationRays();
            UpdateConstellationInspector(_hoveredConstellationNode);
        }

        private void ConstellationApplyRoute_Click(object sender, RoutedEventArgs e)
        {
            if (_constellationRoute.Count == 0)
            {
                ShowToast("Traza una ruta conectando estrellas o elige un preset armónico primero.", "\uE734");
                return;
            }

            int added = ViewModel.ApplyConstellationRouteToQueue(_constellationRoute);
            UpdateConstellationRouteAndNodesVisuals();
            UpdateConstellationRays();
            UpdateConstellationInspector(_hoveredConstellationNode);
            ShowToast($"Ruta armónica aplicada ({added} pistas en cola siguiente)", "\uE734");
        }

        private void GeneratePresetConstellationRoute(int mode)
        {
            if (_constellationNodes.Count < 2)
            {
                ShowToast("Se necesitan al menos 2 canciones analizadas para trazar una ruta.", "\uE734");
                return;
            }

            ConstellationNode? anchor = _activePlayingConstellationNode ?? _hoveredConstellationNode ?? _constellationNodes[0];

            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { anchor.Track.FilePath };
            var route = new List<Track>();
            var cursor = anchor;

            for (int step = 0; step < 6; step++)
            {
                ConstellationNode? bestNext = null;
                double bestScore = double.MaxValue;

                foreach (var candidate in _constellationNodes)
                {
                    if (usedPaths.Contains(candidate.Track.FilePath)) continue;

                    double harmonicScore = ComputeConstellationStepCost(cursor, candidate, mode);
                    if (harmonicScore < bestScore)
                    {
                        bestScore = harmonicScore;
                        bestNext = candidate;
                    }
                }

                if (bestNext == null) break;
                usedPaths.Add(bestNext.Track.FilePath);
                route.Add(bestNext.Track);
                cursor = bestNext;
            }

            _constellationRoute.Clear();
            _constellationRoute.AddRange(route);
            UpdateConstellationRouteAndNodesVisuals();
            UpdateConstellationRays();
            UpdateConstellationInspector(_hoveredConstellationNode ?? anchor);
        }

        private static double ComputeConstellationStepCost(ConstellationNode from, ConstellationNode to, int mode)
        {
            int diffClock = (to.CamelotNumber - from.CamelotNumber + 12) % 12;
            bool sameLetter = to.CamelotLetter == from.CamelotLetter;
            double bpmDelta = to.EffectiveBpm - from.EffectiveBpm;
            double bpmAbsRatio = Math.Abs(bpmDelta) / Math.Max(60.0, from.EffectiveBpm);

            if (mode > 0)
            {
                double keyCost = (diffClock == 1 && sameLetter) ? 0.0
                               : (diffClock == 2 && sameLetter) ? 1.0
                               : (diffClock == 7 && sameLetter) ? 2.5
                               : (diffClock == 0 && !sameLetter && to.CamelotLetter == 'B') ? 2.0
                               : (diffClock == 0 && sameLetter) ? 3.5
                               : 12.0 + Math.Min(diffClock, 12 - diffClock);

                double bpmCost = bpmDelta >= 0 && bpmDelta <= 14 ? bpmDelta * 0.15
                               : bpmDelta > 14 ? 4.0 + bpmDelta * 0.25
                               : 6.0 + Math.Abs(bpmDelta) * 0.4;
                return keyCost + bpmCost;
            }
            if (mode < 0)
            {
                double keyCost = (diffClock == 11 && sameLetter) ? 0.0
                               : (diffClock == 0 && !sameLetter && to.CamelotLetter == 'A') ? 1.0
                               : (diffClock == 10 && sameLetter) ? 2.0
                               : (diffClock == 0 && sameLetter) ? 3.0
                               : 12.0 + Math.Min(diffClock, 12 - diffClock);

                double bpmCost = bpmDelta <= 0 && bpmDelta >= -14 ? Math.Abs(bpmDelta) * 0.15
                               : bpmDelta < -14 ? 4.0 + Math.Abs(bpmDelta) * 0.25
                               : 6.0 + bpmDelta * 0.4;
                return keyCost + bpmCost;
            }

            int minCirc = Math.Min(diffClock, 12 - diffClock);
            double smoothKeyCost = (diffClock == 0 && sameLetter) ? 0.0
                                 : (diffClock == 0 && !sameLetter) ? 0.8
                                 : (minCirc == 1 && sameLetter) ? 1.0
                                 : 6.0 + minCirc * 2.5;
            return smoothKeyCost + bpmAbsRatio * 18.0;
        }

        private static bool AreNodesHarmonicallyCompatible(ConstellationNode a, ConstellationNode b, out string label)
        {
            if (a.CamelotNumber == b.CamelotNumber && a.CamelotLetter == b.CamelotLetter)
            {
                label = "Armonía Exacta";
                return true;
            }
            if (a.CamelotNumber == b.CamelotNumber && a.CamelotLetter != b.CamelotLetter)
            {
                label = "Relativa Mayor/Menor";
                return true;
            }
            int diff = (b.CamelotNumber - a.CamelotNumber + 12) % 12;
            if (a.CamelotLetter == b.CamelotLetter && (diff == 1 || diff == 11))
            {
                label = diff == 1 ? "+1 Impulso Camelot" : "-1 Transición Suave";
                return true;
            }
            if (a.CamelotLetter == b.CamelotLetter && diff == 2)
            {
                label = "+2 Salto de Energía";
                return true;
            }
            double pct = Math.Abs(b.EffectiveBpm - a.EffectiveBpm) / Math.Max(60.0, a.EffectiveBpm);
            if (pct <= 0.06)
            {
                label = $"Tempo Cercano ({(b.EffectiveBpm >= a.EffectiveBpm ? "+" : "")}{b.EffectiveBpm - a.EffectiveBpm:0} BPM)";
                return false;
            }
            label = "Cruce Creativo";
            return false;
        }

        private void UpdateConstellationInspector(ConstellationNode? focusNode)
        {
            if (ConstellationInspTitle == null || ConstellationRouteSummaryText == null) return;

            ConstellationRouteSummaryText.Text = _constellationRoute.Count > 0
                ? $"Ruta DJ trazada: {_constellationRoute.Count} pistas  •  Haz clic en 'Aplicar Ruta' para enviarla a la cola"
                : "Ruta DJ: 0 pistas  •  Haz clic o arrastra sobre estrellas para conectar";

            ConstellationNode? anchorNode = null;
            if (_constellationRoute.Count > 0)
            {
                var lastPath = _constellationRoute[^1].FilePath;
                anchorNode = _constellationNodes.FirstOrDefault(n => string.Equals(n.Track.FilePath, lastPath, StringComparison.OrdinalIgnoreCase));
            }
            anchorNode ??= _activePlayingConstellationNode;

            var displayNode = focusNode ?? anchorNode ?? _constellationNodes.FirstOrDefault();
            if (displayNode == null)
            {
                ConstellationInspTitle.Text = "Explora la Constelación";
                ConstellationInspArtist.Text = "Analiza canciones para ver el mapa Camelot × BPM";
                ConstellationInspCamelotText.Text = "--";
                ConstellationInspBpmText.Text = "-- BPM";
                ConstellationInspCompatText.Text = "Sin datos";
                return;
            }

            ConstellationInspTitle.Text = displayNode.Track.Title ?? "Sin título";
            ConstellationInspArtist.Text = displayNode.Track.Artist ?? "Artista desconocido";
            ConstellationInspCamelotText.Text = displayNode.NormalizedCamelot;
            ConstellationInspBpmText.Text = $"{displayNode.EffectiveBpm:0} BPM";

            ConstellationInspCamelotBadge.Background = displayNode.FillBrush;
            ConstellationInspCamelotText.Foreground = CamelotColorConverter.GetForegroundBrush(displayNode.NormalizedCamelot);

            if (anchorNode != null && !ReferenceEquals(anchorNode, displayNode))
            {
                bool isHarm = AreNodesHarmonicallyCompatible(anchorNode, displayNode, out string compatLabel);
                double bpmDiff = displayNode.EffectiveBpm - anchorNode.EffectiveBpm;
                ConstellationInspCompatText.Text = $"{compatLabel} ({(bpmDiff >= 0 ? "+" : "")}{bpmDiff:0} BPM)";
                ConstellationInspCompatBadge.Background = isHarm ? ConstCompatHarmBgBrush : ConstCompatCreativeBgBrush;
                ConstellationInspCompatText.Foreground = isHarm ? ConstCompatHarmFgBrush : ConstCompatCreativeFgBrush;
            }
            else if (ReferenceEquals(displayNode, _activePlayingConstellationNode))
            {
                ConstellationInspCompatText.Text = "Reproduciendo Ahora (Ancla)";
                ConstellationInspCompatBadge.Background = ConstCompatPlayingBgBrush;
                ConstellationInspCompatText.Foreground = ConstCompatPlayingFgBrush;
            }
            else
            {
                ConstellationInspCompatText.Text = "Lista para conectar";
                ConstellationInspCompatBadge.Background = ConstCompatHarmBgBrush;
                ConstellationInspCompatText.Foreground = ConstCompatHarmFgBrush;
            }
        }

        private void RebuildConstellationScene()
        {
            if (ConstellationCanvas == null) return;

            double w = ConstellationCanvas.ActualWidth;
            double h = ConstellationCanvas.ActualHeight;
            if (w < 80 || h < 80) return;

            _lastConstellationWidth = w;
            _lastConstellationHeight = h;

            ConstellationCanvas.Children.Clear();
            _constellationRayPool.Clear();

            double cx = w < 620 ? w * 0.50 : w * 0.56;
            double cy = h * 0.50;
            double maxRadius = Math.Max(48.0, Math.Min(w * 0.40, h * 0.40));
            double minRadius = maxRadius * 0.20;

            // 1. Static Concentric BPM guide rings (80, 100, 120, 140, 160 BPM)
            double minBpm = 68.0;
            double maxBpm = 172.0;
            int[] bpmGuides = { 80, 100, 120, 140, 160 };
            foreach (int bpm in bpmGuides)
            {
                double t = Math.Clamp((bpm - minBpm) / (maxBpm - minBpm), 0.0, 1.0);
                double r = minRadius + t * (maxRadius - minRadius);
                var ring = new Ellipse
                {
                    Width = r * 2,
                    Height = r * 2,
                    Stroke = bpm == 120 ? ConstRing120Brush : ConstRingDimBrush,
                    StrokeThickness = bpm == 120 ? 1.15 : 0.85,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, cx - r);
                Canvas.SetTop(ring, cy - r);
                ConstellationCanvas.Children.Add(ring);

                var bpmLbl = new TextBlock
                {
                    Text = $"{bpm}",
                    FontSize = 9.5,
                    Foreground = ConstRingLabelBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(bpmLbl, cx + 4);
                Canvas.SetTop(bpmLbl, cy - r - 13);
                ConstellationCanvas.Children.Add(bpmLbl);
            }

            // 2. Static 12 Camelot clock radial spokes & perimeter hour markers
            for (int hour = 1; hour <= 12; hour++)
            {
                double angle = ((hour % 12) * 30.0 - 90.0) * Math.PI / 180.0;
                double x2 = cx + Math.Cos(angle) * maxRadius;
                double y2 = cy + Math.Sin(angle) * maxRadius;

                var camCol = CamelotColorConverter.GetColor($"{hour}B");
                var spoke = new Line
                {
                    X1 = cx + Math.Cos(angle) * (minRadius * 0.65),
                    Y1 = cy + Math.Sin(angle) * (minRadius * 0.65),
                    X2 = x2,
                    Y2 = y2,
                    Stroke = new SolidColorBrush(Color.FromArgb(24, camCol.R, camCol.G, camCol.B)),
                    StrokeThickness = 1.0,
                    IsHitTestVisible = false
                };
                ConstellationCanvas.Children.Add(spoke);

                double lblR = maxRadius + 15;
                var hourLbl = new TextBlock
                {
                    Text = $"{hour}",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(185, camCol.R, camCol.G, camCol.B)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(hourLbl, cx + Math.Cos(angle) * lblR - 7);
                Canvas.SetTop(hourLbl, cy + Math.Sin(angle) * lblR - 8);
                ConstellationCanvas.Children.Add(hourLbl);
            }

            // 3. Pre-allocated harmonic ray pool (zero runtime allocation on hover)
            for (int i = 0; i < MaxConstellationRays; i++)
            {
                var ray = new Line
                {
                    Stroke = ConstRaySoftBrush,
                    StrokeThickness = 0.85,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                _constellationRayPool.Add(ray);
                ConstellationCanvas.Children.Add(ray);
            }

            // 4. Persistent DJ route polyline & drag preview line
            _constellationRoutePolyline = new Polyline
            {
                Stroke = ConstRouteLineBrush,
                StrokeThickness = 2.6,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            ConstellationCanvas.Children.Add(_constellationRoutePolyline);

            _constellationDragLine = new Line
            {
                Stroke = ConstDragLineBrush,
                StrokeThickness = 1.8,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            ConstellationCanvas.Children.Add(_constellationDragLine);

            // 5. Persistent current-playing halo ellipse
            _constellationCurrentHalo = new Ellipse
            {
                Fill = ConstHaloFillBrush,
                Stroke = ConstHaloStrokeBrush,
                StrokeThickness = 1.6,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            ConstellationCanvas.Children.Add(_constellationCurrentHalo);

            // Identify active playing node
            string? currentFile = ViewModel.CurrentTrack?.FilePath;
            _activePlayingConstellationNode = !string.IsNullOrEmpty(currentFile)
                ? _constellationNodes.FirstOrDefault(n => string.Equals(n.Track.FilePath, currentFile, StringComparison.OrdinalIgnoreCase))
                : null;

            if (_constellationNodes.Count == 0) return;

            // 6. Compute polar coordinates & create persistent StarEllipse + collapsed StepBadge per node
            foreach (var node in _constellationNodes)
            {
                uint hCode = (uint)(node.Track.FilePath?.GetHashCode() ?? 0);
                double jitterDeg = ((hCode % 19) - 9) * 0.95;
                double sectorOffset = node.CamelotLetter == 'A' ? -4.5 : 4.5;
                double angle = ((node.CamelotNumber % 12) * 30.0 + sectorOffset + jitterDeg - 90.0) * Math.PI / 180.0;

                double bpmNorm = Math.Clamp((node.EffectiveBpm - minBpm) / (maxBpm - minBpm), 0.04, 0.98);
                double ringBias = node.CamelotLetter == 'A' ? -0.04 : 0.04;
                double r = minRadius + Math.Clamp(bpmNorm + ringBias, 0.02, 1.0) * (maxRadius - minRadius);

                node.X = cx + Math.Cos(angle) * r;
                node.Y = cy + Math.Sin(angle) * r;
                node.Radius = Math.Clamp(4.5 + Math.Log10(node.Track.PlayCount + 1) * 3.2, 4.5, 9.5);

                var star = new Ellipse
                {
                    Width = node.Radius * 2,
                    Height = node.Radius * 2,
                    Fill = node.FillBrush,
                    Stroke = ConstDimStarStrokeBrush,
                    StrokeThickness = 0.9,
                    IsHitTestVisible = false
                };
                node.StarEllipse = star;
                ConstellationCanvas.Children.Add(star);

                var badgeTxt = new TextBlock
                {
                    Text = string.Empty,
                    FontSize = 9.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = ConstStepBadgeFgBrush
                };
                var badge = new Border
                {
                    Background = ConstStepBadgeBgBrush,
                    BorderBrush = ConstStepBadgeBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(4, 0, 4, 0),
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false,
                    Child = badgeTxt
                };
                node.StepBadge = badge;
                node.StepBadgeText = badgeTxt;
                ConstellationCanvas.Children.Add(badge);
            }

            UpdateConstellationRouteAndNodesVisuals();
            UpdateConstellationRays();
        }

        private void UpdateSingleConstellationNodeVisual(ConstellationNode? node)
        {
            if (node == null || node.StarEllipse == null) return;

            bool isCurrent = ReferenceEquals(node, _activePlayingConstellationNode);
            bool isHovered = ReferenceEquals(node, _hoveredConstellationNode);
            double r = node.Radius + (isCurrent ? 3.5 : (isHovered ? 2.8 : (node.IsInRoute ? 1.8 : 0.0)));

            node.StarEllipse.Width = r * 2;
            node.StarEllipse.Height = r * 2;
            node.StarEllipse.Stroke = (isHovered || isCurrent)
                ? ConstWhiteStrokeBrush
                : (node.IsInRoute ? ConstCyanStrokeBrush : ConstDimStarStrokeBrush);
            node.StarEllipse.StrokeThickness = (isHovered || isCurrent || node.IsInRoute) ? 2.0 : 0.9;
            Canvas.SetLeft(node.StarEllipse, node.X - r);
            Canvas.SetTop(node.StarEllipse, node.Y - r);

            if (isCurrent && _constellationCurrentHalo != null)
            {
                double haloR = r + 7.0;
                _constellationCurrentHalo.Width = haloR * 2;
                _constellationCurrentHalo.Height = haloR * 2;
                Canvas.SetLeft(_constellationCurrentHalo, node.X - haloR);
                Canvas.SetTop(_constellationCurrentHalo, node.Y - haloR);
                _constellationCurrentHalo.Visibility = Visibility.Visible;
            }

            if (node.StepBadge != null && node.StepBadgeText != null)
            {
                if (node.IsInRoute)
                {
                    node.StepBadgeText.Text = $"{node.RouteStep}";
                    Canvas.SetLeft(node.StepBadge, node.X + r + 2);
                    Canvas.SetTop(node.StepBadge, node.Y - r - 6);
                    node.StepBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    node.StepBadge.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateConstellationRouteAndNodesVisuals()
        {
            var routeLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var routePoints = new List<ConstellationNode>(_constellationRoute.Count + 1);

            if (_activePlayingConstellationNode != null && _constellationRoute.Count > 0 &&
                !string.Equals(_activePlayingConstellationNode.Track.FilePath, _constellationRoute[0].FilePath, StringComparison.OrdinalIgnoreCase))
            {
                routePoints.Add(_activePlayingConstellationNode);
            }

            for (int i = 0; i < _constellationRoute.Count; i++)
            {
                var rt = _constellationRoute[i];
                routeLookup[rt.FilePath] = i + 1;
                var n = _constellationNodes.FirstOrDefault(x => string.Equals(x.Track.FilePath, rt.FilePath, StringComparison.OrdinalIgnoreCase));
                if (n != null) routePoints.Add(n);
            }

            if (_constellationCurrentHalo != null && _activePlayingConstellationNode == null)
            {
                _constellationCurrentHalo.Visibility = Visibility.Collapsed;
            }

            for (int i = 0; i < _constellationNodes.Count; i++)
            {
                var node = _constellationNodes[i];
                if (routeLookup.TryGetValue(node.Track.FilePath, out int step))
                {
                    node.IsInRoute = true;
                    node.RouteStep = step;
                }
                else
                {
                    node.IsInRoute = false;
                    node.RouteStep = 0;
                }
                UpdateSingleConstellationNodeVisual(node);
            }

            if (_constellationRoutePolyline != null)
            {
                _constellationRoutePolyline.Points.Clear();
                if (routePoints.Count >= 2)
                {
                    foreach (var rp in routePoints)
                    {
                        _constellationRoutePolyline.Points.Add(new Point(rp.X, rp.Y));
                    }
                }
            }

            if (_constellationDragLine != null)
            {
                if (_isDraggingConstellationRoute && routePoints.Count >= 1)
                {
                    var tail = routePoints[^1];
                    _constellationDragLine.X1 = tail.X;
                    _constellationDragLine.Y1 = tail.Y;
                    _constellationDragLine.X2 = _constellationPointerPos.X;
                    _constellationDragLine.Y2 = _constellationPointerPos.Y;
                    _constellationDragLine.Visibility = Visibility.Visible;
                }
                else
                {
                    _constellationDragLine.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateConstellationRays()
        {
            if (_constellationRayPool.Count == 0) return;

            ConstellationNode? routeTailNode = _constellationRoute.Count > 0
                ? _constellationNodes.FirstOrDefault(n => string.Equals(n.Track.FilePath, _constellationRoute[^1].FilePath, StringComparison.OrdinalIgnoreCase))
                : null;

            ConstellationNode? anchorNode = _hoveredConstellationNode ?? routeTailNode ?? _activePlayingConstellationNode;

            int rayIdx = 0;
            if (anchorNode != null)
            {
                for (int i = 0; i < _constellationNodes.Count && rayIdx < MaxConstellationRays; i++)
                {
                    var target = _constellationNodes[i];
                    if (ReferenceEquals(anchorNode, target)) continue;
                    if (!AreNodesHarmonicallyCompatible(anchorNode, target, out _)) continue;

                    double pctBpm = Math.Abs(target.EffectiveBpm - anchorNode.EffectiveBpm) / Math.Max(60.0, anchorNode.EffectiveBpm);
                    if (pctBpm > 0.14) continue;

                    var ray = _constellationRayPool[rayIdx++];
                    ray.X1 = anchorNode.X;
                    ray.Y1 = anchorNode.Y;
                    ray.X2 = target.X;
                    ray.Y2 = target.Y;
                    ray.Stroke = pctBpm <= 0.06 ? ConstRayStrongBrush : ConstRaySoftBrush;
                    ray.StrokeThickness = pctBpm <= 0.06 ? 1.3 : 0.85;
                    ray.Visibility = Visibility.Visible;
                }
            }

            while (rayIdx < _constellationRayPool.Count)
            {
                var ray = _constellationRayPool[rayIdx++];
                if (ray.Visibility != Visibility.Collapsed)
                {
                    ray.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}
