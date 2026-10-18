using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Data
{
    public class DatabaseService
    {
        private readonly string _dbPath;
        private readonly string _playlistsDbPath;

        public DatabaseService()
        {
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ");
            _dbPath = Path.Combine(dataFolder, "library_data.json");
            _playlistsDbPath = Path.Combine(dataFolder, "playlists_data.json");
            Directory.CreateDirectory(dataFolder);
        }

        public async Task<List<Track>> GetAllTracksAsync()
        {
            if (!File.Exists(_dbPath))
                return new List<Track>();

            try
            {
                var json = await Task.Run(() => File.ReadAllText(_dbPath));
                var tracks = JsonSerializer.Deserialize<List<Track>>(json) ?? new List<Track>();
                return tracks.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber).ToList();
            }
            catch
            {
                return new List<Track>();
            }
        }

        public async Task InsertTracksAsync(IEnumerable<Track> tracks)
        {
            var json = JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true });
            await Task.Run(() => File.WriteAllText(_dbPath, json));
        }

        public async Task ClearLibraryAsync()
        {
            if (File.Exists(_dbPath))
            {
                await Task.Run(() => File.Delete(_dbPath));
            }
        }

        public async Task<List<Playlist>> GetAllPlaylistsAsync()
        {
            if (!File.Exists(_playlistsDbPath))
            {
                return new List<Playlist>();
            }

            try
            {
                var json = await Task.Run(() => File.ReadAllText(_playlistsDbPath));
                var playlists = JsonSerializer.Deserialize<List<Playlist>>(json) ?? new List<Playlist>();
                return playlists.Where(p => !p.IsCreateCard).ToList();
            }
            catch
            {
                return new List<Playlist>();
            }
        }

        public async Task SavePlaylistsAsync(IEnumerable<Playlist> playlists)
        {
            var cleanList = playlists.Where(p => !p.IsCreateCard).ToList();
            var json = JsonSerializer.Serialize(cleanList, new JsonSerializerOptions { WriteIndented = true });
            await Task.Run(() => File.WriteAllText(_playlistsDbPath, json));
        }

        public async Task AddPlaylistAsync(Playlist playlist)
        {
            var playlists = await GetAllPlaylistsAsync();
            playlist.Id = playlists.Any() ? playlists.Max(p => p.Id) + 1 : 1;
            playlist.CreatedAt = DateTime.Now;
            playlists.Add(playlist);
            await SavePlaylistsAsync(playlists);
        }

        public async Task DeletePlaylistAsync(int playlistId)
        {
            var playlists = await GetAllPlaylistsAsync();
            var existing = playlists.FirstOrDefault(p => p.Id == playlistId);
            if (existing != null)
            {
                playlists.Remove(existing);
                await SavePlaylistsAsync(playlists);
            }
        }

        public async Task UpdatePlaylistAsync(Playlist playlist)
        {
            var playlists = await GetAllPlaylistsAsync();
            var existing = playlists.FirstOrDefault(p => p.Id == playlist.Id);
            if (existing != null)
            {
                playlists.Remove(existing);
                playlists.Add(playlist);
                await SavePlaylistsAsync(playlists);
            }
        }

        public async Task AddTrackToPlaylistAsync(string playlistName, Track track)
        {
            var playlists = await GetAllPlaylistsAsync();
            var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));

            if (playlist == null)
            {
                playlist = new Playlist { Name = playlistName, TrackCount = 0 };
                playlist.Id = playlists.Any() ? playlists.Max(p => p.Id) + 1 : 1;
                playlist.CreatedAt = DateTime.Now;
                playlists.Add(playlist);
            }

            if (!playlist.TrackIds.Contains(track.Id))
            {
                playlist.TrackIds.Add(track.Id);
            }
            if (!string.IsNullOrEmpty(track.FilePath) && !playlist.TrackFilePaths.Contains(track.FilePath))
            {
                playlist.TrackFilePaths.Add(track.FilePath);
            }
            playlist.TrackCount = playlist.TrackIds.Count;

            if (playlist.TrackCount == 1 || string.IsNullOrEmpty(playlist.CoverImagePath))
            {
                playlist.CoverImagePath = track.CoverImagePath;
            }

            await SavePlaylistsAsync(playlists);
        }

        public async Task AddTracksToPlaylistAsync(string playlistName, IEnumerable<Track> tracks)
        {
            var playlists = await GetAllPlaylistsAsync();
            var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));

            if (playlist == null)
            {
                playlist = new Playlist { Name = playlistName, TrackCount = 0 };
                playlist.Id = playlists.Any() ? playlists.Max(p => p.Id) + 1 : 1;
                playlist.CreatedAt = DateTime.Now;
                playlists.Add(playlist);
            }

            if (playlist.TrackIds == null) playlist.TrackIds = new List<int>();
            if (playlist.TrackFilePaths == null) playlist.TrackFilePaths = new List<string>();

            foreach (var track in tracks)
            {
                if (!playlist.TrackIds.Contains(track.Id))
                {
                    playlist.TrackIds.Add(track.Id);
                }
                if (!string.IsNullOrEmpty(track.FilePath) && !playlist.TrackFilePaths.Contains(track.FilePath))
                {
                    playlist.TrackFilePaths.Add(track.FilePath);
                }
                if (string.IsNullOrEmpty(playlist.CoverImagePath) && !string.IsNullOrEmpty(track.CoverImagePath))
                {
                    playlist.CoverImagePath = track.CoverImagePath;
                }
            }

            playlist.TrackCount = playlist.TrackIds.Count;
            await SavePlaylistsAsync(playlists);
        }

        public async Task UpdatePlaylistTracksOrderAsync(string playlistName, IEnumerable<Track> orderedTracks)
        {
            var playlists = await GetAllPlaylistsAsync();
            var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));
            if (playlist != null)
            {
                var trackList = orderedTracks.ToList();
                playlist.TrackIds = trackList.Select(t => t.Id).ToList();
                playlist.TrackFilePaths = trackList.Where(t => !string.IsNullOrEmpty(t.FilePath)).Select(t => t.FilePath).ToList();
                playlist.TrackCount = playlist.TrackIds.Count;
                await SavePlaylistsAsync(playlists);
            }
        }

        public async Task<List<Track>> GetTracksForPlaylistAsync(string playlistName)
        {
            var playlists = await GetAllPlaylistsAsync();
            var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));

            if (playlist == null)
                return new List<Track>();

            var allTracks = await GetAllTracksAsync();
            var trackByPath = allTracks.Where(t => !string.IsNullOrEmpty(t.FilePath))
                                       .ToDictionary(t => t.FilePath, t => t, StringComparer.OrdinalIgnoreCase);
            var trackById = allTracks.ToDictionary(t => t.Id, t => t);

            var result = new List<Track>();

            if (playlist.TrackFilePaths != null && playlist.TrackFilePaths.Count > 0)
            {
                foreach (var path in playlist.TrackFilePaths)
                {
                    if (trackByPath.TryGetValue(path, out var track))
                    {
                        if (!result.Contains(track))
                        {
                            result.Add(track);
                        }
                    }
                }
            }
            else if (playlist.TrackIds != null && playlist.TrackIds.Count > 0)
            {
                foreach (var id in playlist.TrackIds)
                {
                    if (trackById.TryGetValue(id, out var track))
                    {
                        if (!result.Contains(track))
                        {
                            result.Add(track);
                        }
                    }
                }
            }

            return result;
        }

        public async Task<bool> IsTrackInPlaylistAsync(string playlistName, Track track)
        {
            if (track == null) return false;
            var playlists = await GetAllPlaylistsAsync();
            var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));

            if (playlist == null) return false;
            bool matchesId = playlist.TrackIds != null && playlist.TrackIds.Contains(track.Id);
            bool matchesPath = playlist.TrackFilePaths != null && !string.IsNullOrEmpty(track.FilePath) && playlist.TrackFilePaths.Contains(track.FilePath);
            return matchesId || matchesPath;
        }

        public async Task RemoveTrackFromPlaylistAsync(string playlistName, Track track)
        {
            if (track == null) return;
            var playlists = await GetAllPlaylistsAsync();
            var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName, StringComparison.OrdinalIgnoreCase));

            if (playlist != null)
            {
                playlist.TrackIds?.Remove(track.Id);
                if (!string.IsNullOrEmpty(track.FilePath))
                {
                    playlist.TrackFilePaths?.Remove(track.FilePath);
                }
                playlist.TrackCount = playlist.TrackIds?.Count ?? 0;

                if (playlist.TrackCount == 0)
                {
                    playlist.CoverImagePath = null;
                }

                await SavePlaylistsAsync(playlists);
            }
        }

        public async Task RecordTrackPlayAsync(Track track)
        {
            if (track == null) return;
            try
            {
                var tracks = await GetAllTracksAsync();
                var existing = tracks.FirstOrDefault(t => t.Id == track.Id || (!string.IsNullOrEmpty(t.FilePath) && t.FilePath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase)));
                if (existing != null)
                {
                    existing.PlayCount++;
                    existing.LastPlayed = DateTime.Now;
                    track.PlayCount = existing.PlayCount;
                    track.LastPlayed = existing.LastPlayed;
                    await InsertTracksAsync(tracks);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Database] Error recording play: {ex.Message}");
            }
        }

        public string? GetArtistPhotoPath(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName)) return null;
            var artistsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Artists");
            if (!Directory.Exists(artistsFolder)) return null;
            string safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
            try
            {
                var files = Directory.GetFiles(artistsFolder, $"{safeName}*.jpg");
                if (files.Length > 0)
                {
                    return files.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
                }
            }
            catch { }
            return null;
        }

        public string SaveArtistPhoto(string artistName, string sourceImagePath)
        {
            var artistsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Artists");
            Directory.CreateDirectory(artistsFolder);
            string safeName = string.Join("_", artistName.Split(Path.GetInvalidFileNameChars()));
            
            // Remove previous photos for this artist to save space
            try
            {
                var oldFiles = Directory.GetFiles(artistsFolder, $"{safeName}*.jpg");
                foreach (var old in oldFiles)
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }

            string targetPath = Path.Combine(artistsFolder, $"{safeName}_{DateTime.Now.Ticks}.jpg");
            File.Copy(sourceImagePath, targetPath, true);
            return targetPath;
        }
    }
}

