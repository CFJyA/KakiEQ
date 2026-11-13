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
    public enum AlbumFilterType
    {
        All,
        Single,
        Ep,
        Album
    }

    public class AlbumsViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;
        private readonly List<AlbumItem> _allAlbums = new();
        private string _searchQuery = string.Empty;
        private AlbumFilterType _selectedFilter = AlbumFilterType.All;

        public ObservableCollection<AlbumItem> Albums { get; } = new();

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    FilterAlbums();
                }
            }
        }

        public AlbumFilterType SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (_selectedFilter != value)
                {
                    _selectedFilter = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterSingle));
                    OnPropertyChanged(nameof(IsFilterEp));
                    OnPropertyChanged(nameof(IsFilterAlbum));
                    FilterAlbums();
                }
            }
        }

        public bool IsFilterAll => _selectedFilter == AlbumFilterType.All;
        public bool IsFilterSingle => _selectedFilter == AlbumFilterType.Single;
        public bool IsFilterEp => _selectedFilter == AlbumFilterType.Ep;
        public bool IsFilterAlbum => _selectedFilter == AlbumFilterType.Album;

        public string TotalAlbumsCountFormatted => _selectedFilter switch
        {
            AlbumFilterType.Single => $"{Albums.Count} {(Albums.Count == 1 ? "sencillo" : "sencillos")}",
            AlbumFilterType.Ep => $"{Albums.Count} {(Albums.Count == 1 ? "EP" : "EPs")}",
            AlbumFilterType.Album => $"{Albums.Count} {(Albums.Count == 1 ? "álbum" : "álbumes")}",
            _ => $"{Albums.Count} {(Albums.Count == 1 ? "álbum" : "álbumes")}"
        };

        public ICommand FilterAllCommand { get; }
        public ICommand FilterSingleCommand { get; }
        public ICommand FilterEpCommand { get; }
        public ICommand FilterAlbumCommand { get; }

        public AlbumsViewModel(DatabaseService dbService)
        {
            _dbService = dbService;

            FilterAllCommand = new RelayCommand(() => SelectedFilter = AlbumFilterType.All);
            FilterSingleCommand = new RelayCommand(() => SelectedFilter = AlbumFilterType.Single);
            FilterEpCommand = new RelayCommand(() => SelectedFilter = AlbumFilterType.Ep);
            FilterAlbumCommand = new RelayCommand(() => SelectedFilter = AlbumFilterType.Album);

            _ = LoadAlbumsAsync();
        }

        public async Task LoadAlbumsAsync()
        {
            var uniqueAlbums = await Task.Run(async () =>
            {
                var tracks = await _dbService.GetAllTracksAsync();
                return AlbumHelper.BuildAlbumItems(tracks);
            });

            RunOnUIThread(() =>
            {
                _allAlbums.Clear();
                _allAlbums.AddRange(uniqueAlbums);
                FilterAlbums();
            });
        }

        private void FilterAlbums()
        {
            string query = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = _allAlbums.AsEnumerable();

            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(a => 
                    (a.Title != null && a.Title.ToLowerInvariant().Contains(query)) ||
                    (a.Artist != null && a.Artist.ToLowerInvariant().Contains(query)));
            }

            filtered = _selectedFilter switch
            {
                AlbumFilterType.Single => filtered.Where(a => a.ReleaseType == ReleaseTypeHelper.TypeSingle),
                AlbumFilterType.Ep => filtered.Where(a => a.ReleaseType == ReleaseTypeHelper.TypeEp),
                AlbumFilterType.Album => filtered.Where(a => a.ReleaseType == ReleaseTypeHelper.TypeAlbum),
                _ => filtered
            };

            var list = filtered.ToList();

            Albums.Clear();
            foreach (var album in list)
            {
                Albums.Add(album);
            }
            OnPropertyChanged(nameof(TotalAlbumsCountFormatted));
        }
    }
}
