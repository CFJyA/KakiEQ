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

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _artist = string.Empty;
        public string Artist
        {
            get => _artist;
            set
            {
                if (_artist != value)
                {
                    _artist = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _album = string.Empty;
        public string Album
        {
            get => _album;
            set
            {
                if (_album != value)
                {
                    _album = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _genre = string.Empty;
        public string Genre
        {
            get => _genre;
            set
            {
                if (_genre != value)
                {
                    _genre = value;
                    OnPropertyChanged();
                }
            }
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DurationFormatted));
                }
            }
        }

        private int _trackNumber;
        public int TrackNumber
        {
            get => _trackNumber;
            set
            {
                if (_trackNumber != value)
                {
                    _trackNumber = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayIndexFormatted));
                }
            }
        }

        private int _year;
        public int Year
        {
            get => _year;
            set
            {
                if (_year != value)
                {
                    _year = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime DateAdded { get; set; }

        private string? _coverImagePath;
        public string? CoverImagePath
        {
            get => _coverImagePath;
            set
            {
                if (_coverImagePath != value)
                {
                    _coverImagePath = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _playCount = 0;
        public int PlayCount
        {
            get => _playCount;
            set
            {
                if (_playCount != value)
                {
                    _playCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTime? _lastPlayed;
        public DateTime? LastPlayed
        {
            get => _lastPlayed;
            set
            {
                if (_lastPlayed != value)
                {
                    _lastPlayed = value;
                    OnPropertyChanged();
                }
            }
        }

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

