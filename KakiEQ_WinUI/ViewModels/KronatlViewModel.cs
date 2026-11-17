using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class KronatlViewModel : ViewModelBase
    {
        private readonly KronatlService _kronatlService;
        private readonly DatabaseService _databaseService;

        public ObservableCollection<KronatlSeason> Seasons { get; } = new();

        private KronatlSeason? _selectedSeason;
        public KronatlSeason? SelectedSeason
        {
            get => _selectedSeason;
            set
            {
                if (_selectedSeason != value)
                {
                    _selectedSeason = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _activeBannerTitle = "Temporada en Curso: Otoño 2026";
        public string ActiveBannerTitle
        {
            get => _activeBannerTitle;
            set { if (_activeBannerTitle != value) { _activeBannerTitle = value; OnPropertyChanged(); } }
        }

        private string _activeBannerDescription = "Estadísticas locales en tiempo real sincronizadas con tu biblioteca y tu historial de reproducción.";
        public string ActiveBannerDescription
        {
            get => _activeBannerDescription;
            set { if (_activeBannerDescription != value) { _activeBannerDescription = value; OnPropertyChanged(); } }
        }

        public ICommand OpenCodexCommand { get; }
        public ICommand PlaySeasonTop30Command { get; }
        public ICommand ViewPlaylistCommand { get; }

        public KronatlViewModel(KronatlService kronatlService, DatabaseService databaseService)
        {
            _kronatlService = kronatlService ?? throw new ArgumentNullException(nameof(kronatlService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));

            OpenCodexCommand = new RelayCommand<KronatlSeason>(season =>
            {
                if (season == null) return;
                MainWindow.Instance?.OpenKronatlCodex(season);
            });

            PlaySeasonTop30Command = new RelayCommand<KronatlSeason>(async season =>
            {
                if (season == null || season.TopTracks == null || season.TopTracks.Count == 0) return;
                var mainVm = MainViewModel.Instance;
                if (mainVm != null)
                {
                    mainVm.CurrentQueue = new ObservableCollection<Track>(season.TopTracks);
                    mainVm.CurrentQueueIndex = 0;
                    await mainVm.PlayTrackAsync(season.TopTracks[0]);
                }
            });

            ViewPlaylistCommand = new RelayCommand<KronatlSeason>(async season =>
            {
                if (season == null) return;
                var playlists = await _databaseService.GetAllPlaylistsAsync();
                var match = playlists.FirstOrDefault(p => p.Name.Equals(season.PlaylistName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    MainViewModel.Instance?.NavigateToPlaylist(match);
                }
            });

            _ = LoadSeasonsAsync();
        }

        public async Task LoadSeasonsAsync(bool silent = false)
        {
            if (!silent || Seasons.Count == 0)
            {
                IsLoading = true;
            }

            try
            {
                var libraryTracks = await _databaseService.GetAllTracksAsync();
                await _kronatlService.EnsureHistorySeededAsync(libraryTracks);

                var seasonList = await _kronatlService.GetAllSeasonsAsync(libraryTracks);
                var now = DateTime.Now;
                var currentSeasonal = seasonList.FirstOrDefault(s => s.Type != KronatlSeasonType.Anual && now >= s.PeriodStart && now <= s.PeriodEnd)
                                   ?? seasonList.FirstOrDefault(s => s.Type != KronatlSeasonType.Anual)
                                   ?? seasonList.FirstOrDefault();

                RunOnUIThread(() =>
                {
                    if (currentSeasonal != null)
                    {
                        ActiveBannerTitle = $"Temporada en Curso: {currentSeasonal.SeasonName} {currentSeasonal.Year}";
                        ActiveBannerDescription = $"{currentSeasonal.Subtitle}. Tus estadísticas y la playlist Top 30 se mantienen sincronizadas en tiempo real con tu biblioteca y reproducciones.";
                    }

                    Seasons.Clear();
                    foreach (var s in seasonList)
                    {
                        Seasons.Add(s);
                    }

                    SelectedSeason = Seasons.FirstOrDefault();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KronatlVm] Load error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
