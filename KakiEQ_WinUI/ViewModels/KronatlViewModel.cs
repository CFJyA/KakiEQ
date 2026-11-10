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

        public async Task LoadSeasonsAsync()
        {
            IsLoading = true;
            try
            {
                var libraryTracks = await _databaseService.GetAllTracksAsync();
                await _kronatlService.EnsureHistorySeededAsync(libraryTracks);

                var seasonList = _kronatlService.GetDefinedSeasons();
                Seasons.Clear();

                foreach (var s in seasonList)
                {
                    await _kronatlService.CalculateSeasonStatsAsync(s, libraryTracks);
                    await _kronatlService.GenerateOrGetTop30PlaylistAsync(s, libraryTracks);
                    Seasons.Add(s);
                }

                SelectedSeason = Seasons.FirstOrDefault();
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
