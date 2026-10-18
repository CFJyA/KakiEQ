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
    public class HomeViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        public ObservableCollection<Track> HistoryTracks { get; } = new();
        public ObservableCollection<Track> SuggestedTracks { get; } = new();
        public ObservableCollection<Track> FavoriteTracks { get; } = new();
        public ObservableCollection<Track> RecentlyAddedTracks { get; } = new();

        public ICommand RefreshSuggestionsCommand { get; }
        public ICommand PlayRandomCommand { get; }

        public HomeViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            RefreshSuggestionsCommand = new RelayCommand(async () => await LoadSuggestionsAsync());
            PlayRandomCommand = new RelayCommand(async () => await PlayRandomAsync());
            _ = LoadSuggestionsAsync();
        }

        public async Task LoadSuggestionsAsync()
        {
            var allTracks = await _dbService.GetAllTracksAsync();
            if (allTracks.Count > 0)
            {
                var rng = new Random();

                // 1. Historial Musical (recently played tracks - 10 items for 2 full rows of 5 or 1 full row of 10)
                var history = allTracks.Where(t => t.LastPlayed.HasValue)
                                       .OrderByDescending(t => t.LastPlayed)
                                       .Take(10).ToList();
                if (history.Count == 0)
                {
                    history = allTracks.Take(10).ToList();
                }

                // 2. Recomendados para ti (random mix - 20 items for 4 rows of 5 or 2 rows of 10)
                var suggestions = allTracks.OrderBy(_ => rng.Next()).Take(20).ToList();

                // 3. Tus Favoritos (songs most played by the user - 10 items for full rows)
                var favorites = allTracks.Where(t => t.PlayCount > 0)
                                         .OrderByDescending(t => t.PlayCount)
                                         .Take(10).ToList();
                if (favorites.Count == 0)
                {
                    // Fallback to liked songs in Yollotl playlist if available
                    var likedTracks = await _dbService.GetTracksForPlaylistAsync("Yollotl");
                    if (likedTracks.Count > 0)
                    {
                        favorites = likedTracks.Take(10).ToList();
                    }
                    else
                    {
                        favorites = allTracks.OrderBy(_ => rng.Next()).Take(10).ToList();
                    }
                }

                // 4. Añadidos recientemente (ordered by DateAdded descending)
                var recents = allTracks.OrderByDescending(t => t.DateAdded).Take(10).ToList();

                RunOnUIThread(() =>
                {
                    HistoryTracks.Clear();
                    foreach (var h in history)
                    {
                        HistoryTracks.Add(h);
                    }

                    SuggestedTracks.Clear();
                    foreach (var s in suggestions)
                    {
                        SuggestedTracks.Add(s);
                    }

                    FavoriteTracks.Clear();
                    foreach (var f in favorites)
                    {
                        FavoriteTracks.Add(f);
                    }

                    RecentlyAddedTracks.Clear();
                    foreach (var r in recents)
                    {
                        RecentlyAddedTracks.Add(r);
                    }

                    OnPropertyChanged(nameof(HistoryTracks));
                    OnPropertyChanged(nameof(SuggestedTracks));
                    OnPropertyChanged(nameof(FavoriteTracks));
                    OnPropertyChanged(nameof(RecentlyAddedTracks));
                });
            }
        }

        private async Task PlayRandomAsync()
        {
            var allTracks = await _dbService.GetAllTracksAsync();
            if (allTracks.Any())
            {
                var rng = new Random();
                int idx = rng.Next(allTracks.Count);
                await MainViewModel.Instance!.PlayTrackAsync(allTracks[idx], allTracks);
            }
        }
    }
}

