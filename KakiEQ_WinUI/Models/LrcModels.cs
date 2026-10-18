using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Microsoft.UI.Xaml;

namespace KakiEQ_WinUI.Models
{
    public class LrcLine : INotifyPropertyChanged
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;

        public string TimestampFormatted
        {
            get
            {
                int min = (int)Timestamp.TotalMinutes;
                int sec = Timestamp.Seconds;
                int hundredths = Timestamp.Milliseconds / 10;
                return $"[{min:D2}:{sec:D2}.{hundredths:D2}]";
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FontWeightFormatted));
                    OnPropertyChanged(nameof(OpacityFormatted));
                    OnPropertyChanged(nameof(FontSizeFormatted));
                    OnPropertyChanged(nameof(ActiveForeground));
                    OnPropertyChanged(nameof(ActiveIndicatorVisibility));
                    OnPropertyChanged(nameof(ActiveBackgroundBrush));
                    OnPropertyChanged(nameof(ActiveBorderBrush));
                }
            }
        }

        public string FontWeightFormatted => IsActive ? "Bold" : "SemiBold";
        public double OpacityFormatted => IsActive ? 1.0 : 0.40;
        public double FontSizeFormatted => IsActive ? 28 : 20;
        public string ActiveForeground => IsActive ? "#FFFFFF" : "#CCCCCC";
        public Visibility ActiveIndicatorVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
        public string ActiveBackgroundBrush => IsActive ? "#26BB86FC" : "Transparent";
        public string ActiveBorderBrush => IsActive ? "#38BB86FC" : "Transparent";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
