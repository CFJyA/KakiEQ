using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace KakiEQ_WinUI.Models
{
    public class FolderTrackItem : INotifyPropertyChanged
    {
        private bool _isPlaying;
        private string _displayTitle = string.Empty;
        private string _displaySubTitle = string.Empty;

        public int Index { get; set; }
        public string FilePath { get; set; } = string.Empty;

        public string DisplayTitle
        {
            get => _displayTitle;
            set
            {
                if (_displayTitle != value)
                {
                    _displayTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplaySubTitle
        {
            get => _displaySubTitle;
            set
            {
                if (_displaySubTitle != value)
                {
                    _displaySubTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PlayingGlyphVisibility));
                    OnPropertyChanged(nameof(IndexVisibility));
                    OnPropertyChanged(nameof(TitleColorBrush));
                }
            }
        }

        public Visibility PlayingGlyphVisibility => _isPlaying ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IndexVisibility => _isPlaying ? Visibility.Collapsed : Visibility.Visible;
        public SolidColorBrush TitleColorBrush => _isPlaying
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 187, 134, 252))
            : new SolidColorBrush(Colors.White);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
