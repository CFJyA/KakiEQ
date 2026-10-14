using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace KakiEQ_WinUI.Models
{
    public class Playlist : INotifyPropertyChanged
    {
        private int _id;
        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private DateTime _createdAt;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        private string? _coverImagePath;
        public string? CoverImagePath
        {
            get => _coverImagePath;
            set { _coverImagePath = value; OnPropertyChanged(); }
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        private int _trackCount;
        public int TrackCount
        {
            get => _trackCount;
            set
            {
                _trackCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TrackCountFormatted));
            }
        }

        public List<int> TrackIds { get; set; } = new List<int>();
        public List<string> TrackFilePaths { get; set; } = new List<string>();

        [JsonIgnore]
        public bool IsCreateCard { get; set; }

        [JsonIgnore]
        public Visibility IsCreateCardVisibility => IsCreateCard ? Visibility.Visible : Visibility.Collapsed;

        [JsonIgnore]
        public Visibility IsNormalCardVisibility => !IsCreateCard ? Visibility.Visible : Visibility.Collapsed;

        public string TrackCountFormatted => $"{TrackCount} canciones";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

