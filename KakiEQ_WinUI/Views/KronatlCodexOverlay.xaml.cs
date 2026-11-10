using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.ViewModels;

namespace KakiEQ_WinUI.Views
{
    public sealed partial class KronatlCodexOverlay : UserControl
    {
        private KronatlSeason? _season;
        private float _previousVolume = 1.0f;
        private bool _wasPlayingBeforeCodex = false;

        // Auto-advancing story timer (Instagram / YouTube Music Recap style)
        private readonly DispatcherTimer _storyTimer;
        private const double SLIDE_DURATION_SECONDS = 7.0;
        private double _elapsedInSlide = 0;
        private DateTime _pointerDownTime;
        private bool _isPointerDown = false;

        public KronatlCodexOverlay()
        {
            InitializeComponent();
            KeyDown += KronatlCodexOverlay_KeyDown;

            _storyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS fluid fill
            };
            _storyTimer.Tick += StoryTimer_Tick;
        }

        private void KronatlCodexOverlay_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Right || e.Key == VirtualKey.Space)
            {
                AdvanceNext();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Left)
            {
                AdvancePrev();
                e.Handled = true;
            }
        }

        public void Open(KronatlSeason season)
        {
            if (season == null) return;
            _season = season;

            PopulateCodexData(season);

            if (CodexFlipView != null) CodexFlipView.SelectedIndex = 0;
            if (PageIndicatorText != null) PageIndicatorText.Text = "1 de 8";

            _elapsedInSlide = 0;
            UpdateStoryBars(0);
            _storyTimer.Start();

            Visibility = Visibility.Visible;
            Focus(FocusState.Programmatic);

            // Ambient background playback at subdued volume (~20%)
            StartAmbientPlayback(season);
        }

        public void Close()
        {
            _storyTimer.Stop();
            StopAmbientPlayback();
            Visibility = Visibility.Collapsed;
        }

        private void StoryTimer_Tick(object? sender, object e)
        {
            if (CodexFlipView == null || _season == null) return;
            int index = CodexFlipView.SelectedIndex;
            if (index < 0 || index >= 8) return;

            _elapsedInSlide += 0.033;
            double progress = Math.Clamp(_elapsedInSlide / SLIDE_DURATION_SECONDS, 0.0, 1.0);

            var fills = new[] { StoryFill0, StoryFill1, StoryFill2, StoryFill3, StoryFill4, StoryFill5, StoryFill6, StoryFill7 };
            var tracks = new[] { StoryTrack0, StoryTrack1, StoryTrack2, StoryTrack3, StoryTrack4, StoryTrack5, StoryTrack6, StoryTrack7 };

            if (index < fills.Length && fills[index] != null && tracks[index] != null)
            {
                double trackWidth = tracks[index].ActualWidth > 0 ? tracks[index].ActualWidth : 120.0;
                fills[index].Width = progress * trackWidth;
            }

            if (_elapsedInSlide >= SLIDE_DURATION_SECONDS)
            {
                if (index < 7)
                {
                    AdvanceNext();
                }
                else
                {
                    _storyTimer.Stop();
                }
            }
        }

        private void CodexFlipView_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isPointerDown = true;
            _pointerDownTime = DateTime.Now;
            _storyTimer.Stop();
        }

        private void CodexFlipView_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isPointerDown) return;
            _isPointerDown = false;

            var durationMs = (DateTime.Now - _pointerDownTime).TotalMilliseconds;
            if (durationMs < 300)
            {
                var pt = e.GetCurrentPoint(CodexFlipView).Position;
                if (CodexFlipView != null && pt.X < CodexFlipView.ActualWidth * 0.25)
                {
                    AdvancePrev();
                }
                else
                {
                    AdvanceNext();
                }
            }
            else
            {
                _storyTimer.Start();
            }
        }

        private void PopulateCodexData(KronatlSeason season)
        {
            var stats = season.Stats;

            // Header & Ambient glow
            SeasonHeaderTitle.Text = season.Title;
            SeasonSubtitlePill.Text = season.Subtitle;

            var accentColor = Windows.UI.Color.FromArgb(255, 255, 110, 26); // Default Autumn orange
            var secondaryColor = Windows.UI.Color.FromArgb(255, 156, 39, 176); // Violet
            var tertiaryColor = Windows.UI.Color.FromArgb(255, 0, 230, 118); // Green
            try
            {
                if (!string.IsNullOrEmpty(season.AccentColor))
                {
                    string hex = season.AccentColor.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                        accentColor = Windows.UI.Color.FromArgb(255, r, g, b);
                    }
                }
                if (!string.IsNullOrEmpty(season.SecondaryAccentColor))
                {
                    string hex = season.SecondaryAccentColor.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                        secondaryColor = Windows.UI.Color.FromArgb(255, r, g, b);
                    }
                }
                if (!string.IsNullOrEmpty(season.TertiaryAccentColor))
                {
                    string hex = season.TertiaryAccentColor.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                        tertiaryColor = Windows.UI.Color.FromArgb(255, r, g, b);
                    }
                }
            }
            catch { }

            AmbientGlowStop1.Color = accentColor;
            AmbientGlowStop2.Color = secondaryColor;
            AmbientGlowStop3.Color = tertiaryColor;
            AmbientGlowCenter.Color = secondaryColor;

            ApplySeasonalBackgroundGradient(season, 0);

            if (!string.IsNullOrEmpty(season.ParticlesImagePath))
            {
                try
                {
                    var partUri = new Uri(season.ParticlesImagePath);
                    CodexParticlesTop.Source = new BitmapImage(partUri);
                    CodexParticlesBottom.Source = new BitmapImage(partUri);
                }
                catch { }
            }

            HeaderBadgeBorder.BorderBrush = new SolidColorBrush(accentColor);
            HeaderBadgeText.Text = season.SeasonalBadgeText;
            HeaderBadgeText.Foreground = new SolidColorBrush(accentColor);

            if (!string.IsNullOrEmpty(season.EmblemImagePath))
            {
                try
                {
                    var embUri = new Uri(season.EmblemImagePath);
                    HeaderEmblemImage.Source = new BitmapImage(embUri);
                    Card0EmblemImage.Source = new BitmapImage(embUri);
                    SummaryEmblemImage.Source = new BitmapImage(embUri);
                }
                catch { }
            }

            // Slide 0: Portada
            Card0BadgeBorder.Background = new SolidColorBrush(accentColor);
            Card0Badge.Text = season.SeasonalBadgeText;
            Card0LoreBadge.Text = season.LoreBadgeText;
            Card0Ring.Stroke = new SolidColorBrush(accentColor);
            Card0Title.Text = season.Title.ToUpperInvariant();
            Card0Subtitle.Text = season.Subtitle;
            Card0Lore.Text = $"«{season.SubtitleQuote}»";
            Card0StatTime.Text = $"⏱ {stats.TotalListeningFormatted}";
            Card0StatTrack.Text = $"★ Pista del Alma: {stats.TopTrackTitle}";
            Card0StatArtist.Text = $"👤 Artista Refugio: {stats.TopArtist}";

            // Slide 1: Tiempo Total
            Card1Hours.Text = stats.TotalListeningFormatted;
            Card1Hours.Foreground = new SolidColorBrush(accentColor);

            // Slide 2: Pista del Alma
            Card2TrackTitle.Text = stats.TopTrackTitle;
            Card2Artist.Text = stats.TopTrackArtist;
            Card2PlayCount.Text = $"{stats.TopTrackPlayCount} reproducciones en bucle";
            Card2Camelot.Text = $"Clave {stats.DominantCamelotKey}";
            Card2PeakTime.Text = $"Pico de euforia: {stats.TopTrackPeakTime}";

            if (!string.IsNullOrEmpty(stats.TopTrackCoverPath) && File.Exists(stats.TopTrackCoverPath))
            {
                try
                {
                    Card2Cover.Source = new BitmapImage(new Uri(stats.TopTrackCoverPath));
                    Card2FallbackIcon.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    Card2Cover.Source = null;
                    Card2FallbackIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                Card2Cover.Source = null;
                Card2FallbackIcon.Visibility = Visibility.Visible;
            }

            // Slide 3: Artista Refugio & Top 5 Artistas
            Card3ArtistName.Text = stats.TopArtist;
            Card3Hours.Text = $"{stats.TopArtistTimeFormatted} escuchadas en total";
            Card3Hours.Foreground = new SolidColorBrush(accentColor);

            if (!string.IsNullOrEmpty(stats.TopArtistPhotoPath) && File.Exists(stats.TopArtistPhotoPath))
            {
                try
                {
                    Card3ArtistPhoto.Source = new BitmapImage(new Uri(stats.TopArtistPhotoPath));
                    Card3FallbackIcon.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    Card3ArtistPhoto.Source = null;
                    Card3FallbackIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                Card3ArtistPhoto.Source = null;
                Card3FallbackIcon.Visibility = Visibility.Visible;
            }

            Top5ArtistsList.ItemsSource = stats.Top5Artists;
            SummaryTop5ArtistsList.ItemsSource = stats.Top5Artists;

            // Slide 4: Espectro Armónico, BPM & Top 5 Canciones
            Card4CamelotText.Text = stats.CamelotSpectrumFormatted;
            var camelotColor = CamelotColorConverter.GetColor(stats.DominantCamelotKey);
            Card4CamelotPill.Background = new SolidColorBrush(camelotColor);
            double lum = (camelotColor.R * 0.299 + camelotColor.G * 0.587 + camelotColor.B * 0.114);
            Card4CamelotText.Foreground = new SolidColorBrush(lum > 135 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);

            Card4BpmText.Text = $"{stats.AverageBpm} BPM";
            Card4BpmRhythm.Text = stats.BpmRhythmDescription;

            Top5TracksList.ItemsSource = stats.Top5Tracks;
            SummaryTop5TracksList.ItemsSource = stats.Top5Tracks;

            // Slide 5: Pico de Obsidiana & Álbum Puro
            Card5PeakDate.Text = stats.ObsidianPeakDateFormatted;
            Card5PeakHours.Text = $"{stats.ObsidianPeakHoursFormatted} continuos de música";

            Card6AlbumTitle.Text = stats.PureAlbumTitle;
            Card6AlbumArtist.Text = stats.PureAlbumArtist;
            if (!string.IsNullOrEmpty(stats.PureAlbumCoverPath) && File.Exists(stats.PureAlbumCoverPath))
            {
                try
                {
                    Card6AlbumCover.Source = new BitmapImage(new Uri(stats.PureAlbumCoverPath));
                }
                catch { Card6AlbumCover.Source = null; }
            }
            else
            {
                Card6AlbumCover.Source = null;
            }

            // Slide 6: Fidelidad Audiófila
            Card6FidelityPercent.Text = $"{stats.LosslessPercentage}% Hi-Res Audio";
            Card6FidelityBar.Width = Math.Clamp(540.0 * (stats.LosslessPercentage / 100.0), 40.0, 540.0);

            // Slide 7: Resumen Final
            Card7Headline.Text = $"{season.Title} · Recap Final";
            Card7Badge.Text = season.SeasonalBadgeText;
            SummaryTimeText.Text = stats.TotalListeningFormatted;
            SummaryTimeText.Foreground = new SolidColorBrush(accentColor);

            if (season.IsPlaylistSavedInLibrary)
            {
                SavePlaylistButton.Content = "✓ Guardada en Biblioteca";
                SavePlaylistButton.IsEnabled = false;
            }
            else
            {
                SavePlaylistButton.Content = "💾 Guardar Top 30 en Biblioteca";
                SavePlaylistButton.IsEnabled = true;
            }
        }

        private void UpdateStoryBars(int index)
        {
            var fills = new[] { StoryFill0, StoryFill1, StoryFill2, StoryFill3, StoryFill4, StoryFill5, StoryFill6, StoryFill7 };
            var tracks = new[] { StoryTrack0, StoryTrack1, StoryTrack2, StoryTrack3, StoryTrack4, StoryTrack5, StoryTrack6, StoryTrack7 };

            var activeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 110, 26)); // Autumn orange accent
            if (_season != null)
            {
                try
                {
                    string hex = _season.AccentColor.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                        activeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
                    }
                }
                catch { }
            }

            var completedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 208, 188, 255));

            _elapsedInSlide = 0;

            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i] == null || tracks[i] == null) continue;

                if (i < index)
                {
                    // Previous bars are 100% full
                    double trackW = tracks[i].ActualWidth > 0 ? tracks[i].ActualWidth : 120.0;
                    fills[i].Width = trackW;
                    fills[i].Background = completedBrush;
                }
                else if (i == index)
                {
                    // Active bar starts at 0%
                    fills[i].Width = 0;
                    fills[i].Background = activeBrush;
                }
                else
                {
                    // Subsequent bars are 0%
                    fills[i].Width = 0;
                }
            }

            if (index < 7)
            {
                _storyTimer.Start();
            }
        }

        private void StartAmbientPlayback(KronatlSeason season)
        {
            var mainVm = MainViewModel.Instance;
            if (mainVm == null || season.TopTracks == null || season.TopTracks.Count == 0) return;

            _wasPlayingBeforeCodex = mainVm.IsPlaying;
            _previousVolume = (float)(mainVm.Volume / 100.0);

            // Subdued low-volume playback (~20%) for atmospheric recap soundtrack
            mainVm.Volume = 20.0;

            if (!_wasPlayingBeforeCodex)
            {
                var ambientTrack = season.TopTracks[0];
                _ = mainVm.PlayTrackAsync(ambientTrack);
            }
        }

        private void StopAmbientPlayback()
        {
            var mainVm = MainViewModel.Instance;
            if (mainVm == null) return;

            // Restore previous volume
            mainVm.Volume = _previousVolume * 100.0;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PrevCard_Click(object sender, RoutedEventArgs e)
        {
            AdvancePrev();
        }

        private void NextCard_Click(object sender, RoutedEventArgs e)
        {
            AdvanceNext();
        }

        private void AdvanceNext()
        {
            if (CodexFlipView != null && CodexFlipView.SelectedIndex < 7)
            {
                CodexFlipView.SelectedIndex++;
            }
            else
            {
                Close();
            }
        }

        private void AdvancePrev()
        {
            if (CodexFlipView != null && CodexFlipView.SelectedIndex > 0)
            {
                CodexFlipView.SelectedIndex--;
            }
        }

        private void ApplySeasonalBackgroundGradient(KronatlSeason season, int slideIndex)
        {
            // Highly saturated, vibrant seasonal palettes inspired by Spotify Wrapped & YouTube Music Recap
            Windows.UI.Color c0, c1, c2, c3, centerCol;
            switch (season.Type)
            {
                case KronatlSeasonType.Invierno:
                    c0 = Windows.UI.Color.FromArgb(255, 2, 119, 189);   // Glacial Azure Blue
                    c1 = Windows.UI.Color.FromArgb(255, 74, 20, 140);   // Deep Arctic Violet
                    c2 = Windows.UI.Color.FromArgb(255, 0, 137, 123);   // Boreal Cyan
                    c3 = Windows.UI.Color.FromArgb(255, 26, 35, 126);   // Midnight Indigo
                    centerCol = Windows.UI.Color.FromArgb(255, 3, 169, 244);
                    break;
                case KronatlSeasonType.Primavera:
                    c0 = Windows.UI.Color.FromArgb(255, 216, 27, 96);   // Cherry Blossom Pink
                    c1 = Windows.UI.Color.FromArgb(255, 46, 125, 50);   // Fresh Spring Emerald
                    c2 = Windows.UI.Color.FromArgb(255, 123, 31, 162);  // Lilac Violet
                    c3 = Windows.UI.Color.FromArgb(255, 194, 24, 91);   // Rose Wine
                    centerCol = Windows.UI.Color.FromArgb(255, 255, 64, 129);
                    break;
                case KronatlSeasonType.Verano:
                    c0 = Windows.UI.Color.FromArgb(255, 239, 108, 0);   // Warm Sunburst Orange
                    c1 = Windows.UI.Color.FromArgb(255, 106, 27, 154);  // Twilight Violet
                    c2 = Windows.UI.Color.FromArgb(255, 21, 101, 192);  // Ocean Deep Blue
                    c3 = Windows.UI.Color.FromArgb(255, 245, 127, 23);  // Solar Amber
                    centerCol = Windows.UI.Color.FromArgb(255, 255, 112, 67);
                    break;
                case KronatlSeasonType.Otoño:
                    c0 = Windows.UI.Color.FromArgb(255, 216, 75, 16);   // Radiant Cempasúchil Orange
                    c1 = Windows.UI.Color.FromArgb(255, 106, 27, 154);  // Mictlán Royal Violet
                    c2 = Windows.UI.Color.FromArgb(255, 14, 120, 78);   // Jade / Agave Green
                    c3 = Windows.UI.Color.FromArgb(255, 142, 36, 170);  // Deep Copal Purple
                    centerCol = Windows.UI.Color.FromArgb(255, 255, 110, 26);
                    break;
                default: // Anual
                    c0 = Windows.UI.Color.FromArgb(255, 212, 146, 24);  // Imperial Gold
                    c1 = Windows.UI.Color.FromArgb(255, 106, 27, 154);  // Royal Kaki Purple
                    c2 = Windows.UI.Color.FromArgb(255, 0, 137, 123);   // Emerald Jade
                    c3 = Windows.UI.Color.FromArgb(255, 191, 54, 12);   // Bonfire Copper
                    centerCol = Windows.UI.Color.FromArgb(255, 229, 169, 60);
                    break;
            }

            // Rotate color stops per slide so each slide transition feels dynamic and vibrant
            var stops = new[] { c0, c1, c2, c3 };
            int shift = Math.Abs(slideIndex) % 4;
            BgGradientStop0.Color = stops[shift];
            BgGradientStop1.Color = stops[(shift + 1) % 4];
            BgGradientStop2.Color = stops[(shift + 2) % 4];
            BgGradientStop3.Color = stops[(shift + 3) % 4];
            if (AmbientGlowCenter != null)
            {
                AmbientGlowCenter.Color = centerCol;
            }
        }

        private void CodexFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CodexFlipView == null || PageIndicatorText == null) return;
            int current = CodexFlipView.SelectedIndex + 1;
            PageIndicatorText.Text = $"{current} de 8";
            UpdateStoryBars(CodexFlipView.SelectedIndex);
            if (_season != null)
            {
                ApplySeasonalBackgroundGradient(_season, CodexFlipView.SelectedIndex);
            }
        }

        private async void ExportPoster_Click(object sender, RoutedEventArgs e)
        {
            if (_season == null) return;

            MainWindow.Instance?.ShowToast("Generando póster horizontal de arte en alta resolución (1920x1080)...");

            var exportedPath = await KronatlPosterExporter.ExportPosterAsync(_season);
            if (!string.IsNullOrEmpty(exportedPath) && File.Exists(exportedPath))
            {
                MainWindow.Instance?.ShowToast("¡Póster horizontal exportado con éxito a Imágenes\\KakiEQ\\Kronatl!");
                try
                {
                    // Open in default image viewer
                    await Launcher.LaunchUriAsync(new Uri(exportedPath));
                }
                catch { }
            }
            else
            {
                MainWindow.Instance?.ShowToast("No se pudo exportar el póster. Intenta de nuevo.");
            }
        }

        private async void SavePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (_season == null) return;

            var kronatlService = MainViewModel.Instance?.KronatlService;
            var dbService = MainViewModel.Instance?.DbService;
            if (kronatlService == null || dbService == null) return;

            var allTracks = await dbService.GetAllTracksAsync();
            await kronatlService.SaveTop30PlaylistToLibraryAsync(_season, allTracks);
            if (MainViewModel.Instance != null)
            {
                await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
            }

            SavePlaylistButton.Content = "✓ Guardada en Biblioteca";
            SavePlaylistButton.IsEnabled = false;
            MainWindow.Instance?.ShowToast($"¡Playlist \"{_season.PlaylistName}\" guardada en tu biblioteca con éxito!");
        }

        private async void PlayTop30_Click(object sender, RoutedEventArgs e)
        {
            if (_season == null || _season.TopTracks == null || _season.TopTracks.Count == 0) return;

            var mainVm = MainViewModel.Instance;
            if (mainVm != null)
            {
                mainVm.Volume = 85.0; // Normal volume
                mainVm.CurrentQueue = new ObservableCollection<Track>(_season.TopTracks);
                mainVm.CurrentQueueIndex = 0;
                await mainVm.PlayTrackAsync(_season.TopTracks[0]);
            }

            Close();
        }
    }
}
