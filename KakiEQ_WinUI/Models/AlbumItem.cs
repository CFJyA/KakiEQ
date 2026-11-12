using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace KakiEQ_WinUI.Models
{
    public class AlbumItem : INotifyPropertyChanged
    {
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
                    OnPropertyChanged(nameof(CoverCardSource));
                    OnPropertyChanged(nameof(CoverThumbSource));
                }
            }
        }

        public ImageSource? CoverCardSource => Helpers.ImageHelper.GetCachedCardCover(_coverImagePath);
        public ImageSource? CoverThumbSource => Helpers.ImageHelper.GetCachedThumbCover(_coverImagePath);

        private SolidColorBrush? _dominantColorBrush;
        public SolidColorBrush? DominantColorBrush
        {
            get => _dominantColorBrush;
            set
            {
                if (_dominantColorBrush != value)
                {
                    _dominantColorBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        private static readonly SolidColorBrush SelectedBorderBrush =
            new(Windows.UI.Color.FromArgb(255, 187, 134, 252)); // #BB86FC
        private static readonly SolidColorBrush DefaultBorderBrush =
            new(Windows.UI.Color.FromArgb(255, 42, 42, 42));    // #2A2A2A

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardBorderBrush));
                    OnPropertyChanged(nameof(CardBorderThickness));
                }
            }
        }

        public Brush CardBorderBrush => _isSelected ? SelectedBorderBrush : DefaultBorderBrush;
        public double CardBorderThickness => _isSelected ? 2.0 : 1.15;

        private string _releaseType = "Álbum";
        public string ReleaseType
        {
            get => _releaseType;
            set
            {
                string norm = Helpers.ReleaseTypeHelper.NormalizeReleaseType(value);
                if (string.IsNullOrEmpty(norm)) norm = Helpers.ReleaseTypeHelper.TypeAlbum;
                if (_releaseType != norm)
                {
                    _releaseType = norm;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TrackCountFormatted));
                }
            }
        }

        private int _trackCount;
        public int TrackCount
        {
            get => _trackCount;
            set
            {
                if (_trackCount != value)
                {
                    _trackCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TrackCountFormatted));
                }
            }
        }

        public System.Collections.Generic.List<Track> Tracks { get; set; } = new();

        public string TrackCountFormatted => $"{TrackCount} {(TrackCount == 1 ? "canción" : "canciones")}";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
