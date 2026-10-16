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

        public bool CrossfadeEnabled { get; set; } = false;
        public int CrossfadeDurationSeconds { get; set; } = 3;
        private bool _crossfadeTriggeredForCurrentTrack = false;

        public event EventHandler? PlaybackPositionChanged;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? TrackEnded;
        public event EventHandler? TrackCrossfadeTriggered;

        public BassAudioEngine()
        {
            Bass.Init();
            _equalizer = new BassEqualizer();
        }

        public Task LoadFileAsync(string filePath)
        {
            return LoadFileAsync(filePath, isCrossfade: false);
        }

        public Task LoadFileAsync(string filePath, bool isCrossfade)
        {
            return Task.Run(() =>
            {
                _crossfadeTriggeredForCurrentTrack = false;

                if (isCrossfade && CrossfadeEnabled && _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing)
                {
                    int oldStream = _stream;
                    int newStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
                    if (newStream != 0)
                    {
                        // Start new stream muted and slide volume up
                        Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, 0f);
                        _equalizer.AttachToStream(newStream);
                        Bass.ChannelPlay(newStream);

                        int crossfadeMs = Math.Clamp(CrossfadeDurationSeconds, 1, 10) * 1000;
                        Bass.ChannelSlideAttribute(newStream, ChannelAttribute.Volume, _volume, crossfadeMs);
                        Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, 0f, crossfadeMs);

                        _stream = newStream;

                        // Setup auto-next on new stream completion
                        _endSyncProc = (handle, channel, data, user) =>
                        {
                            TrackEnded?.Invoke(this, EventArgs.Empty);
                        };
                        _endSyncHandle = Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _endSyncProc);

                        // Free old stream after crossfade completes
                        _ = Task.Delay(crossfadeMs + 300).ContinueWith(_ =>
                        {
                            Bass.StreamFree(oldStream);
                        });

                        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }

                // Normal (non-crossfade) loading
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

        public void CheckCrossfadeProgress()
        {
            if (!CrossfadeEnabled || _crossfadeTriggeredForCurrentTrack || _stream == 0) return;
            if (!IsPlaying) return;

            var total = TotalDuration;
            if (total.TotalSeconds <= CrossfadeDurationSeconds + 2) return;

            var current = CurrentPosition;
            if (current >= total - TimeSpan.FromSeconds(CrossfadeDurationSeconds))
            {
                _crossfadeTriggeredForCurrentTrack = true;
                TrackCrossfadeTriggered?.Invoke(this, EventArgs.Empty);
            }
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

