using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;
using KakiEQ_WinUI.Services.Audio;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class MiniPlayerWindow : Window
    {
        private readonly BassAudioEngine _audioEngine;
        private readonly DispatcherTimer _timer;
        private string? _currentFilePath;
        private bool _isRepeat = false;
        private bool _isShuffle = false;

        public MiniPlayerWindow(string? filePath = null)
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarBorder);

            ConfigureWindow();

            _audioEngine = MainViewModel.Instance?.AudioEngine ?? new BassAudioEngine();

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
                        await _audioEngine.PlayAsync();
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
                _timer.Stop();
                // If this is a standalone mini player without MainViewModel, dispose engine
                if (MainViewModel.Instance == null)
                {
                    _audioEngine.Dispose();
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

        private void ConfigureWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                appWindow.Resize(new SizeInt32(380, 540));
                
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
            _currentFilePath = filePath;

            string? title = null;
            string? artist = null;
            string dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;
            byte[]? pictureBytes = null;

            // Extract tags
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

            // User requirement:
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

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (_audioEngine.CurrentPosition.TotalSeconds > 3.0)
            {
                _audioEngine.SetPosition(TimeSpan.Zero);
                SeekSlider.Value = 0;
            }
            else if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.PreviousCommand.Execute(null);
                if (MainViewModel.Instance.CurrentTrack != null)
                {
                    _ = LoadAndPlayAudioFileAsync(MainViewModel.Instance.CurrentTrack.FilePath);
                }
            }
            else
            {
                _audioEngine.SetPosition(TimeSpan.Zero);
                SeekSlider.Value = 0;
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.Instance != null)
            {
                MainViewModel.Instance.NextCommand.Execute(null);
                if (MainViewModel.Instance.CurrentTrack != null)
                {
                    _ = LoadAndPlayAudioFileAsync(MainViewModel.Instance.CurrentTrack.FilePath);
                }
            }
            else
            {
                // Seek forward 10 seconds
                var newPos = _audioEngine.CurrentPosition + TimeSpan.FromSeconds(10);
                if (newPos < _audioEngine.TotalDuration)
                {
                    _audioEngine.SetPosition(newPos);
                    SeekSlider.Value = newPos.TotalSeconds;
                }
            }
        }

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _isShuffle = !_isShuffle;
            ShuffleIcon.Foreground = _isShuffle
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 176, 144, 208));
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            _isRepeat = !_isRepeat;
            RepeatIcon.Foreground = _isRepeat
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 176, 144, 208));
        }

        private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (Math.Abs(e.NewValue - _audioEngine.CurrentPosition.TotalSeconds) > 1.8)
            {
                _audioEngine.SetPosition(TimeSpan.FromSeconds(e.NewValue));
            }
        }
    }
}
