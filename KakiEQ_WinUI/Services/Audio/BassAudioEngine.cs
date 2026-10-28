using System;
using System.IO;
using System.Threading.Tasks;
using ManagedBass;

namespace KakiEQ_WinUI.Services.Audio
{
    public class BassAudioEngine : IAudioEngine, IDisposable
    {
        private int _stream;
        private float _volume = 1.0f;
        private readonly BassEqualizer _equalizer;
        private readonly MultiOutputDeviceService _multiOutput;
        private readonly SystemAudioRoutingService _systemAudioRouting;
        
        private SyncProcedure? _endSyncProc;
        private int _endSyncHandle;
        
        private SyncProcedure? _crossfadeSyncProc;
        private int _crossfadeSyncHandle;

        private bool _crossfadeTriggeredForCurrentTrack = false;
        private readonly object _lock = new();

        // Reference to current in-memory AI track data to prevent GC while playing
        private byte[]? _currentMemoryTrackData;

        private bool _isKaraokeMode = false;
        public bool IsKaraokeMode
        {
            get => _isKaraokeMode;
            set
            {
                if (_isKaraokeMode != value)
                {
                    _isKaraokeMode = value;
                    KaraokeModeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public float KaraokeVocalAttenuation { get; set; } = 1.0f;

        public EqualizerBase Equalizer => _equalizer;
        public MultiOutputDeviceService MultiOutput => _multiOutput;
        public SystemAudioRoutingService SystemAudioRouting => _systemAudioRouting;
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
        public event EventHandler? KaraokeModeChanged;

        public BassAudioEngine()
        {
            Bass.Init();
            _equalizer = new BassEqualizer();
            _multiOutput = new MultiOutputDeviceService();
            _systemAudioRouting = new SystemAudioRoutingService(_multiOutput);

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

                var total = TotalDuration;
                var current = CurrentPosition;
                if (total.TotalSeconds > CrossfadeDurationSeconds + 1)
                {
                    double remainingSeconds = total.TotalSeconds - current.TotalSeconds;
                    if (remainingSeconds > CrossfadeDurationSeconds + 2.0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Crossfade Guard] False trigger rejected. Remaining: {remainingSeconds:F1}s vs Crossfade: {CrossfadeDurationSeconds}s");
                        return;
                    }
                }

                _crossfadeTriggeredForCurrentTrack = true;

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
                    _currentMemoryTrackData = null; // Clear any old in-memory buffer

                    // ORGANIC CROSSFADE
                    if (isCrossfade && CrossfadeEnabled && _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing)
                    {
                        int oldStream = _stream;

                        if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _endSyncHandle); _endSyncHandle = 0; }
                        if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }

                        int newStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Float | BassFlags.Prescan);
                        if (newStream == 0) newStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Prescan);

                        if (newStream != 0)
                        {
                            Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, 0f);
                            Bass.ChannelPlay(newStream);

                            Bass.ChannelSlideAttribute(newStream, ChannelAttribute.Volume, _volume, crossfadeMs);
                            Bass.ChannelSlideAttribute(oldStream, ChannelAttribute.Volume, 0f, crossfadeMs);

                            _stream = newStream;
                            _equalizer.AttachToStream(newStream);
                            _multiOutput.AttachToStream(newStream);
                            SetupStreamSyncs(newStream);

