using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KakiEQ_WinUI.Models
{
    public class EqualizerBand : INotifyPropertyChanged
    {
        public int Index { get; set; }
        public float Frequency { get; set; }
        public string Label { get; set; } = string.Empty;

        private double _gain;
        public double Gain
        {
            get => _gain;
            set
            {
                if (Math.Abs(_gain - value) > 0.01)
                {
                    _gain = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GainFormatted));
                }
            }
        }

        public string GainFormatted => $"{Gain:+0.0;-0.0;0.0} dB";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
