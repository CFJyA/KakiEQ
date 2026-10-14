using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace KakiEQ_WinUI.Models
{
    public class EqualizerPreset : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public float[] Gains { get; set; } = new float[10];

        private bool _isCustom;
        public bool IsCustom
        {
            get => _isCustom;
            set
            {
                _isCustom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DeleteVisibility));
            }
        }

        [JsonIgnore]
        public Visibility DeleteVisibility => IsCustom ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
