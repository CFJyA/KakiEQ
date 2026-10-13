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

        public ObservableCollection<Track> SuggestedTracks { get; } = new();
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
                var suggestions = allTracks.OrderBy(_ => rng.Next()).Take(16).ToList();
                var recents = allTracks.OrderByDescending(t => t.DateAdded).Take(6).ToList();

                RunOnUIThread(() =>
                {
                    SuggestedTracks.Clear();
                    foreach (var s in suggestions)
                    {
                        SuggestedTracks.Add(s);
                    }

                    RecentlyAddedTracks.Clear();
                    foreach (var r in recents)
                    {
                        RecentlyAddedTracks.Add(r);
                    }

                    OnPropertyChanged(nameof(SuggestedTracks));
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

