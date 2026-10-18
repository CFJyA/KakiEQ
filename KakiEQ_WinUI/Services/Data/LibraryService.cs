using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Data
{
    public class LibraryService
    {
        private readonly DatabaseService _dbService;

        public LibraryService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task<int> ScanFolderAsync(string folderPath, IProgress<(int current, int total, string file)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return 0;

            var files = new List<string>();
            try
            {
                var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".opus"
                };

                await Task.Run(() =>
                {
                    foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
                    {
                        if (exts.Contains(Path.GetExtension(file)))
                        {
                            files.Add(file);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error enumerating music files: " + ex.Message);
                return 0;
            }

            var tracks = new List<Track>();
            var cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ", "Covers");
            Directory.CreateDirectory(cacheFolder);

            int trackIdCounter = 1;
            int total = files.Count;

            var existingTracks = await _dbService.GetAllTracksAsync();
            var existingMap = existingTracks.Where(t => !string.IsNullOrEmpty(t.FilePath))
                                           .ToDictionary(t => t.FilePath, t => t, StringComparer.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                using var md5 = System.Security.Cryptography.MD5.Create();

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    progress?.Report((i + 1, total, Path.GetFileName(file)));

                    try
                    {
                        using var tfile = TagLib.File.Create(file);
                        string albumName = string.IsNullOrWhiteSpace(tfile.Tag.Album) ? "Unknown Album" : tfile.Tag.Album;
                        string artistName = string.IsNullOrWhiteSpace(tfile.Tag.FirstAlbumArtist)
                            ? (string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer) ? "Unknown Artist" : tfile.Tag.FirstPerformer)
                            : tfile.Tag.FirstAlbumArtist;

                        // Create unique hash based on artist and album to completely avoid cover collision
                        string safeKey = $"{artistName.Trim().ToLowerInvariant()}_{albumName.Trim().ToLowerInvariant()}";
                        byte[] hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(safeKey));
                        string hashFileName = Convert.ToHexString(hashBytes).ToLowerInvariant();
                        string? coverPath = Path.Combine(cacheFolder, $"{hashFileName}.jpg");

                        if (!File.Exists(coverPath) && tfile.Tag.Pictures.Length > 0)
                        {
                            try
                            {
                                var pic = tfile.Tag.Pictures[0];
                                File.WriteAllBytes(coverPath, pic.Data.Data);
                            }
                            catch { }
                        }

                        if (!File.Exists(coverPath)) coverPath = null;

                        int playCount = 0;
                        DateTime? lastPlayed = null;
                        if (existingMap.TryGetValue(file, out var oldTrack))
                        {
                            playCount = oldTrack.PlayCount;
                            lastPlayed = oldTrack.LastPlayed;
                        }

                        var track = new Track
                        {
                            Id = trackIdCounter++,
                            FilePath = file,
                            Title = string.IsNullOrWhiteSpace(tfile.Tag.Title) ? Path.GetFileNameWithoutExtension(file) : tfile.Tag.Title,
                            Artist = string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer) ? "Unknown Artist" : tfile.Tag.FirstPerformer,
                            Album = albumName,
                            Genre = string.IsNullOrWhiteSpace(tfile.Tag.FirstGenre) ? "Unknown Genre" : tfile.Tag.FirstGenre,
                            Duration = tfile.Properties.Duration,
                            TrackNumber = (int)tfile.Tag.Track,
                            Year = (int)tfile.Tag.Year,
                            DateAdded = DateTime.Now,
                            CoverImagePath = coverPath,
                            PlayCount = playCount,
                            LastPlayed = lastPlayed
                        };
                        tracks.Add(track);
                    }
                    catch
                    {
                        // Fallback for files without metadata
                        try
                        {
                            int playCount = 0;
                            DateTime? lastPlayed = null;
                            if (existingMap.TryGetValue(file, out var oldTrack))
                            {
                                playCount = oldTrack.PlayCount;
                                lastPlayed = oldTrack.LastPlayed;
                            }

                            tracks.Add(new Track
                            {
                                Id = trackIdCounter++,
                                FilePath = file,
                                Title = Path.GetFileNameWithoutExtension(file),
                                Artist = "Unknown Artist",
                                Album = "Unknown Album",
                                Genre = "Unknown Genre",
                                Duration = TimeSpan.Zero,
                                DateAdded = DateTime.Now,
                                CoverImagePath = null,
                                PlayCount = playCount,
                                LastPlayed = lastPlayed
                            });
                        }
                        catch { }
                    }
                }
            });

            await _dbService.ClearLibraryAsync();
            await _dbService.InsertTracksAsync(tracks);
            return tracks.Count;
        }
    }
}

