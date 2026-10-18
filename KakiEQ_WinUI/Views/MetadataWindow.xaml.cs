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
            this.Closed += (s, e) => StopLrcPlayback();
            _ = LoadTracksAsync(trackToSelect);
        }

        private void ConfigureWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                appWindow.Resize(new SizeInt32(1100, 740));
                
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Kaki_Oficial_Blanco.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }

                var titleBar = appWindow.TitleBar;
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
                _allTracks = await vm.DbService.GetAllTracksAsync();
                
                // Assign display indexes
                for (int i = 0; i < _allTracks.Count; i++)
                {
                    _allTracks[i].DisplayIndex = i + 1;
                }

                FilterAndDisplayTracks();

                if (initialTrack != null)
                {
                    var match = _allTracks.FirstOrDefault(t => t.FilePath.Equals(initialTrack.FilePath, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        TracksListView.SelectedItem = match;
                        TracksListView.ScrollIntoView(match);
                    }
                }
                else if (_allTracks.Count > 0)
                {
                    TracksListView.SelectedIndex = 0;
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndDisplayTracks();
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

            string ext = Path.GetExtension(track.FilePath).TrimStart('.').ToUpperInvariant();
            FormatText.Text = ext;

            // Load extended tags with TagLib
            try
            {
                if (File.Exists(track.FilePath))
                {
                    using var tfile = TagLib.File.Create(track.FilePath);
                    var tag = tfile.Tag;

                    DisplayYearText.Text = tag.Year > 0 ? tag.Year.ToString() : "-";
                    DisplayTrackNumberText.Text = tag.Track > 0 ? tag.Track.ToString() : "-";
                    DisplayGenreText.Text = string.IsNullOrWhiteSpace(tag.FirstGenre) ? "-" : tag.FirstGenre;
                    DisplayComposerText.Text = string.IsNullOrWhiteSpace(tag.FirstComposer) ? "-" : tag.FirstComposer;
                    DisplayAlbumArtistText.Text = string.IsNullOrWhiteSpace(tag.FirstAlbumArtist) ? "-" : tag.FirstAlbumArtist;
                    DisplayLyricsStatusText.Text = "Cargando letra...";

                    _ = Task.Run(async () =>
                    {
                        var lrc = await LrcService.LoadRawLrcForTrackAsync(track);
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            _loadedLyrics = lrc;
                            if (!string.IsNullOrWhiteSpace(lrc))
                            {
                                if (lrc.Contains("[") && lrc.Contains("]"))
                                    DisplayLyricsStatusText.Text = "Letra sincronizada (LRC)";
                                else
                                    DisplayLyricsStatusText.Text = "Letra disponible (Texto plano)";
                            }
                            else
                            {
                                DisplayLyricsStatusText.Text = "Sin letra sincronizada";
                            }
                        });
                    });

                    string tagTypes = tfile.TagTypes.ToString();
                    TagTypeText.Text = tagTypes.Replace(", ", " • ");

                    // Artwork display
                    if (tag.Pictures.Length > 0)
                    {
                        var pic = tag.Pictures[0];
                        using var ms = new MemoryStream(pic.Data.Data);
                        var bitmap = new BitmapImage();
                        _ = bitmap.SetSourceAsync(ms.AsRandomAccessStream());
                        CoverImageDisplay.Source = bitmap;
                    }
                    else if (!string.IsNullOrEmpty(track.CoverImagePath) && File.Exists(track.CoverImagePath))
                    {
                        CoverImageDisplay.Source = new BitmapImage(new Uri(track.CoverImagePath));
                    }
                    else
                    {
                        CoverImageDisplay.Source = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading tags: {ex.Message}");
                CoverImageDisplay.Source = null;
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
            EditDiscBox.Text = string.Empty;
            EditAlbumArtistBox.Text = DisplayAlbumArtistText.Text != "-" ? DisplayAlbumArtistText.Text : string.Empty;
            EditComposerBox.Text = DisplayComposerText.Text != "-" ? DisplayComposerText.Text : string.Empty;
            EditLyricsBox.Text = _loadedLyrics ?? string.Empty;

            try
            {
                if (File.Exists(_selectedTrack.FilePath))
                {
                    using var tfile = TagLib.File.Create(_selectedTrack.FilePath);
                    if (tfile.Tag.Disc > 0) EditDiscBox.Text = tfile.Tag.Disc.ToString();
                }
            }
            catch { }
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

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _pendingNewCoverPath = file.Path;
                CoverImageDisplay.Source = new BitmapImage(new Uri(file.Path));
            }
        }

        private async void SaveMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTrack == null || !File.Exists(_selectedTrack.FilePath)) return;

            try
            {
                string filePath = _selectedTrack.FilePath;

                using (var tfile = TagLib.File.Create(filePath))
                {
                    tfile.Tag.Title = EditTitleBox.Text.Trim();
                    tfile.Tag.Performers = new[] { EditArtistBox.Text.Trim() };
                    tfile.Tag.Album = EditAlbumBox.Text.Trim();

                    if (uint.TryParse(EditYearBox.Text.Trim(), out uint year))
                        tfile.Tag.Year = year;

                    if (uint.TryParse(EditTrackBox.Text.Trim(), out uint trk))
                        tfile.Tag.Track = trk;

                    if (uint.TryParse(EditDiscBox.Text.Trim(), out uint disc))
                        tfile.Tag.Disc = disc;

                    if (!string.IsNullOrWhiteSpace(EditGenreBox.Text))
                        tfile.Tag.Genres = new[] { EditGenreBox.Text.Trim() };

                    if (!string.IsNullOrWhiteSpace(EditAlbumArtistBox.Text))
                        tfile.Tag.AlbumArtists = new[] { EditAlbumArtistBox.Text.Trim() };

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

                        // Also save cached cover file for UI with unique hash
                        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
                        Directory.CreateDirectory(cacheFolder);
                        string safeKey = $"{EditArtistBox.Text.Trim().ToLowerInvariant()}_{EditAlbumBox.Text.Trim().ToLowerInvariant()}";
                        using var md5 = System.Security.Cryptography.MD5.Create();
                        string hashFileName = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(safeKey))).ToLowerInvariant();
                        string cachedPath = Path.Combine(cacheFolder, $"{hashFileName}.jpg");
                        try
                        {
                            File.Copy(_pendingNewCoverPath, cachedPath, true);
                            _selectedTrack.CoverImagePath = cachedPath;
                        }
                        catch { }
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
                _selectedTrack.Title = EditTitleBox.Text.Trim();
                _selectedTrack.Artist = EditArtistBox.Text.Trim();
                _selectedTrack.Album = EditAlbumBox.Text.Trim();
                if (int.TryParse(EditTrackBox.Text.Trim(), out int trkNum)) _selectedTrack.TrackNumber = trkNum;
                if (int.TryParse(EditYearBox.Text.Trim(), out int yrNum)) _selectedTrack.Year = yrNum;
                _selectedTrack.Genre = EditGenreBox.Text.Trim();

                // Save to database
                var vm = MainViewModel.Instance;
                if (vm != null)
                {
                    await vm.DbService.InsertTracksAsync(_allTracks);
                }

                CancelEdit();
                LoadTrackDetails(_selectedTrack);
                FilterAndDisplayTracks();

                ShowInfoDialog("¡Metadatos Guardados!", "Los metadatos y la portada han sido incrustados exitosamente dentro del archivo.");
            }
            catch (Exception ex)
            {
                ShowInfoDialog("Error al guardar", $"No se pudieron escribir los cambios en el archivo: {ex.Message}");
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
            }

            StopLrcPlayback();
            LrcGeneratorOverlay.Visibility = Visibility.Collapsed;
            ShowInfoDialog("LRC Guardado", "La letra sincronizada con marcas de tiempo se ha generado y guardado correctamente.");
        }
    }
}
