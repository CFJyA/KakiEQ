using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KakiEQ_WinUI.Models
{
    public class ArtistItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

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

        private int _albumCount;
        public int AlbumCount
        {
            get => _albumCount;
            set
            {
                if (_albumCount != value)
                {
                    _albumCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TrackCountFormatted => $"{TrackCount} canciones";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
