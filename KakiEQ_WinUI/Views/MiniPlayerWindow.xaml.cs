using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Audio;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class MiniPlayerWindow : Window
    {
        public static MiniPlayerWindow? CurrentInstance { get; private set; }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private readonly BassAudioEngine _audioEngine;
        private readonly DispatcherTimer _timer;

        private string? _currentFilePath;
        private string _currentDirectory = string.Empty;
        private string _folderName = string.Empty;
        private readonly List<string> _folderFiles = new();
        private int _currentFolderIndex = -1;

        private bool _isRepeat = false;
        private bool _isShuffle = false;

        public ObservableCollection<FolderTrackItem> FolderPlaylist { get; } = new();

        public MiniPlayerWindow(string? filePath = null)
        {
            CurrentInstance = this;
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarBorder);

            ConfigureWindow();

            _audioEngine = new BassAudioEngine();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _timer.Tick += (s, e) =>
            {
                if (_audioEngine.IsPlaying)
                {
                    double pos = _audioEngine.CurrentPosition.TotalSeconds;
                    SeekSlider.Value = pos;
                    CurrentTimeText.Text = _audioEngine.CurrentPosition.ToString(@"mm\:ss");
                }
            };

            _audioEngine.TrackEnded += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (_isRepeat && !string.IsNullOrEmpty(_currentFilePath))
                    {
                        _audioEngine.SetPosition(TimeSpan.Zero);
                        await _audioEngine.PlayAsync();
                        PlayPauseIcon.Glyph = "\uE769";
                        _timer.Start();
                    }
                    else if (_folderFiles.Count > 1)
                    {
                        // Advance to the next song in the folder
                        if (_isShuffle)
                        {
                            var rng = new Random();
                            int nextIdx;
                            do
                            {
                                nextIdx = rng.Next(_folderFiles.Count);
                            } while (nextIdx == _currentFolderIndex && _folderFiles.Count > 1);

                            await LoadAndPlayAudioFileAsync(_folderFiles[nextIdx]);
                        }
                        else
                        {
                            int nextIdx = (_currentFolderIndex + 1) % _folderFiles.Count;
                            await LoadAndPlayAudioFileAsync(_folderFiles[nextIdx]);
                        }
                    }
                    else
                    {
                        PlayPauseIcon.Glyph = "\uE768";
                        _timer.Stop();
                    }
                });
            };

            this.Closed += (s, e) =>
            {
                if (CurrentInstance == this)
                {
                    CurrentInstance = null;
                }

                _timer.Stop();
                _audioEngine.Dispose();

                // If standalone mini player launched from external file with no MainWindow
                if (MainWindow.Instance == null)
                {
                    Application.Current.Exit();
                }
            };

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                _ = LoadAndPlayAudioFileAsync(filePath);
            }
            else if (MainViewModel.Instance?.CurrentTrack != null)
            {
                _ = LoadAndPlayAudioFileAsync(MainViewModel.Instance.CurrentTrack.FilePath);
            }
        }

        public void BringToForeground()
        {
            Activate();
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
        }

        private void ConfigureWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                appWindow.Resize(new SizeInt32(380, 560));

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
                    titleBar.ButtonForegroundColor = Colors.White;
                }
            }
        }

        public async Task LoadAndPlayAudioFileAsync(string filePath)
        {
            _timer.Stop();
            await _audioEngine.StopAsync();


            _currentFilePath = filePath;
            string dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;

            // Check if folder needs scanning or updating
            if (!string.Equals(_currentDirectory, dirPath, StringComparison.OrdinalIgnoreCase) || _folderFiles.Count == 0)
            {
                ScanFolder(dirPath, filePath);
            }
            else
            {
                _currentFolderIndex = _folderFiles.FindIndex(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
                UpdateFolderUIInfo();
                UpdatePlaylistPlayingState();
            }

            // Extract tags
            string? title = null;
            string? artist = null;
            byte[]? pictureBytes = null;

            try
            {
                using var tfile = TagLib.File.Create(filePath);
                title = string.IsNullOrWhiteSpace(tfile.Tag.Title) ? null : tfile.Tag.Title;
                artist = string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer) ? null : tfile.Tag.FirstPerformer;

                if (tfile.Tag.Pictures.Length > 0)
                {
                    pictureBytes = tfile.Tag.Pictures[0].Data.Data;
                }
            }
            catch { }

            // Metadata UI logic per Image 3 requirement:
            // "En caso de que el archivo que se abra cuente con metadatos pues se autocompletara el llenado de Nombre de cancion y Nombre de Artista; además de agregar la ruta de donde se encuentra el archivo"
            // "En caso de que el archivo de audio no cuente con metadatos, se omite el nombre del artista, y en vez del nombre de la cancion seria el nombre del archivo. Y se mantiene lo de la ruta"
            if (!string.IsNullOrEmpty(artist) || !string.IsNullOrEmpty(title))
            {
                SongTitleText.Text = title ?? Path.GetFileNameWithoutExtension(filePath);
                if (!string.IsNullOrEmpty(artist))
                {
                    ArtistNameText.Text = artist;
                    ArtistNameText.Visibility = Visibility.Visible;
                }
                else
                {
                    ArtistNameText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                SongTitleText.Text = Path.GetFileName(filePath);
                ArtistNameText.Visibility = Visibility.Collapsed;
            }

            SongPathText.Text = dirPath;
            ToolTipService.SetToolTip(SongPathText, filePath);

            // Cover artwork display
            if (pictureBytes != null && pictureBytes.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(pictureBytes);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(ms.AsRandomAccessStream());
                    ArtworkImage.Source = bitmap;
                    ArtworkBorder.Visibility = Visibility.Visible;
                    NoteIllustrationContainer.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    ArtworkBorder.Visibility = Visibility.Collapsed;
                    NoteIllustrationContainer.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ArtworkBorder.Visibility = Visibility.Collapsed;
                NoteIllustrationContainer.Visibility = Visibility.Visible;
            }

            // Load and Play Audio
            await _audioEngine.LoadFileAsync(filePath);
            await _audioEngine.PlayAsync();

            SeekSlider.Maximum = _audioEngine.TotalDuration.TotalSeconds;
            TotalTimeText.Text = _audioEngine.TotalDuration.ToString(@"mm\:ss");
            CurrentTimeText.Text = "00:00";
            PlayPauseIcon.Glyph = "\uE769"; // Pause
            _timer.Start();
        }

        private void ScanFolder(string folderPath, string currentFilePath)
        {
            _currentDirectory = folderPath;
            _folderFiles.Clear();
            FolderPlaylist.Clear();

            try
            {
                if (Directory.Exists(folderPath))
                {
                    var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma"
                    };

                    var files = Directory.EnumerateFiles(folderPath)
                        .Where(f => exts.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                    _folderFiles.AddRange(files);
                    _folderName = Path.GetFileName(folderPath);
                    if (string.IsNullOrEmpty(_folderName))
                    {
                        _folderName = folderPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MiniPlayer] ScanFolder error: {ex.Message}");
            }

            if (_folderFiles.Count == 0 && File.Exists(currentFilePath))
            {
                _folderFiles.Add(currentFilePath);
                _folderName = Path.GetFileName(folderPath);
            }

            _currentFolderIndex = _folderFiles.FindIndex(f => string.Equals(f, currentFilePath, StringComparison.OrdinalIgnoreCase));
            if (_currentFolderIndex < 0 && _folderFiles.Count > 0)
            {
                _currentFolderIndex = 0;
            }

            // Populate initial folder track items
            for (int i = 0; i < _folderFiles.Count; i++)
            {
                string f = _folderFiles[i];
                string nameWithoutExt = Path.GetFileNameWithoutExtension(f);
                string ext = Path.GetExtension(f).ToUpperInvariant().TrimStart('.');
                FolderPlaylist.Add(new FolderTrackItem
                {
                    Index = i + 1,
                    FilePath = f,
                    DisplayTitle = nameWithoutExt,
                    DisplaySubTitle = ext,
                    IsPlaying = (i == _currentFolderIndex)
                });
            }

            UpdateFolderUIInfo();

            // Enrich playlist titles asynchronously without freezing UI
            _ = Task.Run(() =>
            {
                for (int i = 0; i < FolderPlaylist.Count; i++)
                {
                    try
                    {
                        var item = FolderPlaylist[i];
                        if (string.Equals(item.FilePath, currentFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        using var tf = TagLib.File.Create(item.FilePath);
                        string? tagTitle = tf.Tag.Title;
                        string? tagArtist = tf.Tag.FirstPerformer;
                        if (!string.IsNullOrWhiteSpace(tagTitle))
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                item.DisplayTitle = tagTitle;
                                if (!string.IsNullOrWhiteSpace(tagArtist))
                                {
                                    item.DisplaySubTitle = tagArtist;
                                }
                            });
                        }
                    }
                    catch { }
                }
            });
        }

        private void UpdateFolderUIInfo()
        {
            if (_folderFiles.Count > 0)
            {
                int currentDisplay = _currentFolderIndex >= 0 ? _currentFolderIndex + 1 : 1;
                FolderInfoText.Text = $"Carpeta: {_folderName} ({currentDisplay} de {_folderFiles.Count})";
                FolderListHeader.Text = $"Canciones en {_folderName} ({_folderFiles.Count})";
                ToolTipService.SetToolTip(FolderBadge, _currentDirectory);
            }
            else
            {
                FolderInfoText.Text = "Carpeta (1 pista)";
                FolderListHeader.Text = "Canciones de la carpeta";
            }
        }

        private void UpdatePlaylistPlayingState()
        {
            for (int i = 0; i < FolderPlaylist.Count; i++)
            {
                FolderPlaylist[i].IsPlaying = (i == _currentFolderIndex);
            }

            if (_currentFolderIndex >= 0 && _currentFolderIndex < FolderPlaylist.Count)
            {
                FolderTracksListView.ScrollIntoView(FolderPlaylist[_currentFolderIndex]);
            }
        }

        private void ToggleFolderList_Click(object sender, RoutedEventArgs e)
        {
            if (FolderListContainer.Visibility == Visibility.Visible)
            {
                FolderListContainer.Visibility = Visibility.Collapsed;
                ArtworkContainer.Visibility = Visibility.Visible;
                ToggleListText.Text = "Lista";
                ToggleListIcon.Glyph = "\uE8FD";
            }
            else
            {
                FolderListContainer.Visibility = Visibility.Visible;
                ArtworkContainer.Visibility = Visibility.Collapsed;
                ToggleListText.Text = "Carátula";
                ToggleListIcon.Glyph = "\uEB9F";
                UpdatePlaylistPlayingState();
            }
        }

        private async void FolderTracksListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FolderTrackItem item)
            {
                await LoadAndPlayAudioFileAsync(item.FilePath);
            }
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_audioEngine.IsPlaying)
            {
                await _audioEngine.PauseAsync();
                PlayPauseIcon.Glyph = "\uE768"; // Play
                _timer.Stop();
            }
            else
            {
                await _audioEngine.PlayAsync();
                PlayPauseIcon.Glyph = "\uE769"; // Pause
                _timer.Start();
            }
        }

        private async void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (_audioEngine.CurrentPosition.TotalSeconds > 3.0)
            {
                _audioEngine.SetPosition(TimeSpan.Zero);
                SeekSlider.Value = 0;
                return;
            }

            if (_folderFiles.Count <= 1)
            {
                _audioEngine.SetPosition(TimeSpan.Zero);
                SeekSlider.Value = 0;
                return;
            }

            if (_isShuffle)
            {
                var rng = new Random();
                int prevIdx;
                do
                {
                    prevIdx = rng.Next(_folderFiles.Count);
                } while (prevIdx == _currentFolderIndex && _folderFiles.Count > 1);

                await LoadAndPlayAudioFileAsync(_folderFiles[prevIdx]);
            }
            else
            {
                int prevIdx = (_currentFolderIndex - 1 + _folderFiles.Count) % _folderFiles.Count;
                await LoadAndPlayAudioFileAsync(_folderFiles[prevIdx]);
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_folderFiles.Count <= 1)
            {
                _audioEngine.SetPosition(TimeSpan.Zero);
                SeekSlider.Value = 0;
                return;
            }

            if (_isShuffle)
            {
                var rng = new Random();
                int nextIdx;
                do
                {
                    nextIdx = rng.Next(_folderFiles.Count);
                } while (nextIdx == _currentFolderIndex && _folderFiles.Count > 1);

                await LoadAndPlayAudioFileAsync(_folderFiles[nextIdx]);
            }
            else
            {
                int nextIdx = (_currentFolderIndex + 1) % _folderFiles.Count;
                await LoadAndPlayAudioFileAsync(_folderFiles[nextIdx]);
            }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _isShuffle = !_isShuffle;
            ShuffleIcon.Foreground = _isShuffle
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 176, 144, 208));
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            _isRepeat = !_isRepeat;
            RepeatIcon.Foreground = _isRepeat
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 176, 144, 208));
        }

        private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (Math.Abs(e.NewValue - _audioEngine.CurrentPosition.TotalSeconds) > 1.8)
            {
                _audioEngine.SetPosition(TimeSpan.FromSeconds(e.NewValue));
            }
        }

        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
            }
        }

        private async void RootGrid_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is Windows.Storage.StorageFile file)
                    {
                        string ext = Path.GetExtension(file.Path).ToLowerInvariant();
                        if (ext is ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".opus" or ".wma")
                        {
                            await LoadAndPlayAudioFileAsync(file.Path);
                            break;
                        }
                    }
                }
            }
        }
    }
}
