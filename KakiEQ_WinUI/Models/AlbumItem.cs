using Microsoft.UI.Xaml.Media;

namespace KakiEQ_WinUI.Models
{
    public class AlbumItem
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public SolidColorBrush? DominantColorBrush { get; set; }
    }
}

