using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace KakiEQ_WinUI.Models
{
    public class Track : INotifyPropertyChanged
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
        public int PlayCount { get; set; } = 0;
        public DateTime? LastPlayed { get; set; }

        private int _displayIndex;
        [JsonIgnore]
        public int DisplayIndex
        {
            get => _displayIndex;
            set
            {
                _displayIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayIndexFormatted));
            }
        }

        [JsonIgnore]
        public string DisplayIndexFormatted => DisplayIndex > 0 ? DisplayIndex.ToString() : (TrackNumber > 0 ? TrackNumber.ToString() : "•");

        public string DurationFormatted => Duration.TotalHours >= 1
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

