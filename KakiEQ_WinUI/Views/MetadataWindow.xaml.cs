using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class MetadataWindow : Window
    {
        private List<Track> _allTracks = new();
        private Track? _selectedTrack;
        private string? _pendingNewCoverPath;

        public MetadataWindow(Track? trackToSelect = null)
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarBorder);

            ConfigureWindow();
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
                    DisplayCommentText.Text = string.IsNullOrWhiteSpace(tag.Comment) ? "-" : tag.Comment;

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
            EditCommentBox.Text = DisplayCommentText.Text != "-" ? DisplayCommentText.Text : string.Empty;

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

                    tfile.Tag.Comment = EditCommentBox.Text.Trim();

                    // Embed new picture if picked
                    if (!string.IsNullOrEmpty(_pendingNewCoverPath) && File.Exists(_pendingNewCoverPath))
                    {
                        var pic = new TagLib.Picture(_pendingNewCoverPath);
                        tfile.Tag.Pictures = new TagLib.IPicture[] { pic };

                        // Also save cached cover file for UI
                        var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
                        Directory.CreateDirectory(cacheFolder);
                        string safeAlbum = string.Join("_", (tfile.Tag.Album ?? "Album").Split(Path.GetInvalidFileNameChars()));
                        string cachedPath = Path.Combine(cacheFolder, $"{safeAlbum}.jpg");
                        try
                        {
                            File.Copy(_pendingNewCoverPath, cachedPath, true);
                            _selectedTrack.CoverImagePath = cachedPath;
                        }
                        catch { }
                    }

                    tfile.Save();
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
    }
}