                            _ = Task.Delay(crossfadeMs + 600).ContinueWith(_ =>
                            {
                                Bass.ChannelStop(oldStream);
                                Bass.StreamFree(oldStream);
                            });

                            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                            return;
                        }
                    }

                    // NORMAL TRANSITION
                    if (_stream != 0)
                    {
                        if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _endSyncHandle); _endSyncHandle = 0; }
                        if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }
                        Bass.StreamFree(_stream);
                        _stream = 0;
                    }

                    _stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Float | BassFlags.Prescan);
                    if (_stream == 0) _stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Prescan);

                    if (_stream != 0)
                    {
                        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, _volume);
                        _equalizer.AttachToStream(_stream);
                        _multiOutput.AttachToStream(_stream);
                        SetupStreamSyncs(_stream);
                    }

                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        public Task<bool> SwitchStreamFromMemoryAsync(byte[] audioData)
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    if (audioData == null || audioData.Length == 0) return false;

                    TimeSpan currentPos = CurrentPosition;
                    bool wasPlaying = IsPlaying;

                    int newStream = Bass.CreateStream(audioData, 0, audioData.Length, BassFlags.Float | BassFlags.Prescan);
                    if (newStream == 0) newStream = Bass.CreateStream(audioData, 0, audioData.Length, BassFlags.Prescan);
                    if (newStream == 0) return false;

                    int oldStream = _stream;
                    if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _endSyncHandle); _endSyncHandle = 0; }
                    if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }

                    long bytes = Bass.ChannelSeconds2Bytes(newStream, currentPos.TotalSeconds);
                    Bass.ChannelSetPosition(newStream, bytes, PositionFlags.Bytes);

                    Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, _volume);
                    _equalizer.AttachToStream(newStream);
                    _multiOutput.AttachToStream(newStream);
                    SetupStreamSyncs(newStream);

                    _stream = newStream;
                    _currentMemoryTrackData = audioData; // Pin in RAM while playing

                    if (wasPlaying)
                    {
                        Bass.ChannelPlay(newStream);
                    }

                    if (oldStream != 0)
                    {
                        Bass.ChannelStop(oldStream);
                        Bass.StreamFree(oldStream);
                    }

                    PlaybackPositionChanged?.Invoke(this, EventArgs.Empty);
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            });
        }

        public Task<bool> SwitchStreamPreservingPositionAsync(string filePath)
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    if (!File.Exists(filePath)) return false;

                    TimeSpan currentPos = CurrentPosition;
                    bool wasPlaying = IsPlaying;

                    int newStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Float | BassFlags.Prescan);
                    if (newStream == 0) newStream = Bass.CreateStream(filePath, 0, 0, BassFlags.Prescan);
                    if (newStream == 0) return false;

                    int oldStream = _stream;
                    if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _endSyncHandle); _endSyncHandle = 0; }
                    if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }

                    long bytes = Bass.ChannelSeconds2Bytes(newStream, currentPos.TotalSeconds);
                    Bass.ChannelSetPosition(newStream, bytes, PositionFlags.Bytes);

                    Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, _volume);
                    _equalizer.AttachToStream(newStream);
                    _multiOutput.AttachToStream(newStream);
                    SetupStreamSyncs(newStream);

                    _stream = newStream;
                    _currentMemoryTrackData = null; // Revert memory buffer

                    if (wasPlaying)
                    {
                        Bass.ChannelPlay(newStream);
                    }

                    if (oldStream != 0)
                    {
                        Bass.ChannelStop(oldStream);
                        Bass.StreamFree(oldStream);
                    }

                    PlaybackPositionChanged?.Invoke(this, EventArgs.Empty);
                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            });
        }

        private void SetupStreamSyncs(int stream)
        {
            if (stream == 0) return;

            _endSyncHandle = Bass.ChannelSetSync(stream, SyncFlags.End, 0, _endSyncProc!, IntPtr.Zero);

            if (CrossfadeEnabled)
            {
                long totalBytes = Bass.ChannelGetLength(stream, PositionFlags.Bytes);
                if (totalBytes > 0)
                {
                    double totalSec = Bass.ChannelBytes2Seconds(stream, totalBytes);
                    int safeCrossSec = Math.Clamp(CrossfadeDurationSeconds, 1, 10);

                    if (totalSec > safeCrossSec + 1.0)
                    {
                        double triggerSec = totalSec - safeCrossSec;
                        long triggerBytes = Bass.ChannelSeconds2Bytes(stream, triggerSec);

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
            if (!CrossfadeEnabled || _crossfadeTriggeredForCurrentTrack || _stream == 0) return;
            if (!IsPlaying) return;

            var total = TotalDuration;
            if (total.TotalSeconds <= CrossfadeDurationSeconds + 1) return;

            var current = CurrentPosition;
            double remaining = total.TotalSeconds - current.TotalSeconds;
            if (remaining <= CrossfadeDurationSeconds && remaining >= -0.5)
            {
                OnCrossfadeTriggerSync(0, _stream, 0, IntPtr.Zero);
            }
        }

        public Task PlayAsync()
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_stream != 0)
                    {
                        Bass.ChannelPlay(_stream);
                        _multiOutput.NotifyPlaybackState(true);
                        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            });
        }

        public Task PauseAsync()
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_stream != 0)
                    {
                        Bass.ChannelPause(_stream);
                        _multiOutput.NotifyPlaybackState(false);
                        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            });
        }

        public Task StopAsync()
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_stream != 0)
                    {
                        Bass.ChannelStop(_stream);
                        _multiOutput.NotifyPlaybackState(false);
                        _multiOutput.FlushSecondaryBuffers();
                        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            });
        }

        public void SetPosition(TimeSpan position)
        {
            lock (_lock)
            {
                if (_stream != 0)
                {
                    long bytes = Bass.ChannelSeconds2Bytes(_stream, position.TotalSeconds);
                    Bass.ChannelSetPosition(_stream, bytes, PositionFlags.Bytes);
                    _multiOutput.FlushSecondaryBuffers();

                    if (CrossfadeEnabled)
                    {
                        long totalBytes = Bass.ChannelGetLength(_stream, PositionFlags.Bytes);
                        if (totalBytes > 0)
                        {
                            double totalSec = Bass.ChannelBytes2Seconds(_stream, totalBytes);
                            int safeCrossSec = Math.Clamp(CrossfadeDurationSeconds, 1, 10);
                            double triggerSec = totalSec - safeCrossSec;
                            if (position.TotalSeconds < triggerSec && totalSec > safeCrossSec + 1.0)
                            {
                                _crossfadeTriggeredForCurrentTrack = false;
                                if (_crossfadeSyncHandle != 0)
                                {
                                    Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle);
                                    _crossfadeSyncHandle = 0;
                                }
                                long triggerBytes = Bass.ChannelSeconds2Bytes(_stream, triggerSec);
                                if (triggerBytes > 0)
                                {
                                    _crossfadeSyncHandle = Bass.ChannelSetSync(_stream, SyncFlags.Position, triggerBytes, _crossfadeSyncProc!, IntPtr.Zero);
                                }
                            }
                        }
                    }

                    PlaybackPositionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void SetVolume(float volume)
        {
            _volume = Math.Clamp(volume, 0f, 1f);
            _multiOutput.MasterVolume = _volume;
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
                _currentMemoryTrackData = null;
                _systemAudioRouting.Dispose();
                _multiOutput.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
