using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public MetadataWindow(Track? trackToSelect = null)
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarBorder);

            ConfigureWindow();
            UpdateModeTabVisuals();
            this.Closed += (s, e) => StopLrcPlayback();
        }

        public void ShowWindow(Track? trackToSelect = null)
        {
            var vm = MainViewModel.Instance;
            if (vm != null && Content is FrameworkElement fe)
            {
                fe.RequestedTheme = vm.IsLightTheme ? ElementTheme.Light : ElementTheme.Dark;
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
            var albumGroups = _allTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Album))
                .GroupBy(t => t.Album.Trim(), StringComparer.OrdinalIgnoreCase);

            var albums = new List<AlbumItem>();

            foreach (var group in albumGroups)
            {
                var trackList = group.ToList();

                string albumArtist = trackList
                    .Select(t => t.AlbumArtist)
                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(albumArtist))
                {
                    var uniqueArtists = trackList
                        .Select(t => t.Artist.Trim())
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (uniqueArtists.Count == 1)
                    {
                        albumArtist = uniqueArtists[0];
                    }
                    else
                    {
                        var allArtists = trackList.SelectMany(t => Helpers.ArtistHelper.SplitArtists(t.Artist)).ToList();
                        albumArtist = allArtists
                            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(ag => ag.Count())
                            .FirstOrDefault()?.Key
                            ?? trackList.FirstOrDefault()?.Artist
                            ?? "Desconocido";
                    }
                }

                var coverPath = trackList.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;

                string dominantReleaseType = trackList.FirstOrDefault(t => !string.IsNullOrEmpty(t.ReleaseType) && t.ReleaseType != Helpers.ReleaseTypeHelper.TypeAlbum)?.ReleaseType
                                          ?? Helpers.ReleaseTypeHelper.DetectReleaseType(group.Key, trackList.Count, trackList.FirstOrDefault()?.ReleaseType);

                albums.Add(new AlbumItem
                {
                    Title = group.Key,
                    Artist = albumArtist,
                    CoverImagePath = coverPath,
                    ReleaseType = dominantReleaseType,
                    TrackCount = trackList.Count,
                    Tracks = trackList
                });
            }

            _allAlbumItems = albums.OrderBy(a => a.Title).ToList();
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
            CancelEdit();
            CancelAlbumEdit();
            UpdateModeTabVisuals();

            if (_isAlbumMode)
            {
                TracksContainerGrid.Visibility = Visibility.Collapsed;
                AlbumsContainerGrid.Visibility = Visibility.Visible;
                ReadOnlyPanel.Visibility = Visibility.Collapsed;
                EditModePanel.Visibility = Visibility.Collapsed;
                AlbumReadOnlyPanel.Visibility = Visibility.Visible;

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
                AlbumReadOnlyPanel.Visibility = Visibility.Collapsed;
                AlbumEditModePanel.Visibility = Visibility.Collapsed;
                ReadOnlyPanel.Visibility = Visibility.Visible;

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
                        albumArtistText = string.IsNullOrWhiteSpace(tag.FirstAlbumArtist) ? "-" : tag.FirstAlbumArtist;
                        tagTypesText = tfile.TagTypes.ToString().Replace(", ", " • ");

                        var (tagBpm, tagKey) = Helpers.HarmonicAnalysisHelper.ExtractHarmonicMetadataFromTag(tfile);
                        fileTagBpm = tagBpm;
                        fileTagCamelot = tagKey;

                        if (tagBpm > 0) detectedBpm = tagBpm;
                        if (!string.IsNullOrEmpty(tagKey)) detectedCamelot = tagKey;

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
                    if (_selectedTrack != track) return;

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
                    bool harmonicOnlyInDb = (detectedBpm > 0 && fileTagBpm <= 0) || (!string.IsNullOrEmpty(normKey) && string.IsNullOrEmpty(fileTagCamelot));
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

                // Si aún faltan BPM o Clave Camelot, calcularlos con DSP de forma diferida sin bloquear la vista
                if (detectedBpm <= 0 || string.IsNullOrEmpty(detectedCamelot))
                {
                    var (dspBpm, dspKey) = Helpers.HarmonicAnalysisHelper.EnsureTrackHarmonicSync(track);
                    if (detectedBpm <= 0 && dspBpm > 0) detectedBpm = dspBpm;
                    if (string.IsNullOrEmpty(detectedCamelot) && !string.IsNullOrEmpty(dspKey)) detectedCamelot = dspKey;
                    track.HasUnembeddedMetadata = true;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (detectedBpm > 0) track.Bpm = detectedBpm;
                        if (!string.IsNullOrEmpty(detectedCamelot)) track.CamelotKey = detectedCamelot;

                        if (_selectedTrack == track)
                        {
                            string normKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(detectedCamelot);
                            UpdateHarmonicUiDisplay(detectedBpm, normKey, true);
                            EmbedMissingMetadataButton.Visibility = Visibility.Visible;
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

            ReadOnlyPanel.Visibility = Visibility.Collapsed;
            EditModePanel.Visibility = Visibility.Visible;
            ChangeCoverButton.Visibility = Visibility.Visible;

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
            ReadOnlyPanel.Visibility = Visibility.Visible;
            EditModePanel.Visibility = Visibility.Collapsed;
            ChangeCoverButton.Visibility = Visibility.Collapsed;
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

                string normAlbumArtist = string.Empty;
                var splitAlbumArtists = Helpers.ArtistHelper.SplitArtists(EditAlbumArtistBox.Text.Trim());
                if (!string.IsNullOrWhiteSpace(EditAlbumArtistBox.Text))
                {
                    normAlbumArtist = string.Join("; ", splitAlbumArtists);
                }

                using (var tfile = TagLib.File.Create(filePath))
                {
                    bool isFlac = filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) || tfile is TagLib.Flac.File;

                    if (isFlac)
                    {
                        // Ensure no illegal ID3 tags on FLAC file
                        tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);

                        if (tfile.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment xiph)
                        {
                            xiph.SetField("ALBUM", EditAlbumBox.Text.Trim());
                            if (!string.IsNullOrWhiteSpace(normAlbumArtist))
                            {
                                xiph.SetField("ALBUMARTIST", normAlbumArtist);
                                xiph.SetField("ALBUM ARTIST", normAlbumArtist);
                            }
                        }
                    }

                    tfile.Tag.Title = EditTitleBox.Text.Trim();
                    var splitPerformers = Helpers.ArtistHelper.SplitArtists(EditArtistBox.Text.Trim());
                    tfile.Tag.Performers = splitPerformers.ToArray();
                    tfile.Tag.Album = EditAlbumBox.Text.Trim();

                    if (uint.TryParse(EditYearBox.Text.Trim(), out uint year))
                        tfile.Tag.Year = year;

                    if (uint.TryParse(EditTrackBox.Text.Trim(), out uint trk))
                        tfile.Tag.Track = trk;

                    Helpers.DiscMetadataHelper.SetDiscInTag(tfile, EditDiscBox.Text);
                    Helpers.ReleaseTypeHelper.SetReleaseTypeInTag(tfile, parsedReleaseType);

                    double parsedBpm = _selectedTrack.Bpm;
                    if (double.TryParse(EditBpmBox.Text.Trim(), out double userBpm) && userBpm > 0)
                        parsedBpm = Math.Round(userBpm);

                    string parsedCamelot = Helpers.HarmonicAnalysisHelper.MapKeyToCamelot(EditCamelotBox.Text.Trim());
                    if (string.IsNullOrEmpty(parsedCamelot))
                        parsedCamelot = _selectedTrack.CamelotKey;

                    Helpers.HarmonicAnalysisHelper.WriteHarmonicMetadataToTag(tfile, parsedBpm, parsedCamelot);

                    if (!string.IsNullOrWhiteSpace(EditGenreBox.Text))
                        tfile.Tag.Genres = new[] { EditGenreBox.Text.Trim() };

                    if (!string.IsNullOrWhiteSpace(normAlbumArtist))
                    {
                        tfile.Tag.AlbumArtists = splitAlbumArtists.ToArray();
                    }

                    if (!string.IsNullOrWhiteSpace(EditComposerBox.Text))
                        tfile.Tag.Composers = new[] { EditComposerBox.Text.Trim() };

                    if (!string.IsNullOrWhiteSpace(EditLyricsBox.Text))
                    {
                        tfile.Tag.Lyrics = EditLyricsBox.Text.Trim();
                    }

                    // Embed new picture if picked
                    if (!string.IsNullOrEmpty(_pendingNewCoverPath) && File.Exists(_pendingNewCoverPath))
                    {
                        var pic = new TagLib.Picture(_pendingNewCoverPath);
                        tfile.Tag.Pictures = new TagLib.IPicture[] { pic };

                        // Also save cached cover file for UI with unique timestamped hash
                        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
                        Directory.CreateDirectory(cacheFolder);
                        string safeKey = $"{EditArtistBox.Text.Trim().ToLowerInvariant()}_{EditAlbumBox.Text.Trim().ToLowerInvariant()}_{DateTime.Now.Ticks}";
                        using var md5 = System.Security.Cryptography.MD5.Create();
                        string hashFileName = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(safeKey))).ToLowerInvariant();
                        string ext = Path.GetExtension(_pendingNewCoverPath); if (string.IsNullOrEmpty(ext)) ext = ".jpg"; string cachedPath = Path.Combine(cacheFolder, $"{hashFileName}{ext}");
                        try
                        {
                            File.Copy(_pendingNewCoverPath, cachedPath, true);
                            _selectedTrack.CoverImagePath = cachedPath;
                        }
                        catch { }
                    }

                    if (isFlac)
                    {
                        tfile.RemoveTags(TagLib.TagTypes.Id3v1 | TagLib.TagTypes.Id3v2);
                    }

                    tfile.Save();
                }

                // Save lyrics to local cache and .lrc if provided
                if (!string.IsNullOrWhiteSpace(EditLyricsBox.Text))
                {
                    await LrcService.SaveLrcForTrackAsync(_selectedTrack, EditLyricsBox.Text.Trim());
                    _loadedLyrics = EditLyricsBox.Text.Trim();
                }

                // Update in-memory track object
                var finalPerformers = Helpers.ArtistHelper.SplitArtists(EditArtistBox.Text.Trim());
                _selectedTrack.Title = EditTitleBox.Text.Trim();
                _selectedTrack.Artist = string.Join("; ", finalPerformers);
                _selectedTrack.AlbumArtist = normAlbumArtist;
                _selectedTrack.Album = EditAlbumBox.Text.Trim();
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
                CancelEdit();
                LoadTrackDetails(_selectedTrack);
                FilterAndDisplayTracks();
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

            AlbumReadOnlyPanel.Visibility = Visibility.Collapsed;
            AlbumEditModePanel.Visibility = Visibility.Visible;
            ChangeCoverButton.Visibility = Visibility.Visible;

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
            ChangeCoverButton.Visibility = Visibility.Collapsed;
            AlbumEditModePanel.Visibility = Visibility.Collapsed;
            if (_isAlbumMode)
            {
                AlbumReadOnlyPanel.Visibility = Visibility.Visible;
                if (_selectedAlbum != null)
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

            var splitArtists = Helpers.ArtistHelper.SplitArtists(newArtist);
            string normalizedArtist = string.Join("; ", splitArtists);

            try
            {
                string? cachedNewCoverPath = null;
                if (!string.IsNullOrEmpty(pendingCover) && File.Exists(pendingCover))
                {
                    var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
                    Directory.CreateDirectory(cacheFolder);
                    string safeKey = $"{normalizedArtist.ToLowerInvariant()}_{newTitle.ToLowerInvariant()}_{DateTime.Now.Ticks}";
                    using var md5 = System.Security.Cryptography.MD5.Create();
                    string hashFileName = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(safeKey))).ToLowerInvariant();
                    string ext = Path.GetExtension(pendingCover);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                    cachedNewCoverPath = Path.Combine(cacheFolder, $"{hashFileName}{ext}");
                    File.Copy(pendingCover, cachedNewCoverPath, true);
                }

                await Task.Run(() =>
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
                    foreach (var track in tracksToUpdate)
                    {
                        vm.NotifyTrackMetadataChanged(track);
                    }
                }

                BuildAlbumItemsList();
                FilterAndDisplayAlbums();
                CancelAlbumEdit();
                LoadAlbumDetails(_selectedAlbum);

                ShowInfoDialog("¡Álbum Actualizado!",
                    $"Se han guardado e incrustado exitosamente los cambios (carátula, nombre, artista y tipo '{newType}') en las {tracksToUpdate.Count} canciones del álbum.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error al guardar álbum", $"Ocurrió un error al actualizar los archivos: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifica si existen datos en el objeto Track (Base de Datos) que aún no están escritos en las etiquetas físicas del archivo.
        /// </summary>
        private static bool HasDatabaseMetadataMissingInFile(Track track, TagLib.File tfile)
        {
            if (track == null || tfile?.Tag == null) return false;

            var (tagBpm, tagKey) = Helpers.HarmonicAnalysisHelper.ExtractHarmonicMetadataFromTag(tfile);
            string dbKey = Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

            // 1. BPM presente en BD pero ausente en el archivo
            if (track.Bpm > 0 && tagBpm <= 0)
                return true;

            // 2. Clave Camelot presente en BD pero ausente en el archivo
            if (!string.IsNullOrEmpty(dbKey) && string.IsNullOrEmpty(tagKey))
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
            double bpmToWrite = tagBpm > 0 ? tagBpm : track.Bpm;
            string keyToWrite = !string.IsNullOrEmpty(tagKey) ? tagKey : Helpers.CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

            if ((track.Bpm > 0 && tagBpm <= 0) || (!string.IsNullOrEmpty(keyToWrite) && string.IsNullOrEmpty(tagKey)))
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

        private async void EmbedMissingMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null || !File.Exists(_selectedTrack.FilePath)) return;

            var track = _selectedTrack;
            EmbedMissingMetadataButton.IsEnabled = false;

            try
            {
                bool embedded = await Task.Run(() => TryEmbedMissingMetadataIntoFile(track));
                if (embedded)
                {
                    var vm = MainViewModel.Instance;
                    if (vm != null)
                    {
                        await vm.DbService.InsertTracksAsync(_allTracks);
                        vm.NotifyTrackMetadataChanged(track);
                    }

                    EmbedMissingMetadataButton.Visibility = Visibility.Collapsed;
                    LoadTrackDetails(track);
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
            var snapshot = _allTracks.ToList();
            if (snapshot.Count == 0) return;

            EmbedAllMissingToolbarButton.IsEnabled = false;
            EmbedAllMissingToolbarText.Text = "Incrustando metadatos...";

            try
            {
                var (embeddedCount, failedCount) = await Task.Run(() =>
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
                }

                if (_selectedTrack != null)
                {
                    LoadTrackDetails(_selectedTrack);
                }

                await RefreshLibraryMissingMetadataStateAsync();

                string msg = failedCount > 0
                    ? $"Se incrustaron exitosamente los metadatos faltantes (BPM, Clave Camelot, etc.) en {embeddedCount} archivo(s). ({failedCount} archivo(s) omitidos por estar en reproducción o bloqueados)."
                    : $"Se incrustaron exitosamente los metadatos faltantes (BPM, Clave Camelot, etc.) en {embeddedCount} archivo(s) de audio.";

                ShowInfoDialog("¡Metadatos Faltantes Incrustados!", msg);
            }
            finally
            {
                EmbedAllMissingToolbarButton.IsEnabled = true;
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
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "Entendido",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
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
    }
}
