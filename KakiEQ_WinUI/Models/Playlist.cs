using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace KakiEQ_WinUI.Models
{
    public class Playlist
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? CoverImagePath { get; set; }
        public string Description { get; set; } = string.Empty;
        public int TrackCount { get; set; }
        public List<int> TrackIds { get; set; } = new List<int>();
        public List<string> TrackFilePaths { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsCreateCard { get; set; }

        [JsonIgnore]
        public Visibility IsCreateCardVisibility => IsCreateCard ? Visibility.Visible : Visibility.Collapsed;

        [JsonIgnore]
        public Visibility IsNormalCardVisibility => !IsCreateCard ? Visibility.Visible : Visibility.Collapsed;

        public string TrackCountFormatted => $"{TrackCount} canciones";
    }
}

