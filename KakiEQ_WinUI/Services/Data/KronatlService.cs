using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Data
{
    public class KronatlService
    {
        private readonly DatabaseService _databaseService;
        private readonly string _historyDbPath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private List<PlaybackSessionLog>? _cachedLogs;
        private bool _hasReconciledThisSession = false;

        private static readonly DateTime InceptionDate = new(2026, 9, 1, 0, 0, 0);
        private static readonly DateTime KnownFirstPlayDate = new(2026, 10, 17, 0, 0, 0);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public KronatlService(DatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ");
            _historyDbPath = Path.Combine(dataFolder, "playback_history.json");
            Directory.CreateDirectory(dataFolder);
        }

        private async Task<List<PlaybackSessionLog>> LoadLogsInternalAsync()
        {
            if (_cachedLogs != null) return _cachedLogs;

            if (!File.Exists(_historyDbPath))
            {
                _cachedLogs = new List<PlaybackSessionLog>();
                return _cachedLogs;
            }

            try
            {
                var json = await Task.Run(() => File.ReadAllText(_historyDbPath));
                _cachedLogs = JsonSerializer.Deserialize<List<PlaybackSessionLog>>(json) ?? new List<PlaybackSessionLog>();
            }
            catch
            {
                _cachedLogs = new List<PlaybackSessionLog>();
            }

            return _cachedLogs;
        }

        private async Task SaveLogsAtomicallyAsync(List<PlaybackSessionLog> logs)
        {
            var json = JsonSerializer.Serialize(logs, JsonOptions);
            await Task.Run(() =>
            {
                string tmpPath = _historyDbPath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _historyDbPath, overwrite: true);
            });
        }

        public async Task<List<PlaybackSessionLog>> GetAllPlaybackLogsAsync()
        {
            if (_cachedLogs != null) return _cachedLogs.ToList();

            await _lock.WaitAsync();
            try
            {
                var logs = await LoadLogsInternalAsync();
                return logs.ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RecordPlaybackSessionAsync(Track track, double listenedSeconds, bool completedWithoutSkip = true)
        {
            if (track == null) return;

            double effectiveDuration = track.Duration.TotalSeconds > 0 ? track.Duration.TotalSeconds : Math.Max(listenedSeconds, 180.0);
            double effectiveListened = listenedSeconds > 0 ? listenedSeconds : effectiveDuration;
            string extFormat = !string.IsNullOrEmpty(track.AudioFormat)
                ? track.AudioFormat
                : (!string.IsNullOrEmpty(track.FilePath) ? Path.GetExtension(track.FilePath).TrimStart('.').ToUpperInvariant() : "FLAC");

            var log = new PlaybackSessionLog
            {
                TrackId = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                FilePath = track.FilePath,
                DurationSeconds = effectiveDuration,
                ListenedSeconds = effectiveListened,
                AudioFormat = string.IsNullOrEmpty(extFormat) ? "FLAC" : extFormat,
                BitsPerSample = track.BitsPerSample > 0 ? track.BitsPerSample : 16,
                Bitrate = track.Bitrate,
                Bpm = track.Bpm,
                CamelotKey = CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey),
                CoverImagePath = track.CoverImagePath,
                Timestamp = DateTime.Now,
                CompletedWithoutSkip = completedWithoutSkip,
                IsBackfilled = false
            };

            await _lock.WaitAsync();
            try
            {
                var logs = await LoadLogsInternalAsync();
                logs.Add(log);
                _cachedLogs = logs;
                await SaveLogsAtomicallyAsync(logs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kronatl] Error recording session: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Repairs legacy corrupted seeded logs, refreshes stale TrackId/metadata snapshots via FilePath,
        /// and reconciles missing PlayCount entries from the library into their true seasonal period.
        /// </summary>
        public async Task EnsureHistorySeededAsync(List<Track> allTracks, bool forceReconcile = false)
        {
            if (allTracks == null || allTracks.Count == 0) return;
            if (_hasReconciledThisSession && !forceReconcile) return;

            bool anyHistoryChanged = false;

            await _lock.WaitAsync();
            try
            {
                if (_hasReconciledThisSession && !forceReconcile) return;

                var existingLogs = await LoadLogsInternalAsync();
                var lookup = BuildTrackLookup(allTracks);

                // 1. Detect legacy Random(42) seeded batch: logs with Timestamp < InceptionDate
                //    OR >= 30 non-backfilled logs spanning >= 15 distinct calendar days with identical sub-minute tick signature.
                var suspiciousTickGroups = existingLogs
                    .Where(l => !l.IsBackfilled)
                    .GroupBy(l => l.Timestamp.Ticks % TimeSpan.TicksPerMinute)
                    .Where(g => g.Key != 0 && g.Count() >= 30 && g.Select(x => x.Timestamp.Date).Distinct().Count() >= 15)
                    .Select(g => g.Key)
                    .ToHashSet();

                var cleanLogs = new List<PlaybackSessionLog>(existingLogs.Count);
                foreach (var log in existingLogs)
                {
                    bool isPreInception = log.Timestamp < InceptionDate;
                    bool isLegacySeeded = !log.IsBackfilled && suspiciousTickGroups.Contains(log.Timestamp.Ticks % TimeSpan.TicksPerMinute);

                    if (isPreInception || isLegacySeeded)
                    {
                        anyHistoryChanged = true;
                        continue;
                    }

                    // 2. Heal stale TrackId and metadata snapshots using authoritative FilePath lookup
                    var resolved = ResolveTrack(log, lookup);
                    if (resolved != null)
                    {
                        if (log.TrackId != resolved.Id) { log.TrackId = resolved.Id; anyHistoryChanged = true; }
                        if (!string.Equals(log.Title, resolved.Title, StringComparison.Ordinal)) { log.Title = resolved.Title; anyHistoryChanged = true; }
                        if (!string.Equals(log.Artist, resolved.Artist, StringComparison.Ordinal)) { log.Artist = resolved.Artist; anyHistoryChanged = true; }
                        if (!string.Equals(log.Album, resolved.Album, StringComparison.Ordinal)) { log.Album = resolved.Album; anyHistoryChanged = true; }
                        if (!string.IsNullOrEmpty(resolved.FilePath) && !string.Equals(log.FilePath, resolved.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            log.FilePath = resolved.FilePath;
                            anyHistoryChanged = true;
                        }
                        if (resolved.Duration.TotalSeconds > 0 && Math.Abs(log.DurationSeconds - resolved.Duration.TotalSeconds) > 1.0)
                        {
                            log.DurationSeconds = resolved.Duration.TotalSeconds;
                            if (log.IsBackfilled || log.ListenedSeconds <= 15.1)
                            {
                                log.ListenedSeconds = resolved.Duration.TotalSeconds;
                            }
                            anyHistoryChanged = true;
                        }
                        if (!string.IsNullOrEmpty(resolved.AudioFormat) && !string.Equals(log.AudioFormat, resolved.AudioFormat, StringComparison.OrdinalIgnoreCase))
                        {
                            log.AudioFormat = resolved.AudioFormat;
                            anyHistoryChanged = true;
                        }
                        if (resolved.BitsPerSample > 0 && log.BitsPerSample != resolved.BitsPerSample)
                        {
                            log.BitsPerSample = resolved.BitsPerSample;
                            anyHistoryChanged = true;
                        }
                        if (resolved.Bitrate > 0 && log.Bitrate != resolved.Bitrate)
                        {
                            log.Bitrate = resolved.Bitrate;
                            anyHistoryChanged = true;
                        }
                        if (resolved.Bpm > 0 && Math.Abs(log.Bpm - resolved.Bpm) > 0.5)
                        {
                            log.Bpm = resolved.Bpm;
                            anyHistoryChanged = true;
                        }
                        string normKey = CamelotColorConverter.NormalizeCamelotKey(resolved.CamelotKey);
                        if (!string.IsNullOrEmpty(normKey) && !string.Equals(log.CamelotKey, normKey, StringComparison.Ordinal))
                        {
                            log.CamelotKey = normKey;
                            anyHistoryChanged = true;
                        }
                        if (!string.IsNullOrEmpty(resolved.CoverImagePath) && !string.Equals(log.CoverImagePath, resolved.CoverImagePath, StringComparison.OrdinalIgnoreCase))
                        {
                            log.CoverImagePath = resolved.CoverImagePath;
                            anyHistoryChanged = true;
                        }
                    }

                    cleanLogs.Add(log);
                }

                // 3. Reconcile log counts against library PlayCount per canonical track key
                var logsByKey = new Dictionary<string, List<PlaybackSessionLog>>(StringComparer.OrdinalIgnoreCase);
                foreach (var log in cleanLogs)
                {
                    var resolved = ResolveTrack(log, lookup);
                    string key = GetCanonicalLogKey(log, resolved);
                    if (!logsByKey.TryGetValue(key, out var list))
                    {
                        list = new List<PlaybackSessionLog>();
                        logsByKey[key] = list;
                    }
                    list.Add(log);
                }

                var now = DateTime.Now;
                var rebuiltLogs = new List<PlaybackSessionLog>(cleanLogs.Count);
                var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var track in allTracks)
                {
                    string key = GetCanonicalTrackKey(track);
                    if (!processedKeys.Add(key)) continue;

                    logsByKey.TryGetValue(key, out var existingForTrack);
                    existingForTrack ??= new List<PlaybackSessionLog>();

                    var realLogs = existingForTrack.Where(l => !l.IsBackfilled).OrderBy(l => l.Timestamp).ToList();
                    var backfillLogs = existingForTrack.Where(l => l.IsBackfilled).OrderBy(l => l.Timestamp).ToList();

                    rebuiltLogs.AddRange(realLogs);

                    int desiredTotal = Math.Max(realLogs.Count, track.PlayCount);
                    int neededBackfills = Math.Max(0, desiredTotal - realLogs.Count);

                    if (backfillLogs.Count == neededBackfills)
                    {
                        rebuiltLogs.AddRange(backfillLogs);
                    }
                    else if (backfillLogs.Count > neededBackfills)
                    {
                        // Trim excess backfills cleanly
                        rebuiltLogs.AddRange(backfillLogs.Take(neededBackfills));
                        anyHistoryChanged = true;
                    }
                    else
                    {
                        // Keep existing valid backfills and synthesize only the missing historical plays
                        rebuiltLogs.AddRange(backfillLogs);
                        int missing = neededBackfills - backfillLogs.Count;
                        anyHistoryChanged = true;

                        DateTime anchorEnd = track.LastPlayed ?? now;
                        if (anchorEnd < InceptionDate) anchorEnd = InceptionDate;
                        if (anchorEnd > now) anchorEnd = now;

                        DateTime seasonStart = GetSeasonWindowStartForDate(anchorEnd);
                        DateTime anchorStart = seasonStart;

                        if (track.DateAdded >= seasonStart && track.DateAdded <= anchorEnd)
                        {
                            anchorStart = track.DateAdded;
                        }
                        else if (anchorEnd >= KnownFirstPlayDate && seasonStart < KnownFirstPlayDate)
                        {
                            anchorStart = KnownFirstPlayDate;
                        }

                        if (anchorStart > anchorEnd) anchorStart = anchorEnd;

                        double spanMinutes = Math.Max(1.0, (anchorEnd - anchorStart).TotalMinutes);
                        int seed = StringComparer.OrdinalIgnoreCase.GetHashCode(key);
                        var rng = new Random(seed);

                        double durSecs = track.Duration.TotalSeconds > 0 ? track.Duration.TotalSeconds : 180.0;
                        string fmt = !string.IsNullOrEmpty(track.AudioFormat)
                            ? track.AudioFormat
                            : (!string.IsNullOrEmpty(track.FilePath) ? Path.GetExtension(track.FilePath).TrimStart('.').ToUpperInvariant() : "FLAC");
                        if (string.IsNullOrEmpty(fmt)) fmt = "FLAC";

                        for (int m = 0; m < missing; m++)
                        {
                            DateTime ts;
                            if (m == 0 && backfillLogs.Count == 0 && realLogs.Count == 0 && track.LastPlayed.HasValue && track.LastPlayed.Value >= InceptionDate)
                            {
                                ts = anchorEnd;
                            }
                            else
                            {
                                double frac = (m + 0.15 + rng.NextDouble() * 0.7) / Math.Max(1, missing);
                                ts = anchorStart.AddMinutes(frac * spanMinutes)
                                    .AddSeconds(rng.Next(0, 59))
                                    .AddMilliseconds(rng.Next(1, 998));
                                if (ts > anchorEnd) ts = anchorEnd;
                            }

                            rebuiltLogs.Add(new PlaybackSessionLog
                            {
                                TrackId = track.Id,
                                Title = track.Title,
                                Artist = track.Artist,
                                Album = track.Album,
                                FilePath = track.FilePath,
                                DurationSeconds = durSecs,
                                ListenedSeconds = durSecs,
                                AudioFormat = fmt,
                                BitsPerSample = track.BitsPerSample > 0 ? track.BitsPerSample : 16,
                                Bitrate = track.Bitrate,
                                Bpm = track.Bpm,
                                CamelotKey = CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey),
                                CoverImagePath = track.CoverImagePath,
                                Timestamp = ts,
                                CompletedWithoutSkip = true,
                                IsBackfilled = true
                            });
                        }
                    }
                }

                // Preserve any logs for tracks temporarily not in the library snapshot
                foreach (var kvp in logsByKey)
                {
                    if (!processedKeys.Contains(kvp.Key))
                    {
                        rebuiltLogs.AddRange(kvp.Value);
                    }
                }

                rebuiltLogs = rebuiltLogs.OrderBy(l => l.Timestamp).ToList();
                _cachedLogs = rebuiltLogs;
                _hasReconciledThisSession = true;

                if (anyHistoryChanged || !File.Exists(_historyDbPath))
                {
                    await SaveLogsAtomicallyAsync(rebuiltLogs);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kronatl] Error reconciling history: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }

            if (anyHistoryChanged)
            {
                await SyncSavedKronatlPlaylistsAsync(allTracks);
            }
        }

        private static DateTime GetSeasonWindowStartForDate(DateTime dt)
        {
            if (dt.Month == 12) return new DateTime(dt.Year, 12, 1, 0, 0, 0);
            if (dt.Month <= 2) return new DateTime(dt.Year - 1, 12, 1, 0, 0, 0);
            if (dt.Month <= 5) return new DateTime(dt.Year, 3, 1, 0, 0, 0);
            if (dt.Month <= 8) return new DateTime(dt.Year, 6, 1, 0, 0, 0);
            return new DateTime(dt.Year, 9, 1, 0, 0, 0);
        }

        /// <summary>
        /// Creates a complete season definition for any season type and year, including rotating yearly sub-themes
        /// so future cycles never feel monotonous.
        /// </summary>
        public KronatlSeason CreateSeasonDefinition(KronatlSeasonType type, int year)
        {
            int variant = Math.Abs(year - 2026) % 3;

            return type switch
            {
                KronatlSeasonType.Invierno => new KronatlSeason
                {
                    Id = $"invierno_{year}",
                    Type = KronatlSeasonType.Invierno,
                    Year = year,
                    SeasonName = "Invierno",
                    Title = $"Kronatl de Invierno {year}",
                    Subtitle = $"Diciembre {year - 1} – Febrero {year}",
                    LoreDescription = variant switch
                    {
                        1 => "Paisajes de escarcha boreal, frecuencias cristalinas y el refugio cálido de tu música en pleno invierno.",
                        2 => "Ecos del solsticio invernal: armonías profundas que brillaron como estrellas en la quietud de la nieve.",
                        _ => "Frecuencias gélidas, el susurro de la ventisca y paisajes de introspección sonora en las noches más largas."
                    },
                    SubtitleQuote = "Frecuencias gélidas, el susurro de la ventisca y transiciones melódicas inspiradas en la calma invernal del bosque.",
                    SeasonalBadgeText = "CRISTALES · INVIERNO",
                    LoreBadgeText = variant == 1 ? "Aurora y Escarcha" : (variant == 2 ? "Solsticio Boreal" : "Nieves y Escarcha"),
                    EmblemImagePath = "ms-appx:///Assets/kronatl_emblem_invierno.png",
                    ParticlesImagePath = "ms-appx:///Assets/kronatl_particles_invierno.png",
                    PeriodStart = new DateTime(year - 1, 12, 1, 0, 0, 0),
                    PeriodEnd = new DateTime(year, 2, DateTime.DaysInMonth(year, 2), 23, 59, 59),
                    ReleaseDate = new DateTime(year, 3, 1),
                    EmblemGlyph = "\uE708",
                    AccentColor = "#0284C7",
                    SecondaryAccentColor = "#38BDF8",
                    TertiaryAccentColor = "#E0F2FE",
                    OpenCodexBackground = "#0284C7",
                    OpenCodexForeground = "#FFFFFF",
                    CardBorderBrush = "#1E3A8A",
                    CardBackground = "#101726",
                    BackgroundGradientStart = "#0C2D57",
                    BackgroundGradientEnd = "#16102C",
                    PaletteDescription = "Blancos, Azules y Morados"
                },

                KronatlSeasonType.Primavera => new KronatlSeason
                {
                    Id = $"primavera_{year}",
                    Type = KronatlSeasonType.Primavera,
                    Year = year,
                    SeasonName = "Primavera",
                    Title = $"Kronatl de Primavera {year}",
                    Subtitle = $"Marzo – Mayo {year}",
                    LoreDescription = variant switch
                    {
                        1 => "Una explosión cromática de ritmos vivos, pétalos al viento y melodías que renovaron tu energía diaria.",
                        2 => "El equinoccio sonoro: cuerdas vibrantes, brisa fresca y el despertar acústico de tu biblioteca.",
                        _ => "El florecimiento armónico, el renacer de la tierra y transiciones luminosas que celebran la energía de nuevos ciclos."
                    },
                    SubtitleQuote = "Frecuencias frescas, el renacer de la tierra y transiciones melodiosas que celebran el despertar natural.",
                    SeasonalBadgeText = "FLORECER · PRIMAVERA",
                    LoreBadgeText = variant == 1 ? "Pétalos y Brisa" : (variant == 2 ? "Equinoccio Floral" : "Brotes y Capullos"),
                    EmblemImagePath = "ms-appx:///Assets/kronatl_emblem_primavera.png",
                    ParticlesImagePath = "ms-appx:///Assets/kronatl_particles_primavera.png",
                    PeriodStart = new DateTime(year, 3, 1, 0, 0, 0),
                    PeriodEnd = new DateTime(year, 5, 31, 23, 59, 59),
                    ReleaseDate = new DateTime(year, 6, 1),
                    EmblemGlyph = "\uE8F3",
                    AccentColor = "#FF4071",
                    SecondaryAccentColor = "#10B981",
                    TertiaryAccentColor = "#BB86FC",
                    OpenCodexBackground = "#FF4071",
                    OpenCodexForeground = "#FFFFFF",
                    CardBorderBrush = "#264A35",
                    CardBackground = "#101B16",
                    BackgroundGradientStart = "#3A1432",
                    BackgroundGradientEnd = "#0E241B",
                    PaletteDescription = "Rosa cerezo, Verdes y Morados"
                },

                KronatlSeasonType.Verano => new KronatlSeason
                {
                    Id = $"verano_{year}",
                    Type = KronatlSeasonType.Verano,
                    Year = year,
                    SeasonName = "Verano",
                    Title = $"Kronatl de Verano {year}",
                    Subtitle = $"Junio – Agosto {year}",
                    LoreDescription = variant switch
                    {
                        1 => "Atardeceres dorados frente al mar, bajos profundos y la euforia luminosa de tus himnos de verano.",
                        2 => "Horizontes solares y olas acústicas: la banda sonora de tus días más cálidos y vibrantes.",
                        _ => "El resplandor del Sol cenital, frecuencias cálidas y el pulso vibrante de los días largos de playa y libertad."
                    },
                    SubtitleQuote = "Frecuencias enérgicas, el calor del sol y transiciones vibrantes inspiradas en la libertad y frescura del mar.",
                    SeasonalBadgeText = "CALIDEZ · VERANO",
                    LoreBadgeText = variant == 1 ? "Brisa y Horizonte" : (variant == 2 ? "Solsticio Dorado" : "Olas y Sol"),
                    EmblemImagePath = "ms-appx:///Assets/kronatl_emblem_verano.png",
                    ParticlesImagePath = "ms-appx:///Assets/kronatl_particles_verano.png",
                    PeriodStart = new DateTime(year, 6, 1, 0, 0, 0),
                    PeriodEnd = new DateTime(year, 8, 31, 23, 59, 59),
                    ReleaseDate = new DateTime(year, 9, 1),
                    EmblemGlyph = "\uE706",
                    AccentColor = "#FF7A00",
                    SecondaryAccentColor = "#FFB800",
                    TertiaryAccentColor = "#0284C7",
                    OpenCodexBackground = "#FF7A00",
                    OpenCodexForeground = "#000000",
                    CardBorderBrush = "#3D2C18",
                    CardBackground = "#141B24",
                    BackgroundGradientStart = "#42200E",
                    BackgroundGradientEnd = "#101E32",
                    PaletteDescription = "Amarillo, Morado y Naranja"
                },

                KronatlSeasonType.Otoño => new KronatlSeason
                {
                    Id = $"otono_{year}",
                    Type = KronatlSeasonType.Otoño,
                    Year = year,
                    SeasonName = "Otoño",
                    Title = $"Kronatl de Otoño {year}",
                    Subtitle = $"Septiembre – Noviembre {year}",
                    LoreDescription = year == 2026
                        ? "El primer ciclo en la historia de Kaki EQ: frecuencias cálidas de otoño, el aroma del cempasúchil y transiciones armónicas que conectan con el Mictlán."
                        : (variant == 1
                            ? "Vientos de octubre, hojas de ámbar y el misticismo sonoro de las noches de copal y cempasúchil."
                            : "Frecuencias cálidas de otoño, altares de luz y melodías que trascienden el tiempo."),
                    SubtitleQuote = "Frecuencias cálidas de otoño, el aroma del cempasúchil y transiciones armónicas que conectan con el Mictlán.",
                    SeasonalBadgeText = "DÍA DE MUERTOS · OTOÑO",
                    LoreBadgeText = variant == 1 ? "Copal y Ámbar" : (variant == 2 ? "Altar de Frecuencias" : "Cempasúchil y Ceniza"),
                    EmblemImagePath = "ms-appx:///Assets/kronatl_emblem_otono.png",
                    ParticlesImagePath = "ms-appx:///Assets/kronatl_particles_otono.png",
                    PeriodStart = new DateTime(year, 9, 1, 0, 0, 0),
                    PeriodEnd = new DateTime(year, 11, 30, 23, 59, 59),
                    ReleaseDate = new DateTime(year, 12, 1),
                    EmblemGlyph = "\uE7C0",
                    AccentColor = "#FF6E1A",
                    SecondaryAccentColor = "#9C27B0",
                    TertiaryAccentColor = "#00E676",
                    OpenCodexBackground = "#FF6E1A",
                    OpenCodexForeground = "#000000",
                    CardBorderBrush = "#4A261C",
                    CardBackground = "#17151A",
                    BackgroundGradientStart = "#451A14",
                    BackgroundGradientEnd = "#1C0E2E",
                    PaletteDescription = "Naranjas, Verdes y Morados"
                },

                _ => new KronatlSeason
                {
                    Id = $"anual_{year}",
                    Type = KronatlSeasonType.Anual,
                    Year = year,
                    SeasonName = "Anual",
                    Title = $"Kronatl {year}",
                    Subtitle = year == 2026 ? $"El Año de Nacimiento de Kaki EQ ({year})" : $"Gran Recap Global Anual {year}",
                    LoreDescription = year == 2026
                        ? "El gran resumen del año en que nació Kaki EQ: el mapa sonoro de todas tus pasiones acústicas, himnos y artistas refugio."
                        : $"La constelación acústica completa de tu año {year}: cada estación, cada himno y cada hora de alta fidelidad.",
                    SubtitleQuote = "Frecuencias festivas, la emoción de un nuevo comienzo y transiciones brillantes que despiden el año con alegría.",
                    SeasonalBadgeText = "CELEBRACIÓN · ANUAL",
                    LoreBadgeText = variant == 1 ? "Constelación Sonora" : (variant == 2 ? "Legado Acústico" : "Fuegos y Deseos"),
                    EmblemImagePath = "ms-appx:///Assets/kronatl_emblem_anual.png",
                    ParticlesImagePath = "ms-appx:///Assets/kronatl_particles_anual.png",
                    PeriodStart = new DateTime(year, 1, 1, 0, 0, 0),
                    PeriodEnd = new DateTime(year, 12, 31, 23, 59, 59),
                    ReleaseDate = new DateTime(year, 12, 31),
                    EmblemGlyph = "\uE823",
                    AccentColor = "#E5A93C",
                    SecondaryAccentColor = "#D0BCFF",
                    TertiaryAccentColor = "#00E676",
                    OpenCodexBackground = "#E5A93C",
                    OpenCodexForeground = "#000000",
                    CardBorderBrush = "#453316",
                    CardBackground = "#141318",
                    BackgroundGradientStart = "#331D42",
                    BackgroundGradientEnd = "#2E1E10",
                    PaletteDescription = "Dorado Imperial y Morado Kaki"
                }
            };
        }

        private sealed class TrackLookup
        {
            public Dictionary<string, Track> ByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> LibraryOrderByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Track> ByTitleArtist { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<int, Track> ById { get; } = new();
        }

        private sealed class RankedTrackEntry
        {
            public string Key { get; init; } = string.Empty;
            public Track? Track { get; init; }
            public PlaybackSessionLog SampleLog { get; init; } = new();
            public int PlayCount { get; init; }
            public double TotalSeconds { get; init; }
            public int LibraryOrder { get; init; }
            public DateTime LatestRealOrAnyTimestamp { get; init; }
        }

        private static string GetCanonicalTrackKey(Track track)
        {
            return !string.IsNullOrWhiteSpace(track.FilePath)
                ? track.FilePath.Trim()
                : $"id:{track.Id}";
        }

        private static string GetCanonicalLogKey(PlaybackSessionLog log, Track? resolved)
        {
            if (resolved != null && !string.IsNullOrWhiteSpace(resolved.FilePath))
                return resolved.FilePath.Trim();
            if (!string.IsNullOrWhiteSpace(log.FilePath))
                return log.FilePath.Trim();
            return $"id:{log.TrackId}";
        }

        private static TrackLookup BuildTrackLookup(List<Track> allLibraryTracks)
        {
            var lookup = new TrackLookup();
            for (int i = 0; i < allLibraryTracks.Count; i++)
            {
                var trk = allLibraryTracks[i];
                if (!string.IsNullOrWhiteSpace(trk.FilePath))
                {
                    string fp = trk.FilePath.Trim();
                    lookup.ByPath.TryAdd(fp, trk);
                    lookup.LibraryOrderByPath.TryAdd(fp, i);
                }

                if (trk.Id > 0)
                {
                    lookup.ById.TryAdd(trk.Id, trk);
                }

                if (!string.IsNullOrWhiteSpace(trk.Title))
                {
                    string taKey = $"{trk.Title.Trim()}|{trk.Artist?.Trim() ?? string.Empty}";
                    lookup.ByTitleArtist.TryAdd(taKey, trk);
                }
            }
            return lookup;
        }

        private static Track? ResolveTrack(PlaybackSessionLog log, TrackLookup lookup)
        {
            if (!string.IsNullOrWhiteSpace(log.FilePath) &&
                lookup.ByPath.TryGetValue(log.FilePath.Trim(), out var byPath))
            {
                return byPath;
            }

            if (!string.IsNullOrWhiteSpace(log.Title))
            {
                string taKey = $"{log.Title.Trim()}|{log.Artist?.Trim() ?? string.Empty}";
                if (lookup.ByTitleArtist.TryGetValue(taKey, out var byTa))
                {
                    return byTa;
                }
            }

            if (string.IsNullOrWhiteSpace(log.FilePath) &&
                log.TrackId > 0 &&
                lookup.ById.TryGetValue(log.TrackId, out var byId))
            {
                return byId;
            }

            return null;
        }

        private static double ResolveLogSeconds(PlaybackSessionLog log, Track? resolved)
        {
            if (resolved != null && resolved.Duration.TotalSeconds > 0)
                return resolved.Duration.TotalSeconds;
            if (log.ListenedSeconds > 0)
                return log.ListenedSeconds;
            if (log.DurationSeconds > 0)
                return log.DurationSeconds;
            return 180.0;
        }

        private static List<RankedTrackEntry> BuildRankedSeasonTracks(List<PlaybackSessionLog> periodLogs, TrackLookup lookup)
        {
            var grouped = new Dictionary<string, List<(PlaybackSessionLog log, Track? resolved)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var log in periodLogs)
            {
                var resolved = ResolveTrack(log, lookup);
                string key = GetCanonicalLogKey(log, resolved);
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<(PlaybackSessionLog, Track?)>();
                    grouped[key] = list;
                }
                list.Add((log, resolved));
            }

            var ranked = new List<RankedTrackEntry>(grouped.Count);
            foreach (var kvp in grouped)
            {
                var entries = kvp.Value;
                var resolvedTrack = entries.Select(x => x.resolved).FirstOrDefault(t => t != null);
                var sampleLog = entries[0].log;
                double totalSecs = entries.Sum(x => ResolveLogSeconds(x.log, x.resolved));
                int libOrder = (resolvedTrack != null && !string.IsNullOrWhiteSpace(resolvedTrack.FilePath) &&
                                lookup.LibraryOrderByPath.TryGetValue(resolvedTrack.FilePath.Trim(), out int idx))
                    ? idx
                    : int.MaxValue;

                var latestReal = entries.Where(x => !x.log.IsBackfilled).Select(x => x.log.Timestamp).DefaultIfEmpty().Max();
                DateTime latestTs = latestReal != default
                    ? latestReal
                    : entries.Max(x => x.log.Timestamp);

                ranked.Add(new RankedTrackEntry
                {
                    Key = kvp.Key,
                    Track = resolvedTrack,
                    SampleLog = sampleLog,
                    PlayCount = entries.Count,
                    TotalSeconds = totalSecs,
                    LibraryOrder = libOrder,
                    LatestRealOrAnyTimestamp = latestTs
                });
            }

            return ranked
                .OrderByDescending(r => r.PlayCount)
                .ThenBy(r => r.LibraryOrder)
                .ThenByDescending(r => r.TotalSeconds)
                .ThenBy(r => r.Track?.Title ?? r.SampleLog.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<KronatlSeason> GetDefinedSeasons()
        {
            var now = DateTime.Now;
            int currentYear = now.Year;

            var allCandidates = new List<KronatlSeason>();
            for (int y = 2026; y <= Math.Max(2026, currentYear) + 1; y++)
            {
                allCandidates.Add(CreateSeasonDefinition(KronatlSeasonType.Invierno, y));
                allCandidates.Add(CreateSeasonDefinition(KronatlSeasonType.Primavera, y));
                allCandidates.Add(CreateSeasonDefinition(KronatlSeasonType.Verano, y));
                allCandidates.Add(CreateSeasonDefinition(KronatlSeasonType.Otoño, y));
                allCandidates.Add(CreateSeasonDefinition(KronatlSeasonType.Anual, y));
            }

            // Only surface seasons that existed after Kaki EQ's inception (Sept 2026)
            // and have already started (or the current year's Anual summary).
            var activeSeasons = allCandidates
                .Where(s => s.PeriodEnd >= InceptionDate && (s.PeriodStart <= now || (s.Type == KronatlSeasonType.Anual && s.Year == currentYear)))
                .OrderByDescending(s => s.Type != KronatlSeasonType.Anual && now >= s.PeriodStart && now <= s.PeriodEnd)
                .ThenByDescending(s => s.PeriodStart)
                .ThenBy(s => s.Type == KronatlSeasonType.Anual ? 1 : 0)
                .ToList();

            return activeSeasons;
        }

        public async Task<KronatlStats> CalculateSeasonStatsAsync(KronatlSeason season, List<Track> allLibraryTracks)
        {
            if (!_hasReconciledThisSession && allLibraryTracks.Count > 0)
            {
                await EnsureHistorySeededAsync(allLibraryTracks);
            }

            var logs = await GetAllPlaybackLogsAsync();
            var periodLogs = logs
                .Where(l => l.Timestamp >= season.PeriodStart && l.Timestamp <= season.PeriodEnd)
                .ToList();

            var stats = new KronatlStats();
            if (periodLogs.Count == 0)
            {
                season.Stats = stats;
                return stats;
            }

            var lookup = BuildTrackLookup(allLibraryTracks);
            var rankedTracks = BuildRankedSeasonTracks(periodLogs, lookup);

            // 1. Tiempo Total de Vibración
            double totalSeconds = periodLogs.Sum(l => ResolveLogSeconds(l, ResolveTrack(l, lookup)));
            stats.TotalListeningHours = totalSeconds / 3600.0;
            stats.TotalListeningMinutes = (int)Math.Round(totalSeconds / 60.0);

            // 2. Pista del Alma (Top 1 Track — strictly synchronized with rankedTracks[0])
            var top1 = rankedTracks.FirstOrDefault();
            if (top1 != null)
            {
                stats.TopTrack = top1.Track ?? new Track
                {
                    Id = top1.SampleLog.TrackId,
                    Title = top1.SampleLog.Title,
                    Artist = top1.SampleLog.Artist,
                    Album = top1.SampleLog.Album,
                    FilePath = top1.SampleLog.FilePath,
                    CoverImagePath = top1.SampleLog.CoverImagePath,
                    Bpm = top1.SampleLog.Bpm,
                    CamelotKey = top1.SampleLog.CamelotKey
                };
                stats.TopTrackPlayCount = top1.PlayCount;
                stats.TopTrackPeakTime = top1.LatestRealOrAnyTimestamp != default
                    ? top1.LatestRealOrAnyTimestamp.ToString("dddd, h:mm tt", new CultureInfo("es-ES"))
                    : "—";
            }

            string? ResolveArtistImage(string artistName)
            {
                if (string.IsNullOrWhiteSpace(artistName)) return null;
                var customPhoto = _databaseService.GetArtistPhotoPath(artistName);
                if (!string.IsNullOrEmpty(customPhoto) && File.Exists(customPhoto)) return customPhoto;

                var trackWithCover = allLibraryTracks.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.CoverImagePath) &&
                    File.Exists(t.CoverImagePath) &&
                    ArtistHelper.TrackHasArtist(t, artistName));

                if (trackWithCover != null) return trackWithCover.CoverImagePath;

                var logWithCover = periodLogs.FirstOrDefault(l =>
                    !string.IsNullOrEmpty(l.CoverImagePath) &&
                    File.Exists(l.CoverImagePath) &&
                    !string.IsNullOrWhiteSpace(l.Artist) &&
                    ArtistHelper.SplitArtists(l.Artist).Any(a => a.Equals(artistName, StringComparison.OrdinalIgnoreCase)));

                return logWithCover?.CoverImagePath;
            }

            string? ResolveTrackCover(Track? trk, PlaybackSessionLog sampleLog)
            {
                if (trk != null && !string.IsNullOrEmpty(trk.CoverImagePath) && File.Exists(trk.CoverImagePath))
                    return trk.CoverImagePath;
                if (!string.IsNullOrEmpty(sampleLog.CoverImagePath) && File.Exists(sampleLog.CoverImagePath))
                    return sampleLog.CoverImagePath;
                if (!string.IsNullOrWhiteSpace(sampleLog.Title))
                {
                    var byTitle = allLibraryTracks.FirstOrDefault(t =>
                        !string.IsNullOrEmpty(t.CoverImagePath) &&
                        File.Exists(t.CoverImagePath) &&
                        string.Equals(t.Title, sampleLog.Title, StringComparison.OrdinalIgnoreCase));
                    if (byTitle != null) return byTitle.CoverImagePath;
                }
                return null;
            }

            // 3. Artista Refugio & Top 5 Artistas (separating collaborative artists consistently with ArtistsViewModel)
            var artistAgg = new Dictionary<string, (double seconds, int plays, Dictionary<string, int> songCounts)>(StringComparer.OrdinalIgnoreCase);

            foreach (var log in periodLogs)
            {
                var resolved = ResolveTrack(log, lookup);
                string rawArtist = resolved != null && !string.IsNullOrWhiteSpace(resolved.Artist) ? resolved.Artist : log.Artist;
                if (string.IsNullOrWhiteSpace(rawArtist)) continue;

                var splitArtists = ArtistHelper.SplitArtists(rawArtist)
                    .Where(a => !string.IsNullOrWhiteSpace(a) &&
                                !a.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) &&
                                !a.Equals("Desconocido", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (splitArtists.Count == 0) continue;

                double secs = ResolveLogSeconds(log, resolved);
                string songTitle = resolved?.Title ?? log.Title;

                foreach (var artistName in splitArtists)
                {
                    if (!artistAgg.TryGetValue(artistName, out var cur))
                    {
                        cur = (0.0, 0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                    }
                    cur.seconds += secs;
                    cur.plays++;
                    if (!string.IsNullOrWhiteSpace(songTitle))
                    {
                        cur.songCounts[songTitle] = cur.songCounts.TryGetValue(songTitle, out int c) ? c + 1 : 1;
                    }
                    artistAgg[artistName] = cur;
                }
            }

            var orderedArtists = artistAgg
                .OrderByDescending(kvp => kvp.Value.seconds)
                .ThenByDescending(kvp => kvp.Value.plays)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orderedArtists.Count > 0)
            {
                var topArt = orderedArtists[0];
                stats.TopArtist = topArt.Key;
                stats.TopArtistHours = topArt.Value.seconds / 3600.0;
                stats.TopArtistMinutes = (int)Math.Round(topArt.Value.seconds / 60.0);
                stats.TopArtistPhotoPath = ResolveArtistImage(topArt.Key);
                stats.TopArtistTopSongs = topArt.Value.songCounts
                    .OrderByDescending(x => x.Value)
                    .Select(x => x.Key)
                    .Take(3)
                    .ToList();
            }
            else if (stats.TopTrack != null)
            {
                stats.TopArtist = stats.TopTrack.Artist;
                stats.TopArtistHours = stats.TotalListeningHours;
                stats.TopArtistMinutes = stats.TotalListeningMinutes;
                stats.TopArtistPhotoPath = ResolveArtistImage(stats.TopArtist);
                stats.TopArtistTopSongs = new List<string> { stats.TopTrack.Title };
            }

            int artistRank = 1;
            foreach (var art in orderedArtists.Take(5))
            {
                double secs = art.Value.seconds;
                string timeStr = secs >= 3600
                    ? $"{(int)(secs / 3600)}h {((int)Math.Round(secs / 60.0)) % 60}m"
                    : $"{Math.Max(1, (int)Math.Round(secs / 60.0))}m";

                stats.Top5Artists.Add(new TopArtistItem
                {
                    Rank = artistRank++,
                    Name = art.Key,
                    TimeFormatted = timeStr,
                    PhotoPath = ResolveArtistImage(art.Key)
                });
            }

            // 4. Espectro Armónico Predominante (Camelot Keys from live Track data)
            var camelotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var bpmValues = new List<double>();

            foreach (var log in periodLogs)
            {
                var resolved = ResolveTrack(log, lookup);
                string rawKey = resolved != null && !string.IsNullOrWhiteSpace(resolved.CamelotKey)
                    ? resolved.CamelotKey
                    : log.CamelotKey;
                string normKey = CamelotColorConverter.NormalizeCamelotKey(rawKey);
                if (!string.IsNullOrEmpty(normKey))
                {
                    camelotCounts[normKey] = camelotCounts.TryGetValue(normKey, out int kc) ? kc + 1 : 1;
                }

                double bpm = (resolved != null && resolved.Bpm > 40 && resolved.Bpm < 240)
                    ? resolved.Bpm
                    : log.Bpm;
                if (bpm > 40 && bpm < 240)
                {
                    bpmValues.Add(bpm);
                }
            }

            var orderedKeys = camelotCounts.OrderByDescending(k => k.Value).ToList();
            if (orderedKeys.Count > 0)
            {
                stats.DominantCamelotKey = orderedKeys[0].Key;
                stats.SecondaryCamelotKey = orderedKeys.Count > 1 ? orderedKeys[1].Key : string.Empty;
            }
            else if (stats.TopTrack != null && !string.IsNullOrEmpty(stats.TopTrack.CamelotKey))
            {
                stats.DominantCamelotKey = CamelotColorConverter.NormalizeCamelotKey(stats.TopTrack.CamelotKey);
                stats.SecondaryCamelotKey = string.Empty;
            }
            else
            {
                stats.DominantCamelotKey = "8A";
                stats.SecondaryCamelotKey = string.Empty;
            }

            stats.CamelotMoodDescription = stats.DominantCamelotKey.EndsWith("B", StringComparison.OrdinalIgnoreCase)
                ? "Tonalidades mayores luminosas, abiertas y con gran impulso melódico."
                : "Tonalidades menores profundas, introspectivas y de gran carga emotiva.";

            // 5. Pulso Cardinal (Average BPM)
            stats.AverageBpm = bpmValues.Count > 0 ? (int)Math.Round(bpmValues.Average()) : 120;

            // 6. Pico de Obsidiana (Prefer real tracked days when available so historical backfills don't create synthetic peak days)
            var realPeriodLogs = periodLogs.Where(l => !l.IsBackfilled).ToList();
            var logsForPeakDay = realPeriodLogs.Count >= 5 ? realPeriodLogs : periodLogs;

            var dayGroup = logsForPeakDay
                .GroupBy(l => l.Timestamp.Date)
                .OrderByDescending(g => g.Sum(x => ResolveLogSeconds(x, ResolveTrack(x, lookup))))
                .FirstOrDefault();

            if (dayGroup != null)
            {
                stats.ObsidianPeakDate = dayGroup.Key;
                stats.ObsidianPeakHours = dayGroup.Sum(x => ResolveLogSeconds(x, ResolveTrack(x, lookup))) / 3600.0;
            }

            // 7. Álbum de Escucha Pura (using AlbumHelper clustering so collabs stay unified)
            var albumItems = AlbumHelper.BuildAlbumItems(allLibraryTracks);
            var albumByTrackPath = new Dictionary<string, AlbumItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var alb in albumItems)
            {
                foreach (var t in alb.Tracks)
                {
                    if (!string.IsNullOrWhiteSpace(t.FilePath))
                    {
                        albumByTrackPath.TryAdd(t.FilePath.Trim(), alb);
                    }
                }
            }

            var albumAgg = new Dictionary<AlbumItem, (int plays, double seconds)>();
            var fallbackAlbumAgg = new Dictionary<string, (string title, string artist, string? cover, int plays, double seconds)>(StringComparer.Ordinal);

            foreach (var log in periodLogs)
            {
                var resolved = ResolveTrack(log, lookup);
                double secs = ResolveLogSeconds(log, resolved);
                string? fp = resolved?.FilePath ?? log.FilePath;

                if (!string.IsNullOrWhiteSpace(fp) && albumByTrackPath.TryGetValue(fp.Trim(), out var albItem))
                {
                    if (albItem.Title.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase)) continue;
                    (int plays, double seconds) cur = albumAgg.TryGetValue(albItem, out var existingCur) ? existingCur : (0, 0.0);
                    albumAgg[albItem] = (cur.plays + 1, cur.seconds + secs);
                }
                else
                {
                    string albTitle = resolved?.Album ?? log.Album;
                    if (string.IsNullOrWhiteSpace(albTitle) || albTitle.Equals("Unknown Album", StringComparison.OrdinalIgnoreCase)) continue;
                    string albArtist = !string.IsNullOrWhiteSpace(resolved?.AlbumArtist) ? resolved!.AlbumArtist : (resolved?.Artist ?? log.Artist);
                    string key = $"{albTitle.Trim()}|{albArtist.Trim()}";
                    (string title, string artist, string? cover, int plays, double seconds) cur = fallbackAlbumAgg.TryGetValue(key, out var existingFb)
                        ? existingFb
                        : (albTitle.Trim(), albArtist.Trim(), ResolveTrackCover(resolved, log), 0, 0.0);
                    fallbackAlbumAgg[key] = (cur.title, cur.artist, cur.cover ?? ResolveTrackCover(resolved, log), cur.plays + 1, cur.seconds + secs);
                }
            }

            if (albumAgg.Count > 0)
            {
                var bestAlbum = albumAgg.OrderByDescending(x => x.Value.plays).ThenByDescending(x => x.Value.seconds).First();
                stats.PureAlbumTitle = bestAlbum.Key.Title;
                stats.PureAlbumArtist = bestAlbum.Key.Artist;
                stats.PureAlbumCoverPath = bestAlbum.Key.CoverImagePath;
                stats.PureAlbumPlaysCount = bestAlbum.Value.plays;
            }
            else if (fallbackAlbumAgg.Count > 0)
            {
                var bestFb = fallbackAlbumAgg.Values.OrderByDescending(x => x.plays).ThenByDescending(x => x.seconds).First();
                stats.PureAlbumTitle = bestFb.title;
                stats.PureAlbumArtist = bestFb.artist;
                stats.PureAlbumCoverPath = bestFb.cover;
                stats.PureAlbumPlaysCount = bestFb.plays;
            }
            else if (stats.TopTrack != null)
            {
                stats.PureAlbumTitle = stats.TopTrack.Album;
                stats.PureAlbumArtist = !string.IsNullOrWhiteSpace(stats.TopTrack.AlbumArtist) ? stats.TopTrack.AlbumArtist : stats.TopTrack.Artist;
                stats.PureAlbumCoverPath = stats.TopTrack.CoverImagePath;
                stats.PureAlbumPlaysCount = stats.TopTrackPlayCount;
            }

            // 8. Índice de Fidelidad Auditiva (% Lossless vs Lossy) & Format Breakdown
            int losslessCount = 0;
            var formatCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var log in periodLogs)
            {
                var resolved = ResolveTrack(log, lookup);
                string fmt = (resolved != null && !string.IsNullOrWhiteSpace(resolved.AudioFormat))
                    ? resolved.AudioFormat.ToUpperInvariant()
                    : (!string.IsNullOrWhiteSpace(log.AudioFormat) ? log.AudioFormat.ToUpperInvariant() : "AUDIO");
                int bits = (resolved != null && resolved.BitsPerSample > 0) ? resolved.BitsPerSample : log.BitsPerSample;

                formatCounts[fmt] = formatCounts.TryGetValue(fmt, out int fc) ? fc + 1 : 1;

                if (bits >= 24 || fmt is "FLAC" or "WAV" or "ALAC" or "AIFF" or "APE" or "WV" or "DSF" or "DFF")
                {
                    losslessCount++;
                }
            }

            int totalCount = Math.Max(1, periodLogs.Count);
            stats.LosslessPercentage = Math.Clamp((int)Math.Round(((double)losslessCount / totalCount) * 100.0), 0, 100);
            var topFormats = formatCounts.OrderByDescending(f => f.Value).Take(3).Select(f => f.Key).ToList();
            stats.TechnicalFormatBreakdown = topFormats.Count > 0
                ? $"{string.Join(" · ", topFormats)} · Motor BASS 32-bit"
                : "PCM Directo · Motor BASS 32-bit";

            // 9. Top 5 Tracks (strictly taken from rankedTracks so Slide 2, Slide 4, Slide 7, and Top 30 match 1-to-1)
            int trackRank = 1;
            foreach (var entry in rankedTracks.Take(5))
            {
                stats.Top5Tracks.Add(new TopTrackItem
                {
                    Rank = trackRank++,
                    Title = entry.Track?.Title ?? entry.SampleLog.Title,
                    Artist = entry.Track?.Artist ?? entry.SampleLog.Artist,
                    CoverPath = ResolveTrackCover(entry.Track, entry.SampleLog),
                    PlayCount = entry.PlayCount
                });
            }

            season.Stats = stats;
            return stats;
        }

        public async Task<List<Track>> GetSeasonTop30TracksAsync(KronatlSeason season, List<Track> allLibraryTracks)
        {
            if (!_hasReconciledThisSession && allLibraryTracks.Count > 0)
            {
                await EnsureHistorySeededAsync(allLibraryTracks);
            }

            var logs = await GetAllPlaybackLogsAsync();
            var periodLogs = logs
                .Where(l => l.Timestamp >= season.PeriodStart && l.Timestamp <= season.PeriodEnd)
                .ToList();

            var lookup = BuildTrackLookup(allLibraryTracks);
            var rankedTracks = BuildRankedSeasonTracks(periodLogs, lookup);

            var result = rankedTracks
                .Where(r => r.Track != null)
                .Select(r => r.Track!)
                .Take(30)
                .ToList();

            season.TopTracks = result;
            return result;
        }

        private async Task<bool> UpdateSavedPlaylistFromTop30Async(Playlist existing, KronatlSeason season, List<Track> top30)
        {
            var newPaths = top30.Select(t => t.FilePath).Where(f => !string.IsNullOrEmpty(f)).ToList();
            var newIds = top30.Select(t => t.Id).ToList();
            string? newCover = top30.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath;
            string newDesc = $"Las 30 canciones más escuchadas durante {season.Title}. Sincronizada dinámicamente por Kronatl.";

            bool pathsDiffer = existing.TrackFilePaths == null ||
                               existing.TrackFilePaths.Count != newPaths.Count ||
                               !existing.TrackFilePaths.SequenceEqual(newPaths, StringComparer.OrdinalIgnoreCase);
            bool idsDiffer = existing.TrackIds == null ||
                             existing.TrackIds.Count != newIds.Count ||
                             !existing.TrackIds.SequenceEqual(newIds);
            bool coverDiffers = !string.Equals(existing.CoverImagePath, newCover, StringComparison.OrdinalIgnoreCase);

            if (!pathsDiffer && !idsDiffer && !coverDiffers && existing.TrackCount == top30.Count)
            {
                return false;
            }

            existing.Description = newDesc;
            existing.TrackIds = newIds;
            existing.TrackFilePaths = newPaths;
            existing.TrackCount = top30.Count;
            if (!string.IsNullOrEmpty(newCover))
            {
                existing.CoverImagePath = newCover;
            }

            await _databaseService.UpdatePlaylistAsync(existing);
            return true;
        }

        public async Task<bool> SyncSavedKronatlPlaylistsAsync(List<Track> allLibraryTracks, string? specificPlaylistName = null)
        {
            if (allLibraryTracks == null || allLibraryTracks.Count == 0) return false;

            var playlists = await _databaseService.GetAllPlaylistsAsync();
            var kronatlPlaylists = playlists
                .Where(p => p.Name.StartsWith("Kronatl:", StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrEmpty(specificPlaylistName) || p.Name.Equals(specificPlaylistName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (kronatlPlaylists.Count == 0) return false;

            var seasons = GetDefinedSeasons();
            bool anyUpdated = false;

            foreach (var pl in kronatlPlaylists)
            {
                var season = seasons.FirstOrDefault(s => s.PlaylistName.Equals(pl.Name, StringComparison.OrdinalIgnoreCase));
                if (season == null) continue;

                var top30 = await GetSeasonTop30TracksAsync(season, allLibraryTracks);
                if (top30.Count > 0 && await UpdateSavedPlaylistFromTop30Async(pl, season, top30))
                {
                    anyUpdated = true;
                }
            }

            return anyUpdated;
        }

        public async Task<Playlist> GenerateOrGetTop30PlaylistAsync(KronatlSeason season, List<Track> allLibraryTracks)
        {
            var playlists = await _databaseService.GetAllPlaylistsAsync();
            var existing = playlists.FirstOrDefault(p => p.Name.Equals(season.PlaylistName, StringComparison.OrdinalIgnoreCase));

            var top30 = await GetSeasonTop30TracksAsync(season, allLibraryTracks);
            season.TopTracks = top30;

            if (existing != null)
            {
                if (top30.Count > 0)
                {
                    await UpdateSavedPlaylistFromTop30Async(existing, season, top30);
                }
                season.PlaylistId = existing.Id;
                season.IsPlaylistSavedInLibrary = true;
                return existing;
            }

            var transientPlaylist = new Playlist
            {
                Name = season.PlaylistName,
                Description = $"Las 30 canciones más escuchadas durante {season.Title}. Sincronizada dinámicamente por Kronatl.",
                TrackCount = top30.Count,
                TrackIds = top30.Select(t => t.Id).ToList(),
                TrackFilePaths = top30.Select(t => t.FilePath).Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CoverImagePath = top30.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath,
                CreatedAt = DateTime.Now
            };

            season.IsPlaylistSavedInLibrary = false;
            return transientPlaylist;
        }

        public async Task<Playlist> SaveTop30PlaylistToLibraryAsync(KronatlSeason season, List<Track> allLibraryTracks)
        {
            var top30 = await GetSeasonTop30TracksAsync(season, allLibraryTracks);
            var playlists = await _databaseService.GetAllPlaylistsAsync();
            var existing = playlists.FirstOrDefault(p => p.Name.Equals(season.PlaylistName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (top30.Count > 0)
                {
                    await UpdateSavedPlaylistFromTop30Async(existing, season, top30);
                }
                season.PlaylistId = existing.Id;
                season.IsPlaylistSavedInLibrary = true;
                return existing;
            }

            var newPlaylist = new Playlist
            {
                Name = season.PlaylistName,
                Description = $"Las 30 canciones más escuchadas durante {season.Title}. Sincronizada dinámicamente por Kronatl.",
                TrackCount = top30.Count,
                TrackIds = top30.Select(t => t.Id).ToList(),
                TrackFilePaths = top30.Select(t => t.FilePath).Where(f => !string.IsNullOrEmpty(f)).ToList(),
                CoverImagePath = top30.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverImagePath))?.CoverImagePath,
                CreatedAt = DateTime.Now
            };

            await _databaseService.AddPlaylistAsync(newPlaylist);
            season.PlaylistId = newPlaylist.Id;
            season.IsPlaylistSavedInLibrary = true;
            return newPlaylist;
        }

        public async Task<List<KronatlSeason>> GetAllSeasonsAsync(List<Track> allLibraryTracks)
        {
            await EnsureHistorySeededAsync(allLibraryTracks);
            var seasons = GetDefinedSeasons();
            foreach (var s in seasons)
            {
                await CalculateSeasonStatsAsync(s, allLibraryTracks);
                await GenerateOrGetTop30PlaylistAsync(s, allLibraryTracks);
            }
            return seasons;
        }

        public async Task<KronatlSeason?> GetActiveNotificationSeasonAsync(List<Track> allLibraryTracks)
        {
            await EnsureHistorySeededAsync(allLibraryTracks);
            var seasons = GetDefinedSeasons();
            var now = DateTime.Now;

            var active = seasons
                .FirstOrDefault(s => s.Type != KronatlSeasonType.Anual && now >= s.PeriodStart && now <= s.PeriodEnd)
                ?? seasons.FirstOrDefault(s => s.Type != KronatlSeasonType.Anual)
                ?? seasons.FirstOrDefault();

            if (active != null)
            {
                await CalculateSeasonStatsAsync(active, allLibraryTracks);
                await GenerateOrGetTop30PlaylistAsync(active, allLibraryTracks);
            }

            return active;
        }
    }
}
