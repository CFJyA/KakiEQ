using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ManagedBass;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Audio;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class MetadataWindow : Window
    {
        private List<Track> _allTracks = new();
        private Track? _selectedTrack;
        private string? _pendingNewCoverPath;
        private string? _loadedLyrics;
        private AppWindow? _appWindow;
        public bool AllowClose { get; set; } = false;

        // Album mode batch editing fields
        private bool _isAlbumMode = false;
        private List<AlbumItem> _allAlbumItems = new();
        private AlbumItem? _selectedAlbum;
        private string? _pendingNewAlbumCoverPath;

        // LRC Preview playback fields
        private int _lrcStream = 0;
        private DispatcherQueueTimer? _lrcTimer;
        private List<LrcLine> _activeLrcLines = new();
        private ContentDialog? _activeDialog;
        private CancellationTokenSource? _harmonicBatchCts;
        private bool _isHarmonicBatchRunning = false;

        public MetadataWindow(Track? trackToSelect = null)
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarBorder);

            ConfigureWindow();
            UpdateModeTabVisuals();
            this.Closed += (s, e) =>
            {
                StopLrcPlayback();
                _harmonicBatchCts?.Cancel();
            };
        }

        public void ShowWindow(Track? trackToSelect = null)
        {
            var vm = MainViewModel.Instance;
            if (vm != null && Content is FrameworkElement fe)
            {
                fe.RequestedTheme = vm.IsLightTheme ? ElementTheme.Light : ElementTheme.Dark;
            }

            if (trackToSelect != null && _isAlbumMode)
            {
                _isAlbumMode = false;
                UpdateModeTabVisuals();
                AlbumsContainerGrid.Visibility = Visibility.Collapsed;
                TracksContainerGrid.Visibility = Visibility.Visible;
                SyncLeftPanelVisibility();
            }

            _appWindow?.Show();
            Activate();

            // Ejecutar en prioridad Normal inmediatamente tras mostrar el frame para apertura instantánea a 120 FPS
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _ = LoadTracksAsync(trackToSelect);
            });
        }

        private void ConfigureWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow != null)
            {
                _appWindow.Resize(new SizeInt32(1100, 740));
                _appWindow.Closing += (s, args) =>
                {
                    if (!AllowClose)
                    {
                        args.Cancel = true;
                        StopLrcPlayback();
                        _appWindow.Hide();
                    }
                };
                
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Kaki_Oficial_Blanco.ico");
                if (File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }

                var titleBar = _appWindow.TitleBar;
                if (titleBar != null)
                {
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                }
            }

            // Sync theme
            var vm = MainViewModel.Instance;
            if (vm != null && Content is FrameworkElement fe)
            {
                fe.RequestedTheme = vm.IsLightTheme ? ElementTheme.Light : ElementTheme.Dark;
            }
        }

        private async Task LoadTracksAsync(Track? initialTrack = null)
        {
            if (_isHarmonicBatchRunning && _allTracks.Count > 0)
            {
                if (initialTrack != null && !_isAlbumMode)
                {
                    var match = _allTracks.FirstOrDefault(t => t.FilePath.Equals(initialTrack.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        TracksListView.SelectedItem = match;
                        TracksListView.ScrollIntoView(match);
                    }
                }
                return;
            }

            var vm = MainViewModel.Instance;
            if (vm != null)
            {
                // Reuse in-memory tracks if already loaded, otherwise query SQLite on background thread
                if (vm.LibraryVm.AllTracks.Count > 0)
                {
                    _allTracks = vm.LibraryVm.AllTracks.ToList();
                }
                else
                {
                    _allTracks = await Task.Run(() => vm.DbService.GetAllTracksAsync());
                }

                // Assign display indexes without redundant PropertyChanged notifications
                for (int i = 0; i < _allTracks.Count; i++)
                {
                    if (_allTracks[i].DisplayIndex != i + 1)
                    {
                        _allTracks[i].DisplayIndex = i + 1;
                    }
                }

                BuildAlbumItemsList();

                if (_isAlbumMode)
                {
                    FilterAndDisplayAlbums();
                    if (_selectedAlbum == null && _allAlbumItems.Count > 0)
                    {
                        AlbumsListView.SelectedIndex = 0;
                    }
                }
                else
                {
                    if (!ReferenceEquals(TracksListView.ItemsSource, _allTracks) || !string.IsNullOrEmpty(SearchBox.Text))
                    {
                        FilterAndDisplayTracks();
                    }
                    else
                    {
                        FileCountLabel.Text = $"{_allTracks.Count} canciones";
                    }

                    if (initialTrack != null)
                    {
                        var match = _allTracks.FirstOrDefault(t => t.FilePath.Equals(initialTrack.FilePath, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            TracksListView.SelectedItem = match;
                            TracksListView.ScrollIntoView(match);
                        }
                    }
                    else if (TracksListView.SelectedItem == null && _allTracks.Count > 0)
                    {
                        TracksListView.SelectedIndex = 0;
                    }
                }

                _ = RefreshLibraryMissingMetadataStateAsync();
            }
        }

        private void BuildAlbumItemsList()
        {
            _allAlbumItems = Helpers.AlbumHelper.BuildAlbumItems(_allTracks);
            ResyncSelectedAlbumAfterRebuild();
        }

        private void ResyncSelectedAlbumAfterRebuild()
        {
            if (_selectedAlbum == null) return;

            var refreshed = Helpers.AlbumHelper.FindAlbumItem(_allAlbumItems, _selectedAlbum.Title, _selectedAlbum.Artist);
            if (refreshed != null)
            {
                _selectedAlbum = refreshed;
            }
            else
            {
                _selectedAlbum = null;
            }
        }

        private void TabSongsMode_Click(object sender, RoutedEventArgs e)
        {
            SwitchToMode(false);
        }

        private void TabAlbumsMode_Click(object sender, RoutedEventArgs e)
        {
            SwitchToMode(true);
        }

        private void SwitchToMode(bool albumMode)
        {
            _isAlbumMode = albumMode;
            _pendingNewCoverPath = null;
            _pendingNewAlbumCoverPath = null;
            UpdateModeTabVisuals();
            SyncLeftPanelVisibility();

            if (_isAlbumMode)
            {
                TracksContainerGrid.Visibility = Visibility.Collapsed;
                AlbumsContainerGrid.Visibility = Visibility.Visible;

                FilterAndDisplayAlbums();

                if (_selectedAlbum == null && _allAlbumItems.Count > 0)
                {
                    AlbumsListView.SelectedIndex = 0;
                }
                else if (_selectedAlbum != null)
                {
                    LoadAlbumDetails(_selectedAlbum);
                }
            }
            else
            {
                AlbumsContainerGrid.Visibility = Visibility.Collapsed;
                TracksContainerGrid.Visibility = Visibility.Visible;

                FilterAndDisplayTracks();

                if (_selectedTrack == null && _allTracks.Count > 0)
                {
                    TracksListView.SelectedIndex = 0;
                }
                else if (_selectedTrack != null)
                {
                    LoadTrackDetails(_selectedTrack);
                }
            }
        }

        private void SyncLeftPanelVisibility(bool isEditingTrack = false, bool isEditingAlbum = false)
        {
            if (_isAlbumMode)
            {
                ReadOnlyPanel.Visibility = Visibility.Collapsed;
                EditModePanel.Visibility = Visibility.Collapsed;
                AlbumReadOnlyPanel.Visibility = isEditingAlbum ? Visibility.Collapsed : Visibility.Visible;
                AlbumEditModePanel.Visibility = isEditingAlbum ? Visibility.Visible : Visibility.Collapsed;
                ChangeCoverButton.Visibility = isEditingAlbum ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                AlbumReadOnlyPanel.Visibility = Visibility.Collapsed;
                AlbumEditModePanel.Visibility = Visibility.Collapsed;
                ReadOnlyPanel.Visibility = isEditingTrack ? Visibility.Collapsed : Visibility.Visible;
                EditModePanel.Visibility = isEditingTrack ? Visibility.Visible : Visibility.Collapsed;
                ChangeCoverButton.Visibility = isEditingTrack ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateModeTabVisuals()
        {
            ApplyTabButtonStyle(TabSongsModeBtn, !_isAlbumMode);
            ApplyTabButtonStyle(TabAlbumsModeBtn, _isAlbumMode);
        }

        private void ApplyTabButtonStyle(Button? btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive)
            {
                if (Application.Current.Resources.TryGetValue("AppAccent", out object? accent) && accent is Microsoft.UI.Xaml.Media.Brush accentBrush)
                {
                    btn.Background = accentBrush;
                }
                else
                {
                    btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252));
                }

                var darkBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 18));
                if (btn.Content is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock tb) tb.Foreground = darkBrush;
                        if (child is FontIcon fi) fi.Foreground = darkBrush;
                    }
                }
            }
            else
            {
                btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent);
                if (Application.Current.Resources.TryGetValue("AppTextSecondary", out object? textSec) && textSec is Microsoft.UI.Xaml.Media.Brush textSecBrush)
                {
                    if (btn.Content is StackPanel sp)
                    {
                        foreach (var child in sp.Children)
                        {
                            if (child is TextBlock tb) tb.Foreground = textSecBrush;
                            if (child is FontIcon fi) fi.Foreground = textSecBrush;
                        }
                    }
                }
            }
        }

        private void FilterAndDisplayTracks()
        {
            string query = SearchBox.Text?.Trim() ?? string.Empty;
            List<Track> filtered;

            if (string.IsNullOrEmpty(query))
            {
                filtered = _allTracks;
            }
            else
            {
                filtered = _allTracks.Where(t =>
                    t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.Album.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(t.FilePath).Contains(query, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            TracksListView.ItemsSource = filtered;
            FileCountLabel.Text = $"{filtered.Count} canciones";
        }

        private void FilterAndDisplayAlbums()
        {
            string query = SearchBox.Text?.Trim() ?? string.Empty;
            List<AlbumItem> filtered;

            if (string.IsNullOrEmpty(query))
            {
                filtered = _allAlbumItems;
            }
            else
            {
                filtered = _allAlbumItems.Where(a =>
                    a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    a.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    a.ReleaseType.Contains(query, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            AlbumsListView.ItemsSource = filtered;
            FileCountLabel.Text = $"{filtered.Count} {(filtered.Count == 1 ? "álbum" : "álbumes")}";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isAlbumMode)
            {
                FilterAndDisplayAlbums();
            }
            else
            {
                FilterAndDisplayTracks();
            }
        }

        private void TracksListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TracksListView.SelectedItem is Track track)
            {
                LoadTrackDetails(track);
            }
        }

        private void LoadTrackDetails(Track track)
        {
            _selectedTrack = track;
            if (_isAlbumMode) return;

            _pendingNewCoverPath = null;
            CancelEdit();

            DisplayTitleText.Text = track.Title;
            DisplayArtistText.Text = track.Artist;
            DisplayAlbumText.Text = track.Album;
            DisplayPathText.Text = track.FilePath;

            // Mostrar inmediatamente en 0ms los datos ya cacheados en memoria
            DisplayYearText.Text = track.Year > 0 ? track.Year.ToString() : "-";
            DisplayTrackNumberText.Text = track.TrackNumber > 0 ? track.TrackNumber.ToString() : "-";
            DisplayDiscText.Text = !string.IsNullOrWhiteSpace(track.Disc) ? track.Disc : "-";
            DisplayReleaseTypeText.Text = track.ReleaseTypeFormatted;
            DisplayGenreText.Text = (!string.IsNullOrWhiteSpace(track.Genre) && !track.Genre.Equals("Unknown Genre", StringComparison.OrdinalIgnoreCase))
                ? track.Genre
                : "-";

            string initialNormKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);
            UpdateHarmonicUiDisplay(track.Bpm, initialNormKey, track.HasUnembeddedMetadata);
            EmbedMissingMetadataButton.Visibility = track.HasUnembeddedMetadata ? Visibility.Visible : Visibility.Collapsed;

            string ext = Path.GetExtension(track.FilePath).TrimStart('.').ToUpperInvariant();
            FormatText.Text = ext;
            DisplayLyricsStatusText.Text = "Cargando letra...";

            // Fast non-blocking cover preview from cached cover file if available
            string? coverPath = track.CoverImagePath;
            bool hasCachedCover = !string.IsNullOrEmpty(coverPath) && File.Exists(coverPath);
            if (hasCachedCover && coverPath != null)
            {
                try
                {
                    CoverImageDisplay.Source = new BitmapImage(new Uri(coverPath));
                }
                catch
                {
                    CoverImageDisplay.Source = null;
                }
            }
            else
            {
                CoverImageDisplay.Source = null;
            }

            // Lectura única y ligera en segundo plano para la pista seleccionada (sin abrir 2 veces el archivo ni bloquear con DSP)
            string filePath = track.FilePath;
            _ = Task.Run(async () =>
            {
                string yearText = DisplayYearText.Text;
                string trackNumText = DisplayTrackNumberText.Text;
                string discText = DisplayDiscText.Text;
                string genreText = DisplayGenreText.Text;
                string composerText = "-";
                string albumArtistText = "-";
                string tagTypesText = "ID3v2 / Metadata";
                double detectedBpm = track.Bpm;
                string detectedCamelot = track.CamelotKey;
                double fileTagBpm = 0;
                string fileTagCamelot = string.Empty;
                bool hasMissingInFile = track.HasUnembeddedMetadata;
                byte[]? embeddedCoverBytes = null;

                try
                {
                    if (File.Exists(filePath))
                    {
                        using var tfile = TagLib.File.Create(filePath);
                        var tag = tfile.Tag;

                        yearText = tag.Year > 0 ? tag.Year.ToString() : (track.Year > 0 ? track.Year.ToString() : "-");
                        trackNumText = tag.Track > 0 ? tag.Track.ToString() : (track.TrackNumber > 0 ? track.TrackNumber.ToString() : "-");
                        string extractedDisc = Helpers.DiscMetadataHelper.GetDiscFromTag(tfile);
                        discText = !string.IsNullOrWhiteSpace(extractedDisc)
                            ? extractedDisc
                            : (!string.IsNullOrWhiteSpace(track.Disc) ? track.Disc : "-");
                        genreText = !string.IsNullOrWhiteSpace(tag.FirstGenre)
                            ? tag.FirstGenre
                            : (!string.IsNullOrWhiteSpace(track.Genre) && !track.Genre.Equals("Unknown Genre", StringComparison.OrdinalIgnoreCase) ? track.Genre : "-");
                        composerText = string.IsNullOrWhiteSpace(tag.FirstComposer) ? "-" : tag.FirstComposer;
                        string fileAlbumArtist = Helpers.ArtistHelper.GetAlbumArtistsFromFile(tfile);
                        albumArtistText = !string.IsNullOrWhiteSpace(fileAlbumArtist)
                            ? fileAlbumArtist
                            : (!string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.AlbumArtist : "-");
                        tagTypesText = tfile.TagTypes.ToString().Replace(", ", " • ");

                        var (tagBpm, tagKey) = Helpers.HarmonicAnalysisHelper.ExtractHarmonicMetadataFromTag(tfile);
                        fileTagBpm = tagBpm;
                        fileTagCamelot = tagKey;

                        bool preferDbHarmonic = track.HasUnembeddedMetadata && (track.Bpm > 0 || !string.IsNullOrEmpty(track.CamelotKey));
                        if (!preferDbHarmonic)
                        {
                            if (tagBpm > 0) detectedBpm = tagBpm;
                            if (!string.IsNullOrEmpty(tagKey)) detectedCamelot = tagKey;
                        }

                        hasMissingInFile = HasDatabaseMetadataMissingInFile(track, tfile);
                        track.HasUnembeddedMetadata = hasMissingInFile;

                        if (!hasCachedCover && tag.Pictures.Length > 0)
                        {
                            embeddedCoverBytes = Helpers.ImageHelper.EnsureDisplayableBytes(tag.Pictures[0].Data.Data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading tags: {ex.Message}");
                }

                var lrc = await LrcService.LoadRawLrcForTrackAsync(track);

                // Actualizar inmediatamente la interfaz con las etiquetas leídas (sin esperar DSP)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isAlbumMode || _selectedTrack != track) return;

                    if (detectedBpm > 0) track.Bpm = detectedBpm;
                    if (!string.IsNullOrEmpty(detectedCamelot)) track.CamelotKey = detectedCamelot;

                    DisplayYearText.Text = yearText;
                    DisplayTrackNumberText.Text = trackNumText;
                    DisplayDiscText.Text = discText;
                    DisplayGenreText.Text = genreText;
                    DisplayComposerText.Text = composerText;
                    DisplayAlbumArtistText.Text = albumArtistText;
                    TagTypeText.Text = tagTypesText;

                    string normKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(detectedCamelot);
                    bool harmonicOnlyInDb = (detectedBpm > 0 && (fileTagBpm <= 0 || Math.Abs(detectedBpm - fileTagBpm) >= 1.0))
                                         || (!string.IsNullOrEmpty(normKey) && (string.IsNullOrEmpty(fileTagCamelot) || !string.Equals(normKey, fileTagCamelot, StringComparison.OrdinalIgnoreCase)));
                    UpdateHarmonicUiDisplay(detectedBpm, normKey, harmonicOnlyInDb);
                    EmbedMissingMetadataButton.Visibility = hasMissingInFile ? Visibility.Visible : Visibility.Collapsed;
                    _ = RefreshLibraryMissingMetadataStateAsync();

                    _loadedLyrics = lrc;
                    if (!string.IsNullOrWhiteSpace(lrc))
                    {
                        DisplayLyricsStatusText.Text = (lrc.Contains("[") && lrc.Contains("]"))
                            ? "Letra sincronizada (LRC)"
                            : "Letra disponible (Texto plano)";
                    }
                    else
                    {
                        DisplayLyricsStatusText.Text = "Sin letra sincronizada";
                    }

                    if (!hasCachedCover && embeddedCoverBytes != null)
                    {
                        try
                        {
                            using var ms = new MemoryStream(embeddedCoverBytes);
                            var bitmap = new BitmapImage();
                            _ = bitmap.SetSourceAsync(ms.AsRandomAccessStream());
                            CoverImageDisplay.Source = bitmap;
                        }
                        catch { }
                    }
                });

                // Si aún faltan BPM o Clave Camelot, calcularlos con DSP de forma diferida sin bloquear la vista ni mutar Track fuera del hilo UI
                if (detectedBpm <= 0 || string.IsNullOrEmpty(detectedCamelot))
                {
                    var (dspBpm, dspKey, hasUnembeddedDiff) = Helpers.HarmonicAnalysisHelper.ComputeTrackHarmonicSync(track);
                    if (detectedBpm <= 0 && dspBpm > 0) detectedBpm = dspBpm;
                    if (string.IsNullOrEmpty(detectedCamelot) && !string.IsNullOrEmpty(dspKey)) detectedCamelot = dspKey;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (detectedBpm > 0) track.Bpm = detectedBpm;
                        if (!string.IsNullOrEmpty(detectedCamelot)) track.CamelotKey = detectedCamelot;
                        if (hasUnembeddedDiff || detectedBpm > 0 || !string.IsNullOrEmpty(detectedCamelot))
                        {
                            track.HasUnembeddedMetadata = true;
                        }

                        if (!_isAlbumMode && _selectedTrack == track)
                        {
                            string normKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(detectedCamelot);
                            UpdateHarmonicUiDisplay(detectedBpm, normKey, track.HasUnembeddedMetadata);
                            EmbedMissingMetadataButton.Visibility = track.HasUnembeddedMetadata ? Visibility.Visible : Visibility.Collapsed;
                        }
                        _ = RefreshLibraryMissingMetadataStateAsync();
                    });
                }
            });
        }

        private void UpdateHarmonicUiDisplay(double bpm, string normKey, bool isOnlyInDb)
        {
            string dbSuffix = isOnlyInDb ? " (Solo en BD)" : string.Empty;

            if (!string.IsNullOrEmpty(normKey) && bpm > 0)
                DisplayHarmonicText.Text = $"{normKey} • {bpm:0} BPM{dbSuffix}";
            else if (!string.IsNullOrEmpty(normKey))
                DisplayHarmonicText.Text = $"{normKey}{dbSuffix}";
            else if (bpm > 0)
                DisplayHarmonicText.Text = $"{bpm:0} BPM{dbSuffix}";
            else
                DisplayHarmonicText.Text = "-";

            if (!string.IsNullOrEmpty(normKey))
            {
                CamelotTopBadge.Background = Helpers.CamelotColorConverter.GetBackgroundBrush(normKey);
                CamelotTopBadgeText.Foreground = Helpers.CamelotColorConverter.GetForegroundBrush(normKey);
                CamelotTopBadgeText.Text = normKey;
                CamelotTopBadge.Visibility = Visibility.Visible;
            }
            else
            {
                CamelotTopBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void StartEditMode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null) return;
            _isAlbumMode = false;
            SyncLeftPanelVisibility(isEditingTrack: true);

            EditTitleBox.Text = _selectedTrack.Title;
            EditArtistBox.Text = _selectedTrack.Artist;
            EditAlbumBox.Text = _selectedTrack.Album;
            EditYearBox.Text = DisplayYearText.Text != "-" ? DisplayYearText.Text : string.Empty;
            EditTrackBox.Text = DisplayTrackNumberText.Text != "-" ? DisplayTrackNumberText.Text : string.Empty;
            EditGenreBox.Text = DisplayGenreText.Text != "-" ? DisplayGenreText.Text : string.Empty;
            EditDiscBox.Text = DisplayDiscText.Text != "-" ? DisplayDiscText.Text : string.Empty;
            EditBpmBox.Text = _selectedTrack.Bpm > 0 ? $"{_selectedTrack.Bpm:0}" : string.Empty;
            EditCamelotBox.Text = !string.IsNullOrEmpty(_selectedTrack.CamelotKey) ? _selectedTrack.CamelotKey : string.Empty;
            EditAlbumArtistBox.Text = DisplayAlbumArtistText.Text != "-" ? DisplayAlbumArtistText.Text : string.Empty;
            EditComposerBox.Text = DisplayComposerText.Text != "-" ? DisplayComposerText.Text : string.Empty;
            EditLyricsBox.Text = _loadedLyrics ?? string.Empty;

            string curRel = _selectedTrack.ReleaseTypeFormatted;
            foreach (ComboBoxItem item in EditReleaseTypeBox.Items)
            {
                if (string.Equals(item.Content?.ToString(), curRel, StringComparison.OrdinalIgnoreCase))
                {
                    EditReleaseTypeBox.SelectedItem = item;
                    break;
                }
            }
            if (EditReleaseTypeBox.SelectedItem == null && EditReleaseTypeBox.Items.Count > 0)
            {
                EditReleaseTypeBox.SelectedIndex = 0;
            }

            if (string.IsNullOrEmpty(EditDiscBox.Text))
            {
                try
                {
                    if (File.Exists(_selectedTrack.FilePath))
                    {
                        using var tfile = TagLib.File.Create(_selectedTrack.FilePath);
                        string discVal = Helpers.DiscMetadataHelper.GetDiscFromTag(tfile);
                        if (!string.IsNullOrEmpty(discVal)) EditDiscBox.Text = discVal;
                    }
                }
                catch { }
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            CancelEdit();
            if (_selectedTrack != null) LoadTrackDetails(_selectedTrack);
        }

        private void CancelEdit()
        {
            _pendingNewCoverPath = null;
            SyncLeftPanelVisibility(isEditingTrack: false, isEditingAlbum: false);
        }

        private async void ChangeCover_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".jxl");

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                if (_isAlbumMode)
                {
                    _pendingNewAlbumCoverPath = file.Path;
                }
                else
                {
                    _pendingNewCoverPath = file.Path;
                }
                CoverImageDisplay.Source = Helpers.ImageHelper.LoadBitmapSync(file.Path);
            }
        }

        private async void SaveMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null || !File.Exists(_selectedTrack.FilePath)) return;

            try
            {
                string filePath = _selectedTrack.FilePath;
                string parsedReleaseType = (EditReleaseTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                                        ?? Helpers.ReleaseTypeHelper.TypeAlbum;

                string editAlbumTitle = EditAlbumBox.Text.Trim();
                string normAlbumArtist = string.Empty;
                var splitAlbumArtists = Helpers.ArtistHelper.SplitArtists(EditAlbumArtistBox.Text.Trim());
                if (!string.IsNullOrWhiteSpace(EditAlbumArtistBox.Text))
                {
                    normAlbumArtist = string.Join("; ", splitAlbumArtists);
                }

                var splitPerformers = Helpers.ArtistHelper.SplitArtists(EditArtistBox.Text.Trim());
                string normalizedPerformers = string.Join("; ", splitPerformers);

                bool albumOrArtistChanged =
                    !string.Equals(_selectedTrack.Album?.Trim() ?? string.Empty, editAlbumTitle, StringComparison.Ordinal) ||
                    !string.Equals(_selectedTrack.AlbumArtist?.Trim() ?? string.Empty, normAlbumArtist, StringComparison.Ordinal) ||
                    !string.Equals(_selectedTrack.Artist?.Trim() ?? string.Empty, normalizedPerformers, StringComparison.Ordinal);

                string editTitle = EditTitleBox.Text.Trim();
                string editYearText = EditYearBox.Text.Trim();
                string editTrackText = EditTrackBox.Text.Trim();
                string editDiscText = EditDiscBox.Text;
                string editBpmText = EditBpmBox.Text.Trim();
                string editCamelotText = EditCamelotBox.Text.Trim();
                string editGenreText = EditGenreBox.Text.Trim();
                string editComposerText = EditComposerBox.Text.Trim();
                string editLyricsText = EditLyricsBox.Text;
                string? pendingCover = _pendingNewCoverPath;
                var trackRef = _selectedTrack;

                StopLrcPlayback();

                await ExecuteWithPlaybackFileUnlockedAsync(new[] { filePath }, () =>
                {
                    using var tfile = TagLib.File.Create(filePath);
                    bool isFlac = filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) || tfile is TagLib.Flac.File;

                    if (isFlac)
                    {
                        // Ensure no illegal ID3 tags on FLAC file
                        tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);

                        if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                        {
                            xiph.SetField("ALBUM", editAlbumTitle);
                            if (!string.IsNullOrWhiteSpace(normAlbumArtist))
                            {
                                xiph.SetField("ALBUMARTIST", normAlbumArtist);
                                xiph.SetField("ALBUM ARTIST", normAlbumArtist);
                            }
                            else
                            {
                                xiph.RemoveField("ALBUMARTIST");
                                xiph.RemoveField("ALBUM ARTIST");
                            }
                        }
                    }

                    tfile.Tag.Title = editTitle;
                    tfile.Tag.Performers = splitPerformers.ToArray();
                    tfile.Tag.Album = editAlbumTitle;

                    if (uint.TryParse(editYearText, out uint year))
                        tfile.Tag.Year = year;

                    if (uint.TryParse(editTrackText, out uint trk))
                        tfile.Tag.Track = trk;

                    Helpers.DiscMetadataHelper.SetDiscInTag(tfile, editDiscText);
                    Helpers.ReleaseTypeHelper.SetReleaseTypeInTag(tfile, parsedReleaseType);

                    double parsedBpm = trackRef.Bpm;
                    if (double.TryParse(editBpmText, out double userBpm) && userBpm > 0)
                        parsedBpm = Math.Round(userBpm);

                    string parsedCamelot = Helpers.HarmonicAnalysisHelper.MapKeyToCamelot(editCamelotText);
                    if (string.IsNullOrEmpty(parsedCamelot))
                        parsedCamelot = trackRef.CamelotKey;

                    Helpers.HarmonicAnalysisHelper.WriteHarmonicMetadataToTag(tfile, parsedBpm, parsedCamelot);

                    if (!string.IsNullOrWhiteSpace(editGenreText))
                        tfile.Tag.Genres = new[] { editGenreText };

                    if (!string.IsNullOrWhiteSpace(normAlbumArtist))
                    {
                        tfile.Tag.AlbumArtists = splitAlbumArtists.ToArray();
                    }
                    else
                    {
                        tfile.Tag.AlbumArtists = Array.Empty<string>();
                    }

                    if (!string.IsNullOrWhiteSpace(editComposerText))
                        tfile.Tag.Composers = new[] { editComposerText };

                    if (!string.IsNullOrWhiteSpace(editLyricsText))
                    {
                        tfile.Tag.Lyrics = editLyricsText.Trim();
                    }

                    string effectiveCoverArtist = !string.IsNullOrWhiteSpace(normAlbumArtist) ? normAlbumArtist : normalizedPerformers;

                    // Embed new picture if picked
                    if (!string.IsNullOrEmpty(pendingCover) && File.Exists(pendingCover))
                    {
                        var pic = new TagLib.Picture(pendingCover);
                        tfile.Tag.Pictures = new TagLib.IPicture[] { pic };

                        // Also save cached cover file for UI with unique timestamped case-sensitive hash
                        string cachedPath = BuildTimestampedCoverCachePath(effectiveCoverArtist, editAlbumTitle, pendingCover);
                        try
                        {
                            File.Copy(pendingCover, cachedPath, true);
                            trackRef.CoverImagePath = cachedPath;
                        }
                        catch { }
                    }
                    else if (albumOrArtistChanged && tfile.Tag.Pictures.Length > 0)
                    {
                        // Re-key cached cover independently so splitting/renaming does not collide with another album
                        string cachedPath = BuildTimestampedCoverCachePath(effectiveCoverArtist, editAlbumTitle, ".jpg");
                        try
                        {
                            var picBytes = Helpers.ImageHelper.EnsureDisplayableBytes(tfile.Tag.Pictures[0].Data.Data);
                            if (picBytes != null && picBytes.Length > 0)
                            {
                                File.WriteAllBytes(cachedPath, picBytes);
                                trackRef.CoverImagePath = cachedPath;
                            }
                        }
                        catch { }
                    }

                    if (isFlac)
                    {
                        tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
                    }

                    tfile.Save();
                    return true;
                });

                // Save lyrics to local cache and .lrc if provided
                if (!string.IsNullOrWhiteSpace(EditLyricsBox.Text))
                {
                    await LrcService.SaveLrcForTrackAsync(_selectedTrack, EditLyricsBox.Text.Trim());
                    _loadedLyrics = EditLyricsBox.Text.Trim();
                }

                // Update in-memory track object
                _selectedTrack.Title = EditTitleBox.Text.Trim();
                _selectedTrack.Artist = normalizedPerformers;
                _selectedTrack.AlbumArtist = normAlbumArtist;
                _selectedTrack.Album = editAlbumTitle;
                _selectedTrack.ReleaseType = parsedReleaseType;
                if (int.TryParse(EditTrackBox.Text.Trim(), out int trkNum)) _selectedTrack.TrackNumber = trkNum;
                _selectedTrack.Disc = Helpers.DiscMetadataHelper.NormalizeSavedDiscValue(EditDiscBox.Text);
                if (int.TryParse(EditYearBox.Text.Trim(), out int yrNum)) _selectedTrack.Year = yrNum;
                _selectedTrack.Genre = EditGenreBox.Text.Trim();
                if (double.TryParse(EditBpmBox.Text.Trim(), out double finalBpm) && finalBpm > 0)
                    _selectedTrack.Bpm = Math.Round(finalBpm);
                string mappedCamelot = Helpers.HarmonicAnalysisHelper.MapKeyToCamelot(EditCamelotBox.Text.Trim());
                if (!string.IsNullOrEmpty(mappedCamelot))
                    _selectedTrack.CamelotKey = mappedCamelot;
                _selectedTrack.HasUnembeddedMetadata = false;

                // Save to database & broadcast real-time update to all app views
                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    await vm.DbService.InsertTracksAsync(_allTracks);
                    vm.NotifyTrackMetadataChanged(_selectedTrack, lyricsUpdated: !string.IsNullOrWhiteSpace(EditLyricsBox.Text));
                }

                BuildAlbumItemsList();
                if (_isAlbumMode)
                {
                    FilterAndDisplayAlbums();
                    if (_selectedAlbum != null)
                    {
                        LoadAlbumDetails(_selectedAlbum);
                    }
                }
                else
                {
                    CancelEdit();
                    LoadTrackDetails(_selectedTrack);
                    FilterAndDisplayTracks();
                }
                _ = RefreshLibraryMissingMetadataStateAsync();

                ShowInfoDialog("¡Metadatos Guardados!", "Los metadatos y la portada han sido incrustados exitosamente dentro del archivo.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error al guardar", $"No se pudieron escribir los cambios en el archivo: {ex.Message}");
            }
        }

        // ==========================================
        // ÁLBUMES: SELECCIÓN Y EDICIÓN MASIVA
        // ==========================================

        private void AlbumsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlbumsListView.SelectedItem is AlbumItem album)
            {
                LoadAlbumDetails(album);
            }
        }

        private void LoadAlbumDetails(AlbumItem album)
        {
            _selectedAlbum = album;
            if (!_isAlbumMode) return;

            _pendingNewAlbumCoverPath = null;
            CancelAlbumEdit();

            AlbumDisplayTitleText.Text = album.Title;
            AlbumDisplayArtistText.Text = album.Artist;
            AlbumDisplayReleaseTypeText.Text = album.ReleaseType;
            AlbumDisplayReleaseTypeBadgeText.Text = album.ReleaseType.ToUpperInvariant();
            AlbumDisplayTracksCountText.Text = album.TrackCountFormatted;
            AlbumDisplayTracksCountBadgeText.Text = album.TrackCountFormatted;

            AlbumTracksSummaryListView.ItemsSource = album.Tracks;

            string? coverPath = album.CoverImagePath;
            if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
            {
                try
                {
                    CoverImageDisplay.Source = new BitmapImage(new Uri(coverPath));
                }
                catch
                {
                    CoverImageDisplay.Source = null;
                }
            }
            else
            {
                CoverImageDisplay.Source = null;
            }
        }

        private void StartAlbumEditMode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAlbum == null) return;
            _isAlbumMode = true;
            SyncLeftPanelVisibility(isEditingAlbum: true);

            EditAlbumGroupTitleBox.Text = _selectedAlbum.Title;
            EditAlbumGroupArtistBox.Text = _selectedAlbum.Artist;

            string curType = _selectedAlbum.ReleaseType;
            foreach (ComboBoxItem item in EditAlbumGroupTypeBox.Items)
            {
                if (string.Equals(item.Content?.ToString(), curType, StringComparison.OrdinalIgnoreCase))
                {
                    EditAlbumGroupTypeBox.SelectedItem = item;
                    break;
                }
            }
            if (EditAlbumGroupTypeBox.SelectedItem == null && EditAlbumGroupTypeBox.Items.Count > 0)
            {
                EditAlbumGroupTypeBox.SelectedIndex = 0;
            }
        }

        private void CancelAlbumEdit_Click(object sender, RoutedEventArgs e)
        {
            CancelAlbumEdit();
        }

        private void CancelAlbumEdit()
        {
            _pendingNewAlbumCoverPath = null;
            SyncLeftPanelVisibility(isEditingTrack: false, isEditingAlbum: false);
            if (_isAlbumMode && _selectedAlbum != null)
            {
                string? coverPath = _selectedAlbum.CoverImagePath;
                if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
                {
                    try { CoverImageDisplay.Source = new BitmapImage(new Uri(coverPath)); } catch { }
                }
                else
                {
                    CoverImageDisplay.Source = null;
                }
            }
        }

        private async void SaveAlbumMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAlbum == null || _selectedAlbum.Tracks.Count == 0) return;

            string newTitle = EditAlbumGroupTitleBox.Text.Trim();
            string newArtist = EditAlbumGroupArtistBox.Text.Trim();
            string newType = (EditAlbumGroupTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                          ?? Helpers.ReleaseTypeHelper.TypeAlbum;

            if (string.IsNullOrWhiteSpace(newTitle))
            {
                ShowInfoDialog("Nombre requerido", "El álbum debe tener un nombre válido.");
                return;
            }

            var tracksToUpdate = _selectedAlbum.Tracks.ToList();
            string? pendingCover = _pendingNewAlbumCoverPath;

            var splitArtists = string.IsNullOrWhiteSpace(newArtist)
                ? new List<string>()
                : Helpers.ArtistHelper.SplitArtists(newArtist);
            string normalizedArtist = splitArtists.Count > 0 ? string.Join("; ", splitArtists) : string.Empty;

            try
            {
                string? cachedNewCoverPath = null;
                if (!string.IsNullOrEmpty(pendingCover) && File.Exists(pendingCover))
                {
                    string effectiveArtist = !string.IsNullOrWhiteSpace(normalizedArtist) ? normalizedArtist : _selectedAlbum.Artist;
                    cachedNewCoverPath = BuildTimestampedCoverCachePath(effectiveArtist, newTitle, pendingCover);
                    File.Copy(pendingCover, cachedNewCoverPath, true);
                }

                StopLrcPlayback();

                await ExecuteWithPlaybackFileUnlockedAsync(tracksToUpdate.Select(t => t.FilePath), () =>
                {
                    foreach (var track in tracksToUpdate)
                    {
                        if (!File.Exists(track.FilePath)) continue;

                        try
                        {
                            using var tfile = TagLib.File.Create(track.FilePath);
                            bool isFlac = track.FilePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) || tfile is TagLib.Flac.File;

                            if (isFlac)
                            {
                                // Remove any invalid ID3 headers so Windows Explorer reads the fLaC stream correctly
                                tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);

                                if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                                {
                                    xiph.SetField("ALBUM", newTitle);
                                    if (!string.IsNullOrWhiteSpace(normalizedArtist))
                                    {
                                        xiph.SetField("ALBUMARTIST", normalizedArtist);
                                        xiph.SetField("ALBUM ARTIST", normalizedArtist);
                                    }
                                }
                            }

                            tfile.Tag.Album = newTitle;

                            if (!string.IsNullOrWhiteSpace(normalizedArtist))
                            {
                                tfile.Tag.AlbumArtists = splitArtists.ToArray();
                            }

                            Helpers.ReleaseTypeHelper.SetReleaseTypeInTag(tfile, newType);

                            if (!string.IsNullOrEmpty(pendingCover) && File.Exists(pendingCover))
                            {
                                var pic = new TagLib.Picture(pendingCover);
                                tfile.Tag.Pictures = new TagLib.IPicture[] { pic };
                            }

                            if (isFlac)
                            {
                                tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
                            }

                            tfile.Save();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error updating track {track.FilePath}: {ex.Message}");
                        }

                        // Update in-memory track
                        track.Album = newTitle;
                        if (!string.IsNullOrWhiteSpace(normalizedArtist))
                        {
                            track.AlbumArtist = normalizedArtist;
                        }
                        track.ReleaseType = newType;
                        if (cachedNewCoverPath != null)
                        {
                            track.CoverImagePath = cachedNewCoverPath;
                        }
                    }
                    return true;
                });

                // Update selected album object
                _selectedAlbum.Title = newTitle;
                if (!string.IsNullOrWhiteSpace(normalizedArtist)) _selectedAlbum.Artist = normalizedArtist;
                _selectedAlbum.ReleaseType = newType;
                if (cachedNewCoverPath != null) _selectedAlbum.CoverImagePath = cachedNewCoverPath;

                // Save database and notify
                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    await vm.DbService.InsertTracksAsync(_allTracks);
                    vm.NotifyTracksMetadataChanged(tracksToUpdate);
                }

                BuildAlbumItemsList();
                FilterAndDisplayAlbums();
                if (_selectedAlbum != null && AlbumsListView.Items.Contains(_selectedAlbum))
                {
                    AlbumsListView.SelectedItem = _selectedAlbum;
                }
                CancelAlbumEdit();
                if (_selectedAlbum != null)
                {
                    LoadAlbumDetails(_selectedAlbum);
                }

                ShowInfoDialog("¡Álbum Actualizado!",
                    $"Se han guardado e incrustado exitosamente los cambios (carátula, nombre, artista y tipo '{newType}') en las {tracksToUpdate.Count} canciones del álbum.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error al guardar álbum", $"Ocurrió un error al actualizar los archivos: {ex.Message}");
            }
        }

        private static string BuildTimestampedCoverCachePath(string artistName, string albumTitle, string sourceFilePath)
        {
            var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
            Directory.CreateDirectory(cacheFolder);
            string safeKey = $"{artistName.Trim()}_{albumTitle.Trim()}_{DateTime.Now.Ticks}";
            using var md5 = System.Security.Cryptography.MD5.Create();
            string hashFileName = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(safeKey))).ToLowerInvariant();
            string ext = Path.GetExtension(sourceFilePath);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            return Path.Combine(cacheFolder, $"{hashFileName}{ext}");
        }

        /// <summary>
        /// Verifica si existen datos en el objeto Track (Base de Datos) que aún no están escritos en las etiquetas físicas del archivo.
        /// </summary>
        private static bool HasDatabaseMetadataMissingInFile(Track track, TagLib.File tfile)
        {
            if (track == null || tfile?.Tag == null) return false;

            var (tagBpm, tagKey) = Helpers.HarmonicAnalysisHelper.ExtractHarmonicMetadataFromTag(tfile);
            string dbKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

            // 1. BPM presente en BD pero ausente en el archivo (o actualizado por re-análisis pendiente de incrustar)
            if (track.Bpm > 0 && (tagBpm <= 0 || (track.HasUnembeddedMetadata && Math.Abs(tagBpm - track.Bpm) >= 1.0)))
                return true;

            // 2. Clave Camelot presente en BD pero ausente en el archivo (o actualizada por re-análisis pendiente de incrustar)
            if (!string.IsNullOrEmpty(dbKey) && (string.IsNullOrEmpty(tagKey) || (track.HasUnembeddedMetadata && !string.Equals(tagKey, dbKey, StringComparison.OrdinalIgnoreCase))))
                return true;

            // 3. Disco presente en BD pero ausente en el archivo
            string fileDisc = Helpers.DiscMetadataHelper.GetDiscFromTag(tfile);
            if (!string.IsNullOrWhiteSpace(track.Disc) && string.IsNullOrWhiteSpace(fileDisc))
                return true;

            // 4. Año, Número de pista o Género presentes en BD pero ausentes en el archivo
            if (track.Year > 0 && tfile.Tag.Year == 0)
                return true;

            if (track.TrackNumber > 0 && tfile.Tag.Track == 0)
                return true;

            if (!string.IsNullOrWhiteSpace(track.Genre) &&
                !track.Genre.Equals("Unknown Genre", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(tfile.Tag.FirstGenre))
                return true;

            return false;
        }

        /// <summary>
        /// Incrusta únicamente los metadatos que existen en la Base de Datos pero faltan en el archivo físico, sin sobrescribir etiquetas existentes.
        /// </summary>
        private static bool TryEmbedMissingMetadataIntoFile(Track track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                return false;

            using var tfile = TagLib.File.Create(track.FilePath);
            if (!HasDatabaseMetadataMissingInFile(track, tfile))
                return false;

            var (tagBpm, tagKey) = Helpers.HarmonicAnalysisHelper.ExtractHarmonicMetadataFromTag(tfile);
            string dbKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);
            bool needWriteBpm = track.Bpm > 0 && (tagBpm <= 0 || (track.HasUnembeddedMetadata && Math.Abs(tagBpm - track.Bpm) >= 1.0));
            bool needWriteKey = !string.IsNullOrEmpty(dbKey) && (string.IsNullOrEmpty(tagKey) || (track.HasUnembeddedMetadata && !string.Equals(tagKey, dbKey, StringComparison.OrdinalIgnoreCase)));

            double bpmToWrite = needWriteBpm ? track.Bpm : tagBpm;
            string keyToWrite = needWriteKey ? dbKey : tagKey;

            if (needWriteBpm || needWriteKey)
            {
                Helpers.HarmonicAnalysisHelper.WriteHarmonicMetadataToTag(tfile, bpmToWrite, keyToWrite);
            }

            string fileDisc = Helpers.DiscMetadataHelper.GetDiscFromTag(tfile);
            if (!string.IsNullOrWhiteSpace(track.Disc) && string.IsNullOrWhiteSpace(fileDisc))
            {
                Helpers.DiscMetadataHelper.SetDiscInTag(tfile, track.Disc);
            }

            if (track.Year > 0 && tfile.Tag.Year == 0)
            {
                tfile.Tag.Year = (uint)track.Year;
            }

            if (track.TrackNumber > 0 && tfile.Tag.Track == 0)
            {
                tfile.Tag.Track = (uint)track.TrackNumber;
            }

            if (!string.IsNullOrWhiteSpace(track.Genre) &&
                !track.Genre.Equals("Unknown Genre", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(tfile.Tag.FirstGenre))
            {
                tfile.Tag.Genres = new[] { track.Genre.Trim() };
            }

            if (string.IsNullOrWhiteSpace(tfile.Tag.Title) && !string.IsNullOrWhiteSpace(track.Title))
            {
                tfile.Tag.Title = track.Title.Trim();
            }

            if ((tfile.Tag.Performers == null || tfile.Tag.Performers.Length == 0) &&
                !string.IsNullOrWhiteSpace(track.Artist) &&
                !track.Artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
            {
                tfile.Tag.Performers = Helpers.ArtistHelper.SplitArtists(track.Artist).ToArray();
            }

            if (string.IsNullOrWhiteSpace(tfile.Tag.Album) &&
                !string.IsNullOrWhiteSpace(track.Album) &&
                !track.Album.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase))
            {
                tfile.Tag.Album = track.Album.Trim();
            }

            bool isFlac = track.FilePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) || tfile is TagLib.Flac.File;
            if (isFlac)
            {
                tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
                if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                {
                    if (!string.IsNullOrWhiteSpace(track.Album) && !track.Album.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase))
                    {
                        xiph.SetField("ALBUM", track.Album.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(track.AlbumArtist))
                    {
                        xiph.SetField("ALBUMARTIST", track.AlbumArtist.Trim());
                        xiph.SetField("ALBUM ARTIST", track.AlbumArtist.Trim());
                    }
                }
            }

            tfile.Save();
            track.HasUnembeddedMetadata = false;
            return true;
        }

        private Task RefreshLibraryMissingMetadataStateAsync()
        {
            // Evaluación 100% en memoria (0ms, cero lecturas de disco al abrir la ventana)
            int missingCount = 0;
            for (int i = 0; i < _allTracks.Count; i++)
            {
                if (_allTracks[i].HasUnembeddedMetadata)
                {
                    missingCount++;
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (missingCount > 0)
                {
                    EmbedAllMissingToolbarText.Text = missingCount > 1
                        ? $"Incrustar Metadatos Faltantes ({missingCount})"
                        : "Incrustar Metadatos Faltantes";
                    EmbedAllMissingToolbarButton.Visibility = Visibility.Visible;
                }
                else
                {
                    EmbedAllMissingToolbarButton.Visibility = Visibility.Collapsed;
                }
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Si el archivo de audio que se va a modificar está actualmente abierto en BassAudioEngine,
        /// transfiere temporalmente la reproducción a memoria RAM (sin pausar ni cortar el audio),
        /// escribe las etiquetas en el disco y vuelve a vincular el stream físico.
        /// </summary>
        private static async Task<T> ExecuteWithPlaybackFileUnlockedAsync<T>(IEnumerable<string> targetPaths, Func<T> writeAction)
        {
            var vm = MainViewModel.Instance;
            string? activePath = vm?.CurrentTrack?.FilePath;
            bool switchedToMemory = false;

            if (vm != null && !vm.IsKaraokeActive && !string.IsNullOrWhiteSpace(activePath) && File.Exists(activePath))
            {
                bool matchesActive = targetPaths.Any(p => !string.IsNullOrWhiteSpace(p) && p.Equals(activePath, StringComparison.OrdinalIgnoreCase));
                if (matchesActive)
                {
                    try
                    {
                        byte[] ramBuffer = await Task.Run(() => File.ReadAllBytes(activePath));
                        switchedToMemory = await vm.AudioEngine.SwitchStreamFromMemoryAsync(ramBuffer);
                    }
                    catch { }
                }
            }

            try
            {
                return await Task.Run(writeAction);
            }
            finally
            {
                if (switchedToMemory && vm != null && !string.IsNullOrWhiteSpace(activePath) && File.Exists(activePath))
                {
                    try
                    {
                        await vm.AudioEngine.SwitchStreamPreservingPositionAsync(activePath);
                    }
                    catch { }
                }
            }
        }

        private async void EmbedMissingMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null || !File.Exists(_selectedTrack.FilePath)) return;

            var track = _selectedTrack;
            EmbedMissingMetadataButton.IsEnabled = false;
            StopLrcPlayback();

            try
            {
                bool embedded = await ExecuteWithPlaybackFileUnlockedAsync(
                    new[] { track.FilePath },
                    () => TryEmbedMissingMetadataIntoFile(track));

                if (embedded)
                {
                    var vm = MainViewModel.Instance;
                    if (vm != null)
                    {
                        await vm.DbService.InsertTracksAsync(_allTracks);
                        vm.NotifyTrackMetadataChanged(track);
                    }

                    EmbedMissingMetadataButton.Visibility = Visibility.Collapsed;
                    if (_isAlbumMode)
                    {
                        if (_selectedAlbum != null) LoadAlbumDetails(_selectedAlbum);
                    }
                    else
                    {
                        LoadTrackDetails(track);
                    }
                    _ = RefreshLibraryMissingMetadataStateAsync();

                    ShowInfoDialog("¡Metadatos Incrustados!",
                        "Los datos registrados en la Base de Datos (como BPM y Clave Armónica Camelot) se han incrustado directamente en las etiquetas del archivo de audio.");
                }
                else
                {
                    EmbedMissingMetadataButton.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error al incrustar metadatos",
                    $"No se pudieron incrustar los metadatos en el archivo (verifica que no esté en uso por otro proceso): {ex.Message}");
            }
            finally
            {
                EmbedMissingMetadataButton.IsEnabled = true;
            }
        }

        private async void EmbedAllMissingMetadata_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = _allTracks.Where(t => t.HasUnembeddedMetadata).ToList();
            if (snapshot.Count == 0)
            {
                snapshot = _allTracks.ToList();
            }
            if (snapshot.Count == 0) return;

            EmbedAllMissingToolbarButton.IsEnabled = false;
            ReanalyzeAllHarmonicButton.IsEnabled = false;
            EmbedAllMissingToolbarText.Text = "Incrustando metadatos...";
            StopLrcPlayback();

            try
            {
                var modifiedTracks = new List<Track>();
                var (embeddedCount, failedCount) = await ExecuteWithPlaybackFileUnlockedAsync(
                    snapshot.Select(t => t.FilePath),
                    () =>
                    {
                        int ok = 0;
                        int fail = 0;
                        foreach (var t in snapshot)
                        {
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
                                {
                                    if (TryEmbedMissingMetadataIntoFile(t))
                                    {
                                        ok++;
                                        modifiedTracks.Add(t);
                                    }
                                }
                            }
                            catch
                            {
                                fail++;
                            }
                        }
                        return (ok, fail);
                    });

                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    await vm.DbService.InsertTracksAsync(_allTracks);
                    if (modifiedTracks.Count > 0)
                    {
                        vm.NotifyTracksMetadataChanged(modifiedTracks);
                    }
                }

                BuildAlbumItemsList();
                if (_isAlbumMode)
                {
                    FilterAndDisplayAlbums();
                    if (_selectedAlbum != null)
                    {
                        LoadAlbumDetails(_selectedAlbum);
                    }
                    else
                    {
                        SyncLeftPanelVisibility();
                    }
                }
                else if (_selectedTrack != null)
                {
                    LoadTrackDetails(_selectedTrack);
                }

                await RefreshLibraryMissingMetadataStateAsync();

                string msg = failedCount > 0
                    ? $"Se incrustaron exitosamente los metadatos faltantes (BPM, Clave Camelot, etc.) en {embeddedCount} archivo(s). ({failedCount} archivo(s) omitidos por estar bloqueados)."
                    : $"Se incrustaron exitosamente los metadatos faltantes (BPM, Clave Camelot, etc.) en {embeddedCount} archivo(s) de audio.";

                ShowInfoDialog("¡Metadatos Faltantes Incrustados!", msg);
            }
            finally
            {
                EmbedAllMissingToolbarButton.IsEnabled = true;
                ReanalyzeAllHarmonicButton.IsEnabled = true;
            }
        }

        private async void ReanalyzeSingleHarmonic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null || string.IsNullOrWhiteSpace(_selectedTrack.FilePath) || !File.Exists(_selectedTrack.FilePath))
                return;

            var track = _selectedTrack;
            double oldBpm = track.Bpm;
            string oldKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

            ReanalyzeSingleHarmonicButton.IsEnabled = false;
            ReanalyzeSingleHarmonicText.Text = "Analizando audio (DSP)...";

            try
            {
                var (newBpm, newKey, hasUnembeddedDiff) = await Task.Run(() =>
                    Helpers.HarmonicAnalysisHelper.ComputeTrackHarmonicSync(track, forceDsp: true));

                // Mutar Track estrictamente en el hilo UI para no disparar INotifyPropertyChanged desde otro hilo
                if (newBpm > 0) track.Bpm = newBpm;
                if (!string.IsNullOrEmpty(newKey)) track.CamelotKey = newKey;
                if (hasUnembeddedDiff) track.HasUnembeddedMetadata = true;

                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    await vm.DbService.InsertTracksAsync(_allTracks);
                    vm.NotifyTrackMetadataChanged(track);
                    if (vm.CurrentTrack == track)
                    {
                        vm.UpdateCurrentTrackTechnicalInfo();
                    }
                }

                LoadTrackDetails(track);
                await RefreshLibraryMissingMetadataStateAsync();

                string oldKeyStr = string.IsNullOrEmpty(oldKey) ? "-" : oldKey;
                string newKeyStr = string.IsNullOrEmpty(newKey) ? "-" : newKey;
                string pendingNote = track.HasUnembeddedMetadata
                    ? "\n\nPulsa 'Incrustar Metadatos Faltantes' para escribir estos valores corregidos directamente en el archivo físico."
                    : "\n\nLas etiquetas del archivo físico ya coinciden con este análisis.";

                ShowInfoDialog("¡Re-análisis DSP Completado!",
                    $"Anterior: {oldBpm:0} BPM | Clave {oldKeyStr}\nNuevo (Offline DSP): {newBpm:0} BPM | Clave {newKeyStr}{pendingNote}");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error en análisis DSP", $"No se pudo analizar el audio: {ex.Message}");
            }
            finally
            {
                ReanalyzeSingleHarmonicText.Text = "Reanalizar BPM / Clave (DSP)";
                ReanalyzeSingleHarmonicButton.IsEnabled = true;
            }
        }

        private async void ReanalyzeAllHarmonic_Click(object sender, RoutedEventArgs e)
        {
            if (_isHarmonicBatchRunning)
            {
                _harmonicBatchCts?.Cancel();
                ReanalyzeAllHarmonicButton.IsEnabled = false;
                ReanalyzeAllHarmonicText.Text = "Deteniendo...";
                return;
            }

            var targets = _allTracks
                .Where(t => t != null && !t.IsDiscHeader && !string.IsNullOrWhiteSpace(t.FilePath) && File.Exists(t.FilePath))
                .ToList();

            if (targets.Count == 0) return;

            _isHarmonicBatchRunning = true;
            _harmonicBatchCts?.Dispose();
            _harmonicBatchCts = new CancellationTokenSource();
            var ct = _harmonicBatchCts.Token;

            ReanalyzeSingleHarmonicButton.IsEnabled = false;
            EmbedAllMissingToolbarButton.IsEnabled = false;
            ReanalyzeAllHarmonicText.Text = $"Detener (0/{targets.Count})";

            int processed = 0;
            int changedCount = 0;
            int failedCount = 0;
            var changedTracks = new ConcurrentBag<Track>();
            var uiQueue = new ConcurrentQueue<(Track track, double bpm, string key, bool hasUnembedded)>();
            var vm = MainViewModel.Instance;
            int parallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

            int lastSavedCount = 0;
            Task activeDbSaveTask = Task.CompletedTask;
            Exception? unexpectedError = null;

            void DrainUiUpdates(bool allowDbCheckpoint)
            {
                while (uiQueue.TryDequeue(out var item))
                {
                    if (item.bpm > 0) item.track.Bpm = item.bpm;
                    if (!string.IsNullOrEmpty(item.key)) item.track.CamelotKey = item.key;
                    if (item.hasUnembedded) item.track.HasUnembeddedMetadata = true;
                }

                int done = Volatile.Read(ref processed);
                if (_isHarmonicBatchRunning && !ct.IsCancellationRequested)
                {
                    ReanalyzeAllHarmonicText.Text = $"Detener ({done}/{targets.Count})";
                }

                if (allowDbCheckpoint && vm != null && done - lastSavedCount >= 100 && activeDbSaveTask.IsCompleted)
                {
                    lastSavedCount = done;
                    var snapshot = _allTracks.ToList();
                    activeDbSaveTask = Task.Run(async () =>
                    {
                        try { await vm.DbService.InsertTracksAsync(snapshot); } catch { }
                    });
                }
            }

            var uiTimer = DispatcherQueue.CreateTimer();
            uiTimer.Interval = TimeSpan.FromMilliseconds(180);
            uiTimer.Tick += (_, _) => DrainUiUpdates(allowDbCheckpoint: true);
            uiTimer.Start();

            try
            {
                await Task.Run(async () =>
                {
                    await Parallel.ForEachAsync(
                        targets,
                        new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
                        (track, token) =>
                        {
                            try
                            {
                                token.ThrowIfCancellationRequested();

                                double prevBpm = track.Bpm;
                                string prevKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

                                var (newBpm, newKey, hasUnembeddedDiff) =
                                    Helpers.HarmonicAnalysisHelper.ComputeTrackHarmonicSync(track, forceDsp: true);

                                bool changed = Math.Abs(prevBpm - newBpm) >= 0.5
                                            || !string.Equals(prevKey, newKey, StringComparison.OrdinalIgnoreCase)
                                            || hasUnembeddedDiff;

                                if (changed)
                                {
                                    Interlocked.Increment(ref changedCount);
                                    changedTracks.Add(track);
                                }

                                uiQueue.Enqueue((track, newBpm, newKey, hasUnembeddedDiff));
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch
                            {
                                Interlocked.Increment(ref failedCount);
                            }
                            finally
                            {
                                Interlocked.Increment(ref processed);
                            }

                            return ValueTask.CompletedTask;
                        });
                });
            }
            catch (OperationCanceledException)
            {
                // Cancelación limpia solicitada por el usuario o cierre de ventana
            }
            catch (Exception ex)
            {
                unexpectedError = ex;
            }
            finally
            {
                uiTimer.Stop();
                try { await activeDbSaveTask; } catch { }

                // Aplicar el lote final estrictamente en el hilo UI antes de persistir
                DrainUiUpdates(allowDbCheckpoint: false);

                if (vm != null)
                {
                    try { await vm.DbService.InsertTracksAsync(_allTracks); } catch { }
                    var modifiedList = changedTracks.ToList();
                    if (modifiedList.Count > 0)
                    {
                        vm.NotifyTracksMetadataChanged(modifiedList);
                        if (vm.CurrentTrack != null && modifiedList.Contains(vm.CurrentTrack))
                        {
                            vm.UpdateCurrentTrackTechnicalInfo();
                        }
                    }
                }

                _isHarmonicBatchRunning = false;
                ReanalyzeAllHarmonicText.Text = "Reanalizar BPM / Clave";
                ReanalyzeAllHarmonicButton.IsEnabled = true;
                ReanalyzeSingleHarmonicButton.IsEnabled = true;
                EmbedAllMissingToolbarButton.IsEnabled = true;

                if (_isAlbumMode && _selectedAlbum != null)
                {
                    LoadAlbumDetails(_selectedAlbum);
                }
                else if (!_isAlbumMode && _selectedTrack != null)
                {
                    LoadTrackDetails(_selectedTrack);
                }

                await RefreshLibraryMissingMetadataStateAsync();

                int pendingEmbeds = _allTracks.Count(t => t.HasUnembeddedMetadata);
                bool wasCancelled = ct.IsCancellationRequested;
                string title = unexpectedError != null
                    ? "Re-análisis DSP Interrumpido"
                    : (wasCancelled ? "Re-análisis DSP Detenido" : "¡Re-análisis DSP Completado!");

                string summary = $"Se analizaron {processed} de {targets.Count} pistas con el motor DSP offline ({changedCount} actualizadas).";
                if (failedCount > 0)
                {
                    summary += $"\n({failedCount} archivo(s) omitidos por no poder decodificarse).";
                }
                if (unexpectedError != null)
                {
                    summary += $"\nDetalle: {unexpectedError.Message}";
                }
                summary += "\n\n";
                summary += pendingEmbeds > 0
                    ? $"Hay {pendingEmbeds} archivo(s) con datos nuevos o corregidos listos para guardarse en disco. Pulsa 'Incrustar Metadatos Faltantes ({pendingEmbeds})' en la barra superior cuando quieras escribirlos en tus archivos."
                    : "Todos los archivos físicos ya coinciden con el análisis DSP.";

                try
                {
                    if (Content?.XamlRoot != null)
                    {
                        ShowInfoDialog(title, summary);
                    }
                }
                catch { }
            }
        }

        private async void OpenExternalFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".opus");

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                if (_isAlbumMode)
                {
                    SwitchToMode(false);
                }

                var existing = _allTracks.FirstOrDefault(t => t.FilePath.Equals(file.Path, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    TracksListView.SelectedItem = existing;
                    TracksListView.ScrollIntoView(existing);
                }
                else
                {
                    // Create temporary track representation for this file
                    var newTrack = new Track
                    {
                        Id = _allTracks.Count + 1,
                        FilePath = file.Path,
                        Title = Path.GetFileNameWithoutExtension(file.Path),
                        Artist = "Desconocido",
                        Album = "Desconocido",
                        DisplayIndex = _allTracks.Count + 1
                    };
                    _allTracks.Insert(0, newTrack);
                    FilterAndDisplayTracks();
                    TracksListView.SelectedItem = newTrack;
                }
            }
        }

        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack != null && File.Exists(_selectedTrack.FilePath))
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{_selectedTrack.FilePath}\"");
                }
                catch { }
            }
        }

        private async void ShowInfoDialog(string title, string content)
        {
            try
            {
                if (this.Content?.XamlRoot == null) return;

                try
                {
                    _activeDialog?.Hide();
                }
                catch { }

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "Entendido",
                    XamlRoot = this.Content.XamlRoot
                };
                _activeDialog = dialog;
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MetadataWindow] ShowInfoDialog skipped: {ex.Message}");
            }
            finally
            {
                _activeDialog = null;
            }
        }

        private async void OpenLrcEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null) return;

            LrcTrackInfoText.Text = $"{_selectedTrack.Title} • {_selectedTrack.Artist}";

            string lyricsText = !string.IsNullOrWhiteSpace(EditLyricsBox.Text)
                ? EditLyricsBox.Text
                : (_loadedLyrics ?? await LrcService.LoadRawLrcForTrackAsync(_selectedTrack) ?? string.Empty);

            LrcRawTextBox.Text = lyricsText;

            InitializeLrcAudio();

            if (lyricsText.Contains("[") && lyricsText.Contains("]"))
            {
                var parsed = LrcService.Parse(lyricsText);
                if (parsed.Count > 0)
                {
                    _activeLrcLines = parsed;
                    LrcLinesListView.ItemsSource = _activeLrcLines;
                    LrcRawView.Visibility = Visibility.Collapsed;
                    LrcSyncView.Visibility = Visibility.Visible;
                }
                else
                {
                    LrcRawView.Visibility = Visibility.Visible;
                    LrcSyncView.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                LrcRawView.Visibility = Visibility.Visible;
                LrcSyncView.Visibility = Visibility.Collapsed;
            }

            LrcGeneratorOverlay.Visibility = Visibility.Visible;
        }

        private void CloseLrcEditor_Click(object sender, RoutedEventArgs e)
        {
            StopLrcPlayback();
            LrcGeneratorOverlay.Visibility = Visibility.Collapsed;
        }

        private void InitializeLrcAudio()
        {
            StopLrcPlayback();

            if (_selectedTrack == null || !File.Exists(_selectedTrack.FilePath)) return;

            try
            {
                _lrcStream = Bass.CreateStream(_selectedTrack.FilePath, 0, 0, BassFlags.Prescan);
                if (_lrcStream != 0)
                {
                    long totalBytes = Bass.ChannelGetLength(_lrcStream, PositionFlags.Bytes);
                    double totalSecs = Bass.ChannelBytes2Seconds(_lrcStream, totalBytes);
                    LrcProgressSlider.Maximum = totalSecs > 0 ? totalSecs : 100;
                    LrcProgressSlider.Value = 0;

                    var totalSpan = TimeSpan.FromSeconds(totalSecs);
                    LrcTotalTimeText.Text = $"{((int)totalSpan.TotalMinutes):D2}:{totalSpan.Seconds:D2}.{(totalSpan.Milliseconds / 10):D2}";
                    LrcCurrentTimeText.Text = "00:00.00";
                    LrcPlayPauseIcon.Glyph = "\uE768";

                    _lrcTimer = this.DispatcherQueue.CreateTimer();
                    _lrcTimer.Interval = TimeSpan.FromMilliseconds(60);
                    _lrcTimer.Tick += (s, e) =>
                    {
                        if (_lrcStream != 0 && Bass.ChannelIsActive(_lrcStream) == PlaybackState.Playing)
                        {
                            long posBytes = Bass.ChannelGetPosition(_lrcStream, PositionFlags.Bytes);
                            double posSecs = Bass.ChannelBytes2Seconds(_lrcStream, posBytes);
                            LrcProgressSlider.Value = posSecs;
                            var posSpan = TimeSpan.FromSeconds(posSecs);
                            LrcCurrentTimeText.Text = $"{((int)posSpan.TotalMinutes):D2}:{posSpan.Seconds:D2}.{(posSpan.Milliseconds / 10):D2}";

                            if (_activeLrcLines.Count > 0)
                            {
                                int activeIdx = LrcService.FindActiveIndex(_activeLrcLines, posSpan);
                                for (int i = 0; i < _activeLrcLines.Count; i++)
                                {
                                    _activeLrcLines[i].IsActive = (i == activeIdx);
                                }
                            }
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing preview audio: {ex.Message}");
            }
        }

        private void StopLrcPlayback()
        {
            _lrcTimer?.Stop();
            _lrcTimer = null;
            if (_lrcStream != 0)
            {
                Bass.ChannelStop(_lrcStream);
                Bass.StreamFree(_lrcStream);
                _lrcStream = 0;
            }
            if (LrcPlayPauseIcon != null) LrcPlayPauseIcon.Glyph = "\uE768";
        }

        private void LrcPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_lrcStream == 0) return;

            if (Bass.ChannelIsActive(_lrcStream) == PlaybackState.Playing)
            {
                Bass.ChannelPause(_lrcStream);
                LrcPlayPauseIcon.Glyph = "\uE768";
                _lrcTimer?.Stop();
            }
            else
            {
                Bass.ChannelPlay(_lrcStream);
                LrcPlayPauseIcon.Glyph = "\uE769";
                _lrcTimer?.Start();
            }
        }

        private void LrcProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_lrcStream != 0 && Math.Abs(e.NewValue - Bass.ChannelBytes2Seconds(_lrcStream, Bass.ChannelGetPosition(_lrcStream, PositionFlags.Bytes))) > 1.0)
            {
                long targetBytes = Bass.ChannelSeconds2Bytes(_lrcStream, e.NewValue);
                Bass.ChannelSetPosition(_lrcStream, targetBytes);
            }
        }

        private void LrcStartSync_Click(object sender, RoutedEventArgs e)
        {
            string raw = LrcRawTextBox.Text ?? string.Empty;
            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrEmpty(l))
                           .ToList();

            if (lines.Count == 0)
            {
                ShowInfoDialog("Letra vacía", "Por favor pega la letra de la canción antes de comenzar a marcar tiempos.");
                return;
            }

            _activeLrcLines = new List<LrcLine>();
            foreach (var lineText in lines)
            {
                var parsedList = LrcService.Parse(lineText);
                if (parsedList.Count > 0)
                {
                    _activeLrcLines.Add(parsedList[0]);
                }
                else
                {
                    _activeLrcLines.Add(new LrcLine
                    {
                        Timestamp = TimeSpan.Zero,
                        Text = lineText
                    });
                }
            }

            LrcLinesListView.ItemsSource = null;
            LrcLinesListView.ItemsSource = _activeLrcLines;
            if (_activeLrcLines.Count > 0)
            {
                LrcLinesListView.SelectedIndex = 0;
            }

            LrcRawView.Visibility = Visibility.Collapsed;
            LrcSyncView.Visibility = Visibility.Visible;

            if (_lrcStream != 0 && Bass.ChannelIsActive(_lrcStream) != PlaybackState.Playing)
            {
                Bass.ChannelPlay(_lrcStream);
                LrcPlayPauseIcon.Glyph = "\uE769";
                _lrcTimer?.Start();
            }
        }

        private void LrcBackToRaw_Click(object sender, RoutedEventArgs e)
        {
            LrcSyncView.Visibility = Visibility.Collapsed;
            LrcRawView.Visibility = Visibility.Visible;
        }

        private void LrcStampCurrentLine_Click(object sender, RoutedEventArgs e)
        {
            if (_activeLrcLines == null || _activeLrcLines.Count == 0) return;

            int idx = LrcLinesListView.SelectedIndex;
            if (idx < 0) idx = 0;

            if (idx >= 0 && idx < _activeLrcLines.Count)
            {
                TimeSpan curPos = TimeSpan.Zero;
                if (_lrcStream != 0)
                {
                    long posBytes = Bass.ChannelGetPosition(_lrcStream, PositionFlags.Bytes);
                    double posSecs = Bass.ChannelBytes2Seconds(_lrcStream, posBytes);
                    curPos = TimeSpan.FromSeconds(posSecs);
                }

                _activeLrcLines[idx].Timestamp = curPos;

                int nextIdx = idx + 1;
                LrcLinesListView.ItemsSource = null;
                LrcLinesListView.ItemsSource = _activeLrcLines;
                if (nextIdx < _activeLrcLines.Count)
                {
                    LrcLinesListView.SelectedIndex = nextIdx;
                    LrcLinesListView.ScrollIntoView(_activeLrcLines[nextIdx]);
                }
                else
                {
                    LrcLinesListView.SelectedIndex = _activeLrcLines.Count - 1;
                }
            }
        }

        private void LrcStampSingleLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LrcLine line)
            {
                if (_lrcStream != 0)
                {
                    long posBytes = Bass.ChannelGetPosition(_lrcStream, PositionFlags.Bytes);
                    double posSecs = Bass.ChannelBytes2Seconds(_lrcStream, posBytes);
                    line.Timestamp = TimeSpan.FromSeconds(posSecs);

                    int curSel = LrcLinesListView.SelectedIndex;
                    LrcLinesListView.ItemsSource = null;
                    LrcLinesListView.ItemsSource = _activeLrcLines;
                    LrcLinesListView.SelectedIndex = curSel;
                }
            }
        }

        private void LrcLine_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is LrcLine line && line.Timestamp > TimeSpan.Zero)
            {
                if (_lrcStream != 0)
                {
                    long targetBytes = Bass.ChannelSeconds2Bytes(_lrcStream, line.Timestamp.TotalSeconds);
                    Bass.ChannelSetPosition(_lrcStream, targetBytes);
                }
            }
        }

        private async void SaveLrc_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null) return;

            string finalLrc = string.Empty;
            if (LrcSyncView.Visibility == Visibility.Visible && _activeLrcLines.Count > 0)
            {
                finalLrc = LrcService.Format(_activeLrcLines, _selectedTrack.Title, _selectedTrack.Artist, _selectedTrack.Album);
            }
            else
            {
                finalLrc = LrcRawTextBox.Text ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(finalLrc))
            {
                await LrcService.SaveLrcForTrackAsync(_selectedTrack, finalLrc);
                _loadedLyrics = finalLrc;
                EditLyricsBox.Text = finalLrc;
                DisplayLyricsStatusText.Text = "Letra sincronizada (LRC)";
                MainViewModel.Instance?.NotifyLyricsChanged(_selectedTrack);
            }

            StopLrcPlayback();
            LrcGeneratorOverlay.Visibility = Visibility.Collapsed;
            ShowInfoDialog("LRC Guardado", "La letra sincronizada con marcas de tiempo se ha generado y guardado correctamente.");
        }

        private void MetadataWorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            double h = e.NewSize.Height;
            if (w <= 0) return;

            if (LrcGeneratorOverlay != null)
            {
                LrcGeneratorOverlay.Padding = w < 640 ? new Thickness(12) : new Thickness(32);
            }

            if (w < 720)
            {
                MetadataWorkspaceGrid.Padding = new Thickness(12, 10, 12, 12);
                MetaLeftCol.Width = new GridLength(1, GridUnitType.Star);
                MetaSepCol.Width = new GridLength(0);
                MetaRightCol.Width = new GridLength(0);

                Grid.SetRow(LeftMetadataScrollViewer, 0);
                Grid.SetRowSpan(LeftMetadataScrollViewer, 1);
                Grid.SetColumn(LeftMetadataScrollViewer, 0);
                LeftMetadataScrollViewer.MaxHeight = h > 200 ? Math.Max(220, h * 0.44) : 300;
                LeftMetadataScrollViewer.Padding = new Thickness(0);

                MetadataVerticalSeparator.Visibility = Visibility.Collapsed;

                Grid.SetRow(RightMetadataPanelGrid, 2);
                Grid.SetRowSpan(RightMetadataPanelGrid, 1);
                Grid.SetColumn(RightMetadataPanelGrid, 0);
                RightMetadataPanelGrid.Margin = new Thickness(0, 10, 0, 0);

                CoverArtBorder.Width = 170;
                CoverArtBorder.Height = 170;
            }
            else
            {
                bool isCompactSide = w < 960;
                MetadataWorkspaceGrid.Padding = isCompactSide ? new Thickness(14, 12, 14, 12) : new Thickness(24, 18, 24, 18);
                double leftW = isCompactSide ? 268 : 360;
                double coverSize = isCompactSide ? 236 : 320;

                MetaLeftCol.Width = new GridLength(leftW);
                MetaSepCol.Width = GridLength.Auto;
                MetaRightCol.Width = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(LeftMetadataScrollViewer, 0);
                Grid.SetRowSpan(LeftMetadataScrollViewer, 3);
                Grid.SetColumn(LeftMetadataScrollViewer, 0);
                LeftMetadataScrollViewer.MaxHeight = double.PositiveInfinity;
                LeftMetadataScrollViewer.Padding = new Thickness(0, 0, 12, 0);

                MetadataVerticalSeparator.Visibility = Visibility.Visible;

                Grid.SetRow(RightMetadataPanelGrid, 0);
                Grid.SetRowSpan(RightMetadataPanelGrid, 3);
                Grid.SetColumn(RightMetadataPanelGrid, 2);
                RightMetadataPanelGrid.Margin = new Thickness(0);

                CoverArtBorder.Width = coverSize;
                CoverArtBorder.Height = coverSize;
            }
        }
    }
}
