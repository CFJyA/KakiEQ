using System;
using System.Collections.Generic;

namespace KakiEQ_WinUI.Models
{
    public enum KronatlSeasonType
    {
        Invierno,   // Dic, Ene, Feb -> Liberación: 1ª semana de Marzo (Blancos, Azules y Morados)
        Primavera,  // Mar, Abr, May -> Liberación: 1ª semana de Junio (Explosión de colores)
        Verano,     // Jun, Jul, Ago -> Liberación: 1ª semana de Septiembre (Amarillo, Morado y Naranja)
        Otoño,      // Sep, Oct, Nov -> Liberación: 1ª semana de Diciembre (Naranjas, Verdes y Morados)
        Anual       // Ene a Dic     -> Liberación: 31 de Diciembre / 1ª semana de Enero (Dorado y Morado Kaki)
    }

    public class KronatlSeason
    {
        public string Id { get; set; } = string.Empty;
        public KronatlSeasonType Type { get; set; }
        public int Year { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SeasonName { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string LoreDescription { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime ReleaseDate { get; set; }
        public bool IsReleased => DateTime.Now >= ReleaseDate;

        public string StatusBadgeText
        {
            get
            {
                if (IsReleased) return "LIBERADO";
                int weeksLeft = Math.Max(1, (int)Math.Ceiling((ReleaseDate - DateTime.Now).TotalDays / 7.0));
                return $"EN CURSO · SE LIBERA EN {weeksLeft} SEMANAS ({ReleaseDate:d 'de' MMMM})";
            }
        }

        public string EmblemGlyph { get; set; } = "\uE7C0"; // Leaf / Time glyph
        public string EmblemImagePath { get; set; } = string.Empty;
        public string ParticlesImagePath { get; set; } = string.Empty;
        public string SeasonalBadgeText { get; set; } = "DÍA DE MUERTOS · OTOÑO";
        public string LoreBadgeText { get; set; } = "Cempasúchil y Ceniza";
        public string SubtitleQuote { get; set; } = string.Empty;
        public string OpenCodexBackground { get; set; } = "#FF6E1A";
        public string OpenCodexForeground { get; set; } = "#000000";
        public string CardBorderBrush { get; set; } = "#4A261C";
        public string CardBackground { get; set; } = "#17151A";

        public string AccentColor { get; set; } = "#FF7043"; // Main theme accent (e.g. orange for Autumn)
        public string SecondaryAccentColor { get; set; } = "#00C853"; // Secondary (e.g. green for Autumn)
        public string TertiaryAccentColor { get; set; } = "#BB86FC"; // Purple Kaki base
        public string BackgroundGradientStart { get; set; } = "#34161C";
        public string BackgroundGradientEnd { get; set; } = "#120E18";
        public string PaletteDescription { get; set; } = "Naranjas, Verdes y Morados";

        public string PlaylistName => Type == KronatlSeasonType.Anual
            ? $"Kronatl: Anual {Year}"
            : $"Kronatl: {SeasonName} {Year}";

        public KronatlStats Stats { get; set; } = new();
        public List<Track> TopTracks { get; set; } = new();
        public int PlaylistId { get; set; }
        public bool IsPlaylistSavedInLibrary { get; set; }
    }
}
