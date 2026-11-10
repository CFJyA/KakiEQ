using System;

namespace KakiEQ_WinUI.Models
{
    public class PlaybackSessionLog
    {
        public int TrackId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public double ListenedSeconds { get; set; }
        public string AudioFormat { get; set; } = "FLAC";
        public int BitsPerSample { get; set; } = 16;
        public int Bitrate { get; set; }
        public double Bpm { get; set; }
        public string CamelotKey { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool CompletedWithoutSkip { get; set; } = true;
    }
}
