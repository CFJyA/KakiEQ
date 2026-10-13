using System;
using System.Threading.Tasks;
using ManagedBass;

namespace KakiEQ_WinUI.Services.Audio
{
    public class BassAudioEngine : IAudioEngine, IDisposable
    {
        private int _stream;
        private float _volume = 1.0f;
        private readonly BassEqualizer _equalizer;
        private SyncProcedure? _endSyncProc;
        private int _endSyncHandle;

        public EqualizerBase Equalizer => _equalizer;
        public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
        public float Volume => _volume;

        public TimeSpan CurrentPosition
        {
            get
            {
                if (_stream == 0) return TimeSpan.Zero;
                long pos = Bass.ChannelGetPosition(_stream);
                if (pos < 0) return TimeSpan.Zero;
                double seconds = Bass.ChannelBytes2Seconds(_stream, pos);
                return TimeSpan.FromSeconds(Math.Max(0, seconds));
            }
        }

        public TimeSpan TotalDuration
        {
            get
            {
                if (_stream == 0) return TimeSpan.Zero;
                long len = Bass.ChannelGetLength(_stream);
                if (len < 0) return TimeSpan.Zero;
                double seconds = Bass.ChannelBytes2Seconds(_stream, len);
                return TimeSpan.FromSeconds(Math.Max(0, seconds));
            }
        }

        public event EventHandler? PlaybackPositionChanged;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? TrackEnded;

        public BassAudioEngine()
        {
            Bass.Init();
            _equalizer = new BassEqualizer();
        }

        public Task LoadFileAsync(string filePath)
        {
            return Task.Run(() =>
            {
                if (_stream != 0)
                {
                    Bass.StreamFree(_stream);
                    _stream = 0;
                }

                _stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
                if (_stream != 0)
                {
                    Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, _volume);
                    _equalizer.AttachToStream(_stream);

                    // Setup auto-next on track completion
                    _endSyncProc = (handle, channel, data, user) =>
                    {
                        TrackEnded?.Invoke(this, EventArgs.Empty);
                    };
                    _endSyncHandle = Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _endSyncProc);
                }

                PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public Task PlayAsync()
        {
            return Task.Run(() =>
            {
                if (_stream != 0)
                {
                    Bass.ChannelPlay(_stream);
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        public Task PauseAsync()
        {
            return Task.Run(() =>
            {
                if (_stream != 0)
                {
                    Bass.ChannelPause(_stream);
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        public Task StopAsync()
        {
            return Task.Run(() =>
            {
                if (_stream != 0)
                {
                    Bass.ChannelStop(_stream);
                    Bass.ChannelSetPosition(_stream, 0);
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        public void SetPosition(TimeSpan position)
        {
            if (_stream != 0)
            {
                long bytes = Bass.ChannelSeconds2Bytes(_stream, position.TotalSeconds);
                Bass.ChannelSetPosition(_stream, bytes);
                PlaybackPositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetVolume(float volume)
        {
            _volume = Math.Clamp(volume, 0f, 1f);
            if (_stream != 0)
            {
                Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, _volume);
            }
        }

        public void Dispose()
        {
            if (_stream != 0)
            {
                Bass.StreamFree(_stream);
                _stream = 0;
            }
            Bass.Free();
            GC.SuppressFinalize(this);
        }

        ~BassAudioEngine()
        {
            Dispose();
        }
    }
}

