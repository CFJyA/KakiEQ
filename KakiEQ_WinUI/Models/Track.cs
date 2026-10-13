using System;

namespace KakiEQ_WinUI.Models
{
    public class Track
    {
        public int Id { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int TrackNumber { get; set; }
        public int Year { get; set; }
        public DateTime DateAdded { get; set; }
        public string? CoverImagePath { get; set; }

        public string DurationFormatted => Duration.TotalHours >= 1
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");
    }
}

