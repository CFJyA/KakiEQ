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

        private string? _disc;
        public string? Disc
        {
            get => _disc;
            set
            {
                if (_disc != value)
                {
                    _disc = value;
                    OnPropertyChanged();
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
            get => Helpers.ImageHelper.EnsureDisplayableImage(_coverImagePath);
            set
            {
                if (_coverImagePath != value)
                {
                    _coverImagePath = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CoverThumbSource));
                    OnPropertyChanged(nameof(CoverCardSource));
                    OnPropertyChanged(nameof(CoverHeroSource));
                }
            }
        }

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.ImageSource? CoverThumbSource => Helpers.ImageHelper.GetCachedThumbCover(_coverImagePath);

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.ImageSource? CoverCardSource => Helpers.ImageHelper.GetCachedCardCover(_coverImagePath);

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.ImageSource? CoverHeroSource => Helpers.ImageHelper.GetCachedHeroCover(_coverImagePath);

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

        // --- Audio Technical Specifications ---
        private string _audioFormat = string.Empty;
        public string AudioFormat
        {
            get => _audioFormat;
            set
            {
                if (_audioFormat != value)
                {
                    _audioFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _bitsPerSample;
        public int BitsPerSample
        {
            get => _bitsPerSample;
            set
            {
                if (_bitsPerSample != value)
                {
                    _bitsPerSample = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _bitrate;
        public int Bitrate
        {
            get => _bitrate;
            set
            {
                if (_bitrate != value)
                {
                    _bitrate = value;
                    OnPropertyChanged();
                }
            }
        }

        private double? _baseDecibels;
        public double? BaseDecibels
        {
            get => _baseDecibels;
            set
            {
                if (_baseDecibels != value)
                {
                    _baseDecibels = value;
                    OnPropertyChanged();
                }
            }
        }

        // --- Harmonic Analysis (BPM & Camelot Key) ---
        private double _bpm;
        public double Bpm
        {
            get => _bpm;
            set
            {
                double rounded = value > 0 ? Math.Round(value) : 0;
                if (Math.Abs(_bpm - rounded) > 0.01)
                {
                    _bpm = rounded;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BpmFormatted));
                    OnPropertyChanged(nameof(HasBpmVisibility));
                }
            }
        }

        private string _camelotKey = string.Empty;
        public string CamelotKey
        {
            get => _camelotKey;
            set
            {
                string norm = Helpers.CamelotColorConverter.NormalizeCamelotKey(value);
                if (_camelotKey != norm)
                {
                    _camelotKey = norm;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CamelotKeyFormatted));
                    OnPropertyChanged(nameof(HasCamelotVisibility));
                    OnPropertyChanged(nameof(CamelotBackgroundBrush));
                    OnPropertyChanged(nameof(CamelotForegroundBrush));
                }
            }
        }

        public bool HasUnembeddedMetadata { get; set; }

        [JsonIgnore]
        public string BpmFormatted => _bpm > 0 ? $"{_bpm:0} BPM" : string.Empty;

        [JsonIgnore]
        public string CamelotKeyFormatted => Helpers.CamelotColorConverter.NormalizeCamelotKey(_camelotKey);

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility HasBpmVisibility =>
            _bpm > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility HasCamelotVisibility =>
            !string.IsNullOrEmpty(CamelotKeyFormatted) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.SolidColorBrush CamelotBackgroundBrush =>
            Helpers.CamelotColorConverter.GetBackgroundBrush(_camelotKey);

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.SolidColorBrush CamelotForegroundBrush =>
            Helpers.CamelotColorConverter.GetForegroundBrush(_camelotKey);

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

        [JsonIgnore]
        public bool IsDiscHeader { get; set; }

        [JsonIgnore]
        public string DiscHeaderTitle { get; set; } = string.Empty;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility DiscHeaderRowVisibility =>
            IsDiscHeader ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility NormalTrackRowVisibility =>
            IsDiscHeader ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

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

