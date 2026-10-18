using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media.Imaging;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow? Instance { get; private set; }
        public MainViewModel ViewModel => MainViewModel.Instance ?? new MainViewModel();

        private Services.SystemIntegration.TrayIconService? _trayService;
        private Views.SettingsWindow? _settingsWindow;
        private Views.MetadataWindow? _metadataWindow;
        private Views.MiniPlayerWindow? _miniPlayerWindow;
        private bool _isExiting = false;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();

            // Setup Custom TitleBar with Native Windows Caption Buttons
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

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

                // Sync FullScreen state and Maximized state
                appWindow.Changed += (s, args) =>
                {
                    if (args.DidPresenterChange)
                    {
                        ViewModel.IsFullScreen = (appWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen);
                    }
                    if (appWindow.Presenter is OverlappedPresenter op)
                    {
                        ViewModel.IsWindowMaximized = (op.State == OverlappedPresenterState.Maximized);
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

            // Auto-scroll synchronized lyrics to active line
            ViewModel.LyricLineChanged += (s, activeIndex) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (LyricsListView != null && activeIndex >= 0 && activeIndex < ViewModel.CurrentLyricsLines.Count)
                        {
                            LyricsListView.ScrollIntoView(ViewModel.CurrentLyricsLines[activeIndex]);
                        }
                    }
                    catch { }
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

            // Monitor track change to update tray tooltip
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.CurrentTrack))
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
                }
            };

            // Check if initial music folder needs to be selected
            _ = CheckInitialMusicFolderAsync();
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

            var result = await dialog.ShowAsync();
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
            _settingsWindow?.Close();
            _metadataWindow?.Close();
            _miniPlayerWindow?.Close();
            Application.Current.Exit();
        }

        public void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new Views.SettingsWindow();
            _settingsWindow.Closed += (s, ev) => _settingsWindow = null;
            _settingsWindow.Activate();
        }

        public void OpenMetadata_Click(object sender, RoutedEventArgs e)
        {
            OpenMetadataWindow(ViewModel.CurrentTrack);
        }

        public void OpenMetadataWindow(Track? track = null)
        {
            if (_metadataWindow != null)
            {
                _metadataWindow.Activate();
                return;
            }

            _metadataWindow = new Views.MetadataWindow(track);
            _metadataWindow.Closed += (s, ev) => _metadataWindow = null;
            _metadataWindow.Activate();
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

        private void OpenLyricsMetadata_Click(object sender, RoutedEventArgs e)
        {
            var track = ViewModel.CurrentTrack;
            if (track == null) return;

            if (_metadataWindow != null)
            {
                _metadataWindow.Activate();
                return;
            }

            _metadataWindow = new Views.MetadataWindow(track);
            _metadataWindow.Closed += (s, args) =>
            {
                _metadataWindow = null;
                _ = ViewModel.LoadLyricsForCurrentTrackAsync();
            };
            _metadataWindow.Activate();
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

            await aboutDialog.ShowAsync();
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
                    Grid.SetRowSpan(toastBorder, 3);
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
    }
}

