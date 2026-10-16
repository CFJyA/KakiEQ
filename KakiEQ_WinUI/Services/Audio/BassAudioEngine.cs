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
        
        private SyncProcedure? _crossfadeSyncProc;
        private int _crossfadeSyncHandle;

        private bool _crossfadeTriggeredForCurrentTrack = false;
        private readonly object _lock = new();

        public EqualizerBase Equalizer => _equalizer;
        public bool IsPlaying => _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
        public float Volume => _volume;

        public TimeSpan CurrentPosition
        {
            get
            {
                if (_stream == 0) return TimeSpan.Zero;
                long pos = Bass.ChannelGetPosition(_stream, PositionFlags.Bytes);
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
                long len = Bass.ChannelGetLength(_stream, PositionFlags.Bytes);
                if (len < 0) return TimeSpan.Zero;
                double seconds = Bass.ChannelBytes2Seconds(_stream, len);
                return TimeSpan.FromSeconds(Math.Max(0, seconds));
            }
        }

        public bool CrossfadeEnabled { get; set; } = false;
        public int CrossfadeDurationSeconds { get; set; } = 3;

        public event EventHandler? PlaybackPositionChanged;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? TrackEnded;
        public event EventHandler? TrackCrossfadeTriggered;

        public BassAudioEngine()
        {
            Bass.Init();
            _equalizer = new BassEqualizer();

            _endSyncProc = OnTrackEndSync;
            _crossfadeSyncProc = OnCrossfadeTriggerSync;
        }

        private void OnTrackEndSync(int handle, int channel, int data, IntPtr user)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnCrossfadeTriggerSync(int handle, int channel, int data, IntPtr user)
        {
            lock (_lock)
            {
                if (_crossfadeTriggeredForCurrentTrack) return;
                _crossfadeTriggeredForCurrentTrack = true;

                // Remove the End sync so this outgoing track will NOT fire TrackEnded when it finishes fading
                if (_stream != 0 && _endSyncHandle != 0)
                {
                    Bass.ChannelRemoveSync(_stream, _endSyncHandle);
                    _endSyncHandle = 0;
                }
                if (_stream != 0 && _crossfadeSyncHandle != 0)
                {
                    Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle);
                    _crossfadeSyncHandle = 0;
                }
            }

            TrackCrossfadeTriggered?.Invoke(this, EventArgs.Empty);
        }

        public Task LoadFileAsync(string filePath)
        {
            return LoadFileAsync(filePath, isCrossfade: false);
        }

        public Task LoadFileAsync(string filePath, bool isCrossfade)
        {
            return Task.Run(() =>
            {
                int crossfadeMs = Math.Clamp(CrossfadeDurationSeconds, 1, 10) * 1000;

                lock (_lock)
                {
                    _crossfadeTriggeredForCurrentTrack = false;

                    // ORGANIC CROSSFADE:
                    // If crossfade is enabled and current stream is actively playing, fade out old stream while fading in new stream
                    if (isCrossfade && CrossfadeEnabled && _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing)
                    {
                        int oldStream = _stream;

                        // Unhook all syncs from oldStream so it won't fire any callbacks
                        if (_endSyncHandle != 0)
                        {
                            Bass.ChannelRemoveSync(oldStream, _endSyncHandle);
                            _endSyncHandle = 0;
                        }
                        if (_crossfadeSyncHandle != 0)
                        {
                            Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle);
                            _crossfadeSyncHandle = 0;
                        }

                        int newStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
                        if (newStream != 0)
                        {
                            // Start newStream at 0 volume and play immediately
                            Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, 0f);
                            Bass.ChannelPlay(newStream);

                            // Smooth slide: new stream fades UP to volume, old stream fades DOWN to 0
                            Bass.ChannelSlideAttribute(newStream, ChannelAttribute.Volume, _volume, crossfadeMs);
                            Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, 0f, crossfadeMs);

                            _stream = newStream;
                            _equalizer.AttachToStream(newStream);

                            SetupStreamSyncs(newStream);

                            // Safely free oldStream once its slide down finishes completely
                            _ = Task.Delay(crossfadeMs + 600).ContinueWith(_ =>
                            {
                                Bass.ChannelStop(oldStream);
                                Bass.StreamFree(oldStream);
                            });

                            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                            return;
                        }
                    }

                    // NORMAL (NON-CROSSFADE) TRANSITION:
                    if (_stream != 0)
                    {
                        if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _endSyncHandle); _endSyncHandle = 0; }
                        if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }
                        Bass.StreamFree(_stream);
                        _stream = 0;
                    }

                    _stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
                    if (_stream != 0)
                    {
                        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, _volume);
                        _equalizer.AttachToStream(_stream);
                        SetupStreamSyncs(_stream);
                    }

                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        private void SetupStreamSyncs(int stream)
        {
            if (stream == 0) return;

            // 1. Regular End Sync
            _endSyncHandle = Bass.ChannelSetSync(stream, SyncFlags.End, 0, _endSyncProc!, IntPtr.Zero);

            // 2. Position Sync for Crossfade
            if (CrossfadeEnabled)
            {
                long totalBytes = Bass.ChannelGetLength(stream, PositionFlags.Bytes);
                if (totalBytes > 0)
                {
                    double totalSeconds = Bass.ChannelBytes2Seconds(stream, totalBytes);
                    if (totalSeconds > CrossfadeDurationSeconds + 1)
                    {
                        double triggerSeconds = totalSeconds - CrossfadeDurationSeconds;
                        long triggerBytes = Bass.ChannelSeconds2Bytes(stream, triggerSeconds);

                        if (triggerBytes > 0 && triggerBytes < totalBytes)
                        {
                            _crossfadeSyncHandle = Bass.ChannelSetSync(stream, SyncFlags.Position, triggerBytes, _crossfadeSyncProc!, IntPtr.Zero);
                        }
                    }
                }
            }
        }

        public void CheckCrossfadeProgress()
        {
            // Fallback check in case position sync was missed (e.g. variable bitrate or stream format edge case)
            if (!CrossfadeEnabled || _crossfadeTriggeredForCurrentTrack || _stream == 0) return;
            if (!IsPlaying) return;

            var total = TotalDuration;
            if (total.TotalSeconds <= CrossfadeDurationSeconds + 1) return;

            var current = CurrentPosition;
            if (current >= total - TimeSpan.FromSeconds(CrossfadeDurationSeconds))
            {
                OnCrossfadeTriggerSync(0, _stream, 0, IntPtr.Zero);
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

                // Reset crossfade triggers if seeking before trigger position
                lock (_lock)
                {
                    _crossfadeTriggeredForCurrentTrack = false;
                    if (_crossfadeSyncHandle != 0)
                    {
                        Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle);
                        _crossfadeSyncHandle = 0;
                    }
                    if (_endSyncHandle != 0)
                    {
                        Bass.ChannelRemoveSync(_stream, _endSyncHandle);
                        _endSyncHandle = 0;
                    }
                    SetupStreamSyncs(_stream);
                }

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
            lock (_lock)
            {
                if (_stream != 0)
                {
                    if (_endSyncHandle != 0) Bass.ChannelRemoveSync(_stream, _endSyncHandle);
                    if (_crossfadeSyncHandle != 0) Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle);
                    Bass.StreamFree(_stream);
                    _stream = 0;
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
