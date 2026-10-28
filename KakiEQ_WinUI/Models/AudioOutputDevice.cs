using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KakiEQ_WinUI.Models
{
    public class AudioOutputDevice : INotifyPropertyChanged
    {
        public int DeviceIndex { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Driver { get; set; } = string.Empty;
        public string DeviceTypeDescription { get; set; } = "Dispositivo de audio";
        public string IconGlyph { get; set; } = "\uE767"; // Default Speaker
        public bool IsDefault { get; set; }
        public bool IsPrimary { get; set; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(StatusBadgeColor));
                }
            }
        }

        private float _volume = 1.0f;
        public float Volume
        {
            get => _volume;
            set
            {
                float clamped = Math.Clamp(value, 0f, 1f);
                if (Math.Abs(_volume - clamped) > 0.005f)
                {
                    _volume = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumePercentFormatted));
                    OnPropertyChanged(nameof(VolumePercentInt));
                    OnPropertyChanged(nameof(VolumeIconGlyph));
                }
            }
        }

        public int VolumePercentInt
        {
            get => (int)Math.Round(_volume * 100);
            set => Volume = value / 100f;
        }

        public string VolumePercentFormatted => $"{(int)Math.Round(_volume * 100)}%";

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumeIconGlyph));
                }
            }
        }

        public string VolumeIconGlyph
        {
            get
            {
                if (IsMuted || _volume <= 0.01f) return "\uE74F"; // Mute
                if (_volume < 0.33f) return "\uE992"; // Low
                if (_volume < 0.66f) return "\uE993"; // Med
                return "\uE995"; // High
            }
        }

        private int _latencyCompensationMs = 0;
        public int LatencyCompensationMs
        {
            get => _latencyCompensationMs;
            set
            {
                int clamped = Math.Clamp(value, 0, 300);
                if (_latencyCompensationMs != clamped)
                {
                    _latencyCompensationMs = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LatencyFormatted));
                }
            }
        }

        public string LatencyFormatted => _latencyCompensationMs == 0 ? "Sin retardo" : $"+{_latencyCompensationMs} ms";

        public string StatusBadgeText => IsEnabled ? "En reproducción" : "Inactivo";
        public string StatusBadgeColor => IsEnabled ? "#4CAF50" : "#888888";

        public int PushStreamHandle { get; set; } = 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
