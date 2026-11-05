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

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
