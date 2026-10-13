namespace KakiEQ_WinUI.Models
{
    public class ArtistItem
    {
        public string Name { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public int TrackCount { get; set; }
        public int AlbumCount { get; set; }

        public string TrackCountFormatted => $"{TrackCount} canciones";
    }
}

