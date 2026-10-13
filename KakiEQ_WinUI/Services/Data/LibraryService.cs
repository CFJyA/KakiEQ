using System;
using System.Collections.Generic;
using System.IO;
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

            await Task.Run(() =>
            {
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    progress?.Report((i + 1, total, Path.GetFileName(file)));

                    try
                    {
                        using var tfile = TagLib.File.Create(file);
                        string albumName = string.IsNullOrWhiteSpace(tfile.Tag.Album) ? "Unknown Album" : tfile.Tag.Album;
                        string safeAlbumName = string.Join("_", albumName.Split(Path.GetInvalidFileNameChars()));
                        string? coverPath = Path.Combine(cacheFolder, $"{safeAlbumName}.jpg");

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
                            CoverImagePath = coverPath
                        };
                        tracks.Add(track);
                    }
                    catch
                    {
                        // Fallback for files without metadata
                        try
                        {
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
                                CoverImagePath = null
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

