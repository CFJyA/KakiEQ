using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class LibraryViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;
        private List<Track> _allTracksCache = new();

        public ObservableCollection<Track> FilteredTracks { get; } = new();

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        private int _selectedSortIndex = 0;
        public int SelectedSortIndex
        {
            get => _selectedSortIndex;
            set
            {
                if (_selectedSortIndex != value)
                {
                    _selectedSortIndex = value;
                    OnPropertyChanged();
                    ApplyFilterAndSort();
                }
            }
        }

        public ICommand ShuffleAllCommand { get; }

        public LibraryViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            ShuffleAllCommand = new RelayCommand(ShuffleAll);
            _ = LoadLibraryAsync();
        }

        public async Task LoadLibraryAsync()
        {
            _allTracksCache = await _dbService.GetAllTracksAsync();
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            var query = _allTracksCache.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                query = query.Where(t =>
                    t.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    t.Artist.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    t.Album.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
            }

            query = _selectedSortIndex switch
            {
                0 => query.OrderBy(t => t.Title),
                1 => query.OrderByDescending(t => t.Title),
                2 => query.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber),
                3 => query.OrderByDescending(t => t.Year),
                4 => query.OrderByDescending(t => t.Duration),
                _ => query.OrderBy(t => t.Title)
            };

            var list = query.ToList();

            RunOnUIThread(() =>
            {
                FilteredTracks.Clear();
                foreach (var track in list)
                {
                    FilteredTracks.Add(track);
                }
                OnPropertyChanged(nameof(FilteredTracks));
                OnPropertyChanged(nameof(TotalTracksCount));
                OnPropertyChanged(nameof(TotalTracksCountFormatted));
            });
        }

        public int TotalTracksCount => FilteredTracks.Count;
        public string TotalTracksCountFormatted => $"{FilteredTracks.Count} canciones";

        private void ShuffleAll()
        {
            if (!_allTracksCache.Any()) return;
            var rng = new Random();
            var shuffled = _allTracksCache.OrderBy(_ => rng.Next()).ToList();
            if (shuffled.Any())
            {
                _ = MainViewModel.Instance?.PlayTrackAsync(shuffled[0], shuffled);
            }
        }
    }
}

