using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KakiEQ_WinUI.Models
{
    public class KronatlStats
    {
        // 1. Tiempo Total de Vibración
        public double TotalListeningHours { get; set; }
        public int TotalListeningMinutes { get; set; }
        public string TotalListeningFormatted => TotalListeningHours >= 1
            ? $"{(int)TotalListeningHours}h {TotalListeningMinutes % 60}m"
            : $"{TotalListeningMinutes} minutos";

        // 2. Pista del Alma (Top 1 Track)
        public Track? TopTrack { get; set; }
        public string TopTrackTitle => TopTrack?.Title ?? "Música del Alma";
        public string TopTrackArtist => TopTrack?.Artist ?? "Desconocido";
        public string? TopTrackCoverPath => TopTrack?.CoverImagePath;
        public int TopTrackPlayCount { get; set; }
        public string TopTrackPeakTime { get; set; } = "Viernes, 11:42 PM";

        // 3. Artista Refugio (Top Artist)
        public string TopArtist { get; set; } = "Artista";
        public double TopArtistHours { get; set; }
        public int TopArtistMinutes { get; set; }
        public string TopArtistTimeFormatted => TopArtistHours >= 1
            ? $"{(int)TopArtistHours}h {TopArtistMinutes % 60}m"
            : $"{TopArtistMinutes}m";
        public string? TopArtistPhotoPath { get; set; }
        public List<string> TopArtistTopSongs { get; set; } = new();

        // 4. Espectro Armónico Predominante
        public string DominantCamelotKey { get; set; } = "8A";
        public string SecondaryCamelotKey { get; set; } = "9A";
        public string CamelotSpectrumFormatted => !string.IsNullOrEmpty(SecondaryCamelotKey)
            ? $"{DominantCamelotKey} / {SecondaryCamelotKey}"
            : DominantCamelotKey;
        public string CamelotMoodDescription { get; set; } = "Energía melódica y contemplativa";

        // 5. Pulso Cardinal
        public int AverageBpm { get; set; } = 120;
        public string BpmRhythmDescription => AverageBpm switch
        {
            < 90 => "Tempo calmo, introspectivo y reflexivo",
            < 115 => "Pulso sereno, fluido y armónico",
            < 130 => "Ritmo enérgico, constante y dinámico",
            _ => "Cadencia veloz, vibrante y apasionada"
        };

        // 6. Pico de Obsidiana
        public DateTime ObsidianPeakDate { get; set; }
        public string ObsidianPeakDateFormatted => ObsidianPeakDate != default
            ? ObsidianPeakDate.ToString("d 'de' MMMM, yyyy", new System.Globalization.CultureInfo("es-ES"))
            : "Día pico de escucha";
        public double ObsidianPeakHours { get; set; }
        public string ObsidianPeakHoursFormatted => $"{(int)ObsidianPeakHours}h {((int)(ObsidianPeakHours * 60)) % 60}m de flujo sonoro";

        // 7. Álbum de Escucha Pura
        public string PureAlbumTitle { get; set; } = "Álbum Completo";
        public string PureAlbumArtist { get; set; } = "Artista";
        public string? PureAlbumCoverPath { get; set; }
        public int PureAlbumPlaysCount { get; set; }

        // 8. Índice de Fidelidad Auditiva
        public int LosslessPercentage { get; set; } = 85;
        public int LossyPercentage => Math.Max(0, 100 - LosslessPercentage);
        public string FidelitySummary => $"{LosslessPercentage}% Lossless / Hi-Res (FLAC / 24-bit)";
        public string TechnicalFormatBreakdown { get; set; } = "FLAC 24-bit · WAV · PCM Directo";

        // Top 5 Lists for YouTube Music Recap style presentation
        public List<TopArtistItem> Top5Artists { get; set; } = new();
        public List<TopTrackItem> Top5Tracks { get; set; } = new();
    }

    public class TopArtistItem
    {
        public int Rank { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TimeFormatted { get; set; } = string.Empty;
        public string? PhotoPath { get; set; }

        public BitmapImage? PhotoImageSource
        {
            get
            {
                if (string.IsNullOrEmpty(PhotoPath) || !File.Exists(PhotoPath)) return null;
                try { return new BitmapImage(new Uri(PhotoPath)); }
                catch { return null; }
            }
        }

        public Visibility FallbackIconVisibility =>
            (!string.IsNullOrEmpty(PhotoPath) && File.Exists(PhotoPath)) ? Visibility.Collapsed : Visibility.Visible;
    }

    public class TopTrackItem
    {
        public int Rank { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string? CoverPath { get; set; }
        public int PlayCount { get; set; }
        public string PlayCountFormatted => $"{PlayCount} plays";
        public string PlayCountShort => $"{PlayCount}p";

        public BitmapImage? CoverImageSource
        {
            get
            {
                if (string.IsNullOrEmpty(CoverPath) || !File.Exists(CoverPath)) return null;
                try { return new BitmapImage(new Uri(CoverPath)); }
                catch { return null; }
            }
        }

        public Visibility FallbackIconVisibility =>
            (!string.IsNullOrEmpty(CoverPath) && File.Exists(CoverPath)) ? Visibility.Collapsed : Visibility.Visible;
    }
}
