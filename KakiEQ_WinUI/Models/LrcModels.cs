using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
                }
            }
        }

        public string FontWeightFormatted => IsActive ? "Bold" : "Normal";
        public double OpacityFormatted => IsActive ? 1.0 : 0.45;
        public double FontSizeFormatted => IsActive ? 22 : 16;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
