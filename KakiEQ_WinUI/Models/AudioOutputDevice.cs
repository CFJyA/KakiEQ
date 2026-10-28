using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

        // Separate stream handles for Local Player vs System-Wide Routing to prevent sample-rate & buffer collisions
        public int PushStreamHandle { get; set; } = 0;
        public int LocalStreamSampleRate { get; set; } = 0;
        public int LocalStreamChannels { get; set; } = 0;
        public bool LocalStreamIsFloat { get; set; } = true;

        public int RoutingPushStreamHandle { get; set; } = 0;
        public int RoutingStreamSampleRate { get; set; } = 0;
        public int RoutingStreamChannels { get; set; } = 0;
        public bool RoutingStreamIsFloat { get; set; } = true;

        // Per-device circular delay buffers for sample-accurate latency compensation (0..300 ms)
        internal readonly AudioDelayRingBuffer LocalDelayBuffer = new();
        internal readonly AudioDelayRingBuffer RoutingDelayBuffer = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Zero-allocation circular delay line for per-device latency compensation (0 to 300 ms).
    /// </summary>
    internal sealed class AudioDelayRingBuffer
    {
        private byte[] _ring = Array.Empty<byte>();
        private byte[] _scratch = Array.Empty<byte>();
        private int _writePos = 0;
        private int _delayBytes = 0;
        private readonly object _sync = new();

        public void Configure(int sampleRate, int channels, int bytesPerSample, int delayMs)
        {
            lock (_sync)
            {
                if (delayMs <= 0 || sampleRate <= 0 || channels <= 0 || bytesPerSample <= 0)
                {
                    _delayBytes = 0;
                    _writePos = 0;
                    return;
                }

                int frameAlign = channels * bytesPerSample;
                int rawBytes = (int)((long)sampleRate * frameAlign * delayMs / 1000L);
                int alignedBytes = (rawBytes / frameAlign) * frameAlign;

                if (alignedBytes <= 0)
                {
                    _delayBytes = 0;
                    _writePos = 0;
                    return;
                }

                if (_ring.Length < alignedBytes)
                {
                    _ring = new byte[alignedBytes];
                }
                else if (alignedBytes != _delayBytes)
                {
                    Array.Clear(_ring, 0, alignedBytes);
                }

                _delayBytes = alignedBytes;
                if (_writePos >= _delayBytes)
                {
                    _writePos = 0;
                }
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                if (_delayBytes > 0 && _ring.Length >= _delayBytes)
                {
                    Array.Clear(_ring, 0, _delayBytes);
                }
                _writePos = 0;
            }
        }

        /// <summary>
        /// Processes input audio through the delay ring buffer and returns a pointer to the delayed output block.
        /// If delay is 0, returns the original input pointer directly with zero copies.
        /// </summary>
        public unsafe IntPtr ProcessOutOfPlace(IntPtr input, int length)
        {
            if (_delayBytes <= 0 || length <= 0) return input;

            lock (_sync)
            {
                if (_delayBytes <= 0) return input;

                if (_scratch.Length < length)
                {
                    _scratch = new byte[Math.Max(length * 2, 16384)];
                }

                byte* src = (byte*)input;
                fixed (byte* dst = _scratch)
                fixed (byte* ring = _ring)
                {
                    int remaining = length;
                    int offset = 0;

                    while (remaining > 0)
                    {
                        int chunk = Math.Min(remaining, _delayBytes - _writePos);
                        Buffer.MemoryCopy(ring + _writePos, dst + offset, chunk, chunk);
                        Buffer.MemoryCopy(src + offset, ring + _writePos, chunk, chunk);

                        _writePos += chunk;
                        if (_writePos >= _delayBytes) _writePos = 0;

                        offset += chunk;
                        remaining -= chunk;
                    }
                }

                return Marshal.UnsafeAddrOfPinnedArrayElement(_scratch, 0);
            }
        }

        /// <summary>
        /// Processes audio in-place (used when delaying the Primary stream directly in its DSP callback).
        /// </summary>
        public unsafe void ProcessInPlace(IntPtr buffer, int length)
        {
            if (_delayBytes <= 0 || length <= 0) return;

            lock (_sync)
            {
                if (_delayBytes <= 0) return;

                byte* ptr = (byte*)buffer;
                fixed (byte* ring = _ring)
                {
                    for (int i = 0; i < length; i++)
                    {
                        byte delayed = ring[_writePos];
                        ring[_writePos] = ptr[i];
                        ptr[i] = delayed;

                        _writePos++;
                        if (_writePos >= _delayBytes) _writePos = 0;
                    }
                }
            }
        }
    }
}
