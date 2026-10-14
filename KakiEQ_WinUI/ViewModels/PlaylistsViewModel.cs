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
    public class PlaylistsViewModel : ViewModelBase
    {
        private readonly DatabaseService _dbService;

        public ObservableCollection<Playlist> Playlists { get; } = new();

        public ICommand CreatePlaylistCommand { get; }

        public PlaylistsViewModel(DatabaseService dbService)
        {
            _dbService = dbService;
            CreatePlaylistCommand = new RelayCommand(async () => await CreatePlaylistAsync());
            _ = LoadPlaylistsAsync();
        }

        public async Task LoadPlaylistsAsync()
        {
            var playlists = await _dbService.GetAllPlaylistsAsync();
            RunOnUIThread(() =>
            {
                Playlists.Clear();

                // 1. First card is always the '+' Create Playlist card
                Playlists.Add(new Playlist
                {
                    Id = -1,
                    Name = "Crear Playlist",
                    IsCreateCard = true
                });

                // 2. User created playlists
                foreach (var p in playlists.Where(x => !x.IsCreateCard))
                {
                    Playlists.Add(p);
                }

                OnPropertyChanged(nameof(Playlists));
            });
        }

        public async Task CreatePlaylistAsync(string? name = null)
        {
            int realCount = Playlists.Count(p => !p.IsCreateCard);
            string playlistName = string.IsNullOrWhiteSpace(name) ? $"Playlist {realCount + 1}" : name;
            var newPlaylist = new Playlist { Name = playlistName, TrackCount = 0 };
            await _dbService.AddPlaylistAsync(newPlaylist);
            await LoadPlaylistsAsync();
            if (MainViewModel.Instance != null)
            {
                await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
            }
        }

        public async Task DeletePlaylistAsync(Playlist playlist)
        {
            if (playlist == null || playlist.IsCreateCard) return;
            await _dbService.DeletePlaylistAsync(playlist.Id);
            await LoadPlaylistsAsync();
            if (MainViewModel.Instance != null)
            {
                await MainViewModel.Instance.LoadGlobalPlaylistsAsync();
            }
        }
    }
}

