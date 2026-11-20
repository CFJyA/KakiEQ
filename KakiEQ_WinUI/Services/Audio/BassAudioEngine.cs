using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;

namespace KakiEQ_WinUI.Services.Audio
{
    public class BassAudioEngine : IAudioEngine, IDisposable
    {
        private readonly record struct TrackAudioProfile(float LinearGain, float GainDb, double IntroStartSec, double OutroEndSec);

        private sealed class StemFilterState
        {
            public float LpL;
            public float LpR;
            public float HpStateL;
            public float HpStateR;
            public float AlphaLow = 0.029f;
            public float AlphaHigh = 0.43f;
            public int Channels = 2;

            public void Configure(int sampleRate, int channels = 2)
            {
                int sr = Math.Max(8000, sampleRate);
                Channels = Math.Max(1, channels);
                AlphaLow = 1f - MathF.Exp(-2f * MathF.PI * 210f / sr);
                AlphaHigh = 1f - MathF.Exp(-2f * MathF.PI * 4200f / sr);
                Reset();
            }

            public void Reset()
            {
                LpL = 0f;
                LpR = 0f;
                HpStateL = 0f;
                HpStateR = 0f;
            }
        }

        private int _stream;
        private int _fadingOutStream;
        private CancellationTokenSource? _crossfadeRampCts;
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

        private bool _crossfadeEnabled = false;
        private int _crossfadeDurationSeconds = 3;
        private bool _volumeNormalizationEnabled = false;
        private bool _harmonicAutomixEnabled = false;
        private bool _stemMorphCrossfadeEnabled = false;
        private bool _wasapiExclusiveEnabled = false;
        private float _currentNormalizationLinear = 1.0f;
        private float _currentNormalizationGainDb = 0f;
        private double _currentTrackBpm = 0;
        private double _nextTrackBpm = 0;
        private double _currentTrackMusicalEndSec = 0;
        private string _currentLoadedFilePath = string.Empty;

        // Stem-Morphing Crossfade DSP state
        private volatile float _currentCrossfadeProgress = 1.0f;
        private int _oldStreamStemDspHandle = 0;
        private int _newStreamStemDspHandle = 0;
        private DSPProcedure? _oldStemDspProc;
        private DSPProcedure? _newStemDspProc;
        private readonly StemFilterState _oldStemState = new();
        private readonly StemFilterState _newStemState = new();

        // FFT Spectrum Analyzer & WASAPI Exclusive state
        private readonly float[] _fftBuffer = new float[1024];
        private readonly float[] _exclusivePcmRing = new float[2048];
        private int _exclusiveRingWritePos = 0;
        private readonly object _ringLock = new();
        private NAudio.Wave.WasapiOut? _exclusiveOut;
        private BassExclusiveWaveProvider? _exclusiveProvider;
        private volatile float _exclusiveNewGain = 1.0f;
        private volatile float _exclusiveOldGain = 0.0f;
        private volatile bool _exclusiveEndSignaled = false;
        private int _exclusiveSrcRate = 48000;
        private int _exclusiveSrcChannels = 2;

        // Precomputed 1024-pt Radix-2 FFT tables for Exclusive mode spectrum analysis
        private static readonly int[] FftBitRev1024 = BuildBitRev(1024);
        private static readonly float[] FftHann1024 = BuildHann(1024);
        private static readonly float[] FftCos1024 = BuildTrigCos(1024);
        private static readonly float[] FftSin1024 = BuildTrigSin(1024);

        private readonly ConcurrentDictionary<string, TrackAudioProfile> _trackAudioProfileCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _prewarmingPaths = new(StringComparer.OrdinalIgnoreCase);

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

        public bool IsPlaying
        {
            get
            {
                if (_wasapiExclusiveEnabled && _exclusiveOut != null)
                {
                    return _exclusiveOut.PlaybackState == NAudio.Wave.PlaybackState.Playing;
                }
                return _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
            }
        }

        public float Volume => _volume;
        public float CurrentNormalizationGainDb => _currentNormalizationGainDb;

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

        public bool CrossfadeEnabled
        {
            get => _crossfadeEnabled;
            set
            {
                lock (_lock)
                {
                    if (_crossfadeEnabled != value)
                    {
                        _crossfadeEnabled = value;
                        ReconfigureCrossfadeSyncForCurrentStreamLocked();
                    }
                }
            }
        }

        public int CrossfadeDurationSeconds
        {
            get => _crossfadeDurationSeconds;
            set
            {
                int clamped = Math.Clamp(value, 1, 10);
                lock (_lock)
                {
                    if (_crossfadeDurationSeconds != clamped)
                    {
                        _crossfadeDurationSeconds = clamped;
                        ReconfigureCrossfadeSyncForCurrentStreamLocked();
                    }
                }
            }
        }

        public bool VolumeNormalizationEnabled
        {
            get => _volumeNormalizationEnabled;
            set
            {
                lock (_lock)
                {
                    if (_volumeNormalizationEnabled != value)
                    {
                        _volumeNormalizationEnabled = value;
                        _multiOutput.NormalizationMultiplier = value ? _currentNormalizationLinear : 1.0f;
                        if (_wasapiExclusiveEnabled && _fadingOutStream == 0)
                        {
                            _exclusiveNewGain = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                        }
                        else
                        {
                            _multiOutput.SlidePrimaryDeviceVolume(160);
                        }
                    }
                }
            }
        }

        public bool HarmonicAutomixEnabled
        {
            get => _harmonicAutomixEnabled;
            set
            {
                lock (_lock)
                {
                    if (_harmonicAutomixEnabled != value)
                    {
                        _harmonicAutomixEnabled = value;
                        ReconfigureCrossfadeSyncForCurrentStreamLocked();
                    }
                }
            }
        }

        public bool StemMorphCrossfadeEnabled
        {
            get => _stemMorphCrossfadeEnabled;
            set => _stemMorphCrossfadeEnabled = value;
        }

        public bool WasapiExclusiveEnabled
        {
            get => _wasapiExclusiveEnabled;
            set
            {
                bool changed = false;
                lock (_lock)
                {
                    if (_wasapiExclusiveEnabled != value)
                    {
                        _wasapiExclusiveEnabled = value;
                        changed = true;
                    }
                }

                if (changed)
                {
                    _ = ReloadCurrentStreamForOutputModeChangeAsync();
                }
            }
        }

        public void SetTrackBpmContext(double currentTrackBpm, double nextTrackBpm = 0)
        {
            lock (_lock)
            {
                _currentTrackBpm = currentTrackBpm;
                _nextTrackBpm = nextTrackBpm;
                ReconfigureCrossfadeSyncForCurrentStreamLocked();
            }
        }

        public event EventHandler? PlaybackPositionChanged;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? TrackEnded;
        public event EventHandler? TrackCrossfadeTriggered;
        public event EventHandler? KaraokeModeChanged;

        public BassAudioEngine()
        {
            // Configure BASS for FloatDSP stem morphing and sufficient headroom during dual-stream crossfade transitions
            Bass.Configure(Configuration.FloatDSP, true);
            Bass.Configure(Configuration.UpdatePeriod, 10);
            Bass.Configure(Configuration.DeviceBufferLength, 25);
            Bass.Configure(Configuration.PlaybackBufferLength, 180);

            int primaryDevice = FindPrimaryPhysicalBassDevice();
            if (!Bass.Init(primaryDevice, 48000, DeviceInitFlags.Default, IntPtr.Zero))
            {
                Bass.Init();
            }

            _equalizer = new BassEqualizer();
            _multiOutput = new MultiOutputDeviceService(_equalizer);
            _systemAudioRouting = new SystemAudioRoutingService(_multiOutput);

            _endSyncProc = OnTrackEndSync;
            _crossfadeSyncProc = OnCrossfadeTriggerSync;
            _oldStemDspProc = OnOldStreamStemDsp;
            _newStemDspProc = OnNewStreamStemDsp;
        }

        private static int FindPrimaryPhysicalBassDevice()
        {
            int count = Bass.DeviceCount;
            int startIndex = (count > 2) ? 2 : 1;
            int firstPhysical = -1;

            for (int i = startIndex; i < count; i++)
            {
                if (Bass.GetDeviceInfo(i, out var info) && info.IsEnabled && !MultiOutputDeviceService.IsVirtualCableDevice(info))
                {
                    if (info.IsDefault) return i;
                    if (firstPhysical == -1) firstPhysical = i;
                }
            }

            return firstPhysical != -1 ? firstPhysical : -1;
        }

        private void EnsurePrimaryPhysicalDeviceActive()
        {
            int primaryDev = _multiOutput.GetPrimaryPhysicalDeviceIndex();
            if (primaryDev > 0)
            {
                Bass.Init(primaryDev, 48000, DeviceInitFlags.Default, IntPtr.Zero);
                Bass.CurrentDevice = primaryDev;
            }
        }

        private void OnTrackEndSync(int handle, int channel, int data, IntPtr user)
        {
            if (_wasapiExclusiveEnabled)
            {
                if (_exclusiveEndSignaled) return;
                _exclusiveEndSignaled = true;
            }
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnCrossfadeTriggerSync(int handle, int channel, int data, IntPtr user)
        {
            lock (_lock)
            {
                if (_crossfadeTriggeredForCurrentTrack || _stream == 0 || (handle != 0 && channel != _stream)) return;

                double totalSec = TotalDuration.TotalSeconds;
                double curPosSec = CurrentPosition.TotalSeconds;
                double effectiveEndSec = (_currentTrackMusicalEndSec > 5.0 && _currentTrackMusicalEndSec < totalSec && curPosSec < _currentTrackMusicalEndSec - 1.2)
                    ? _currentTrackMusicalEndSec
                    : totalSec;
                double crossSec = GetEffectiveCrossfadeDurationSeconds(effectiveEndSec);

                if (crossSec < 1.0) return;

                if (effectiveEndSec > crossSec + 1.0)
                {
                    double remainingSeconds = effectiveEndSec - curPosSec;
                    if (remainingSeconds > crossSec + 2.2)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Crossfade Guard] False trigger rejected. Remaining: {remainingSeconds:F1}s vs Crossfade: {crossSec:F1}s");
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

        private TrackAudioProfile GetOrComputeTrackAudioProfile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new TrackAudioProfile(1.0f, 0f, 0.0, 0.0);

            if (_trackAudioProfileCache.TryGetValue(filePath, out var cached))
                return cached;

            double rgGain = double.NaN;
            double rgPeak = double.NaN;
            try
            {
                using var tfile = TagLib.File.Create(filePath);
                if (!double.IsNaN(tfile.Tag.ReplayGainTrackGain))
                    rgGain = tfile.Tag.ReplayGainTrackGain;
                else if (!double.IsNaN(tfile.Tag.ReplayGainAlbumGain))
                    rgGain = tfile.Tag.ReplayGainAlbumGain;

                if (!double.IsNaN(tfile.Tag.ReplayGainTrackPeak) && tfile.Tag.ReplayGainTrackPeak > 0)
                    rgPeak = tfile.Tag.ReplayGainTrackPeak;
            }
            catch { }

            double introStartSec = 0.0;
            double outroEndSec = 0.0;
            float maxPeak = (!double.IsNaN(rgPeak) && rgPeak > 0) ? (float)rgPeak : 0f;
            float gainDb = 0f;

            int dec = 0;
            try
            {
                dec = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                if (dec != 0 && Bass.ChannelGetInfo(dec, out var info))
                {
                    int sr = Math.Max(8000, info.Frequency);
                    int ch = Math.Max(1, info.Channels);
                    long totalBytes = Bass.ChannelGetLength(dec, PositionFlags.Bytes);
                    double totalSec = totalBytes > 0 ? Bass.ChannelBytes2Seconds(dec, totalBytes) : 0.0;
                    outroEndSec = totalSec;

                    int blockFrames50ms = Math.Max(128, sr / 20);
                    float[] buf50 = new float[blockFrames50ms * ch];

                    // 1. Detectar silencio inicial (hasta los primeros 2.5 s)
                    if (totalSec > 4.0)
                    {
                        double scanIntroMax = Math.Min(2.5, totalSec * 0.25);
                        int introBlocks = (int)(scanIntroMax / 0.05);
                        Bass.ChannelSetPosition(dec, 0, PositionFlags.Bytes);
                        for (int b = 0; b < introBlocks; b++)
                        {
                            int read = Bass.ChannelGetData(dec, buf50, buf50.Length * sizeof(float));
                            if (read <= 0) break;
                            int samples = read / sizeof(float);
                            double sumSq = 0;
                            for (int i = 0; i < samples; i++)
                            {
                                float s = buf50[i];
                                sumSq += s * s;
                            }
                            double rms = Math.Sqrt(sumSq / Math.Max(1, samples));
                            if (rms >= 0.0056) // > -45 dBFS
                            {
                                introStartSec = Math.Max(0.0, b * 0.05 - 0.04);
                                break;
                            }
                        }
                    }

                    // 2. Detectar cola de silencio final (últimos 8.0 s)
                    if (totalSec > 10.0)
                    {
                        double scanTailSec = Math.Min(8.0, totalSec * 0.30);
                        double tailStart = Math.Max(0.0, totalSec - scanTailSec);
                        long tailByte = Bass.ChannelSeconds2Bytes(dec, tailStart);
                        if (Bass.ChannelSetPosition(dec, tailByte, PositionFlags.Bytes))
                        {
                            double curSec = tailStart;
                            double lastActiveSec = tailStart;
                            bool sawTailSignal = false;
                            while (true)
                            {
                                int read = Bass.ChannelGetData(dec, buf50, buf50.Length * sizeof(float));
                                if (read <= 0) break;
                                int samples = read / sizeof(float);
                                int framesRead = samples / ch;
                                if (framesRead <= 0) break;

                                double blockDur = (double)framesRead / sr;
                                double sumSq = 0;
                                for (int i = 0; i < samples; i++)
                                {
                                    float s = buf50[i];
                                    float abs = MathF.Abs(s);
                                    if (abs > maxPeak) maxPeak = abs;
                                    sumSq += s * s;
                                }

                                double rms = Math.Sqrt(sumSq / samples);
                                curSec += blockDur;
                                if (rms >= 0.0018) // > -55 dBFS
                                {
                                    lastActiveSec = curSec;
                                    sawTailSignal = true;
                                }
                            }

                            if (sawTailSignal)
                            {
                                outroEndSec = Math.Min(totalSec, Math.Max(totalSec - 3.0, lastActiveSec + 0.20));
                            }
                        }
                    }

                    // 3. Medir sonoridad perceptual (-14 LUFS objetivo) si no hay ReplayGain o falta pico real
                    bool hasValidRg = !double.IsNaN(rgGain) && Math.Abs(rgGain) > 0.01;
                    if (!hasValidRg || maxPeak <= 0.001f)
                    {
                        double[] anchors = totalSec > 8.0 ? new[] { 0.22, 0.50, 0.74 } : new[] { 0.0 };
                        double winSec = totalSec > 8.0 ? 1.8 : Math.Max(0.5, totalSec);
                        int subBlockFrames = Math.Max(256, sr / 10); // bloques de 100 ms
                        float[] subBuf = new float[subBlockFrames * ch];
                        float[] prevCh = new float[ch];

                        double gatedSum = 0.0;
                        int gatedCount = 0;
                        double allSum = 0.0;
                        int allCount = 0;

                        foreach (double a in anchors)
                        {
                            double startSec = totalSec > 8.0 ? Math.Clamp(totalSec * a - winSec * 0.5, 0.0, Math.Max(0.0, totalSec - 0.5)) : 0.0;
                            Bass.ChannelSetPosition(dec, Bass.ChannelSeconds2Bytes(dec, startSec), PositionFlags.Bytes);
                            Array.Clear(prevCh, 0, prevCh.Length);

                            int remainingFrames = (int)(winSec * sr);
                            while (remainingFrames > 0)
                            {
                                int wantFrames = Math.Min(subBlockFrames, remainingFrames);
                                int read = Bass.ChannelGetData(dec, subBuf, wantFrames * ch * sizeof(float));
                                if (read <= 0) break;

                                int samples = read / sizeof(float);
                                int frames = samples / ch;
                                if (frames <= 0) break;
                                remainingFrames -= frames;

                                double blockWeightedSq = 0.0;
                                for (int f = 0; f < frames; f++)
                                {
                                    int baseIdx = f * ch;
                                    for (int c = 0; c < ch; c++)
                                    {
                                        float s = subBuf[baseIdx + c];
                                        float abs = MathF.Abs(s);
                                        if (abs > maxPeak) maxPeak = abs;

                                        // Aproximación K-weighting de 1er orden (énfasis medios-agudos)
                                        float hp = s - 0.85f * prevCh[c];
                                        prevCh[c] = s;
                                        float weighted = 0.70f * s + 0.30f * hp;
                                        blockWeightedSq += weighted * weighted;
                                    }
                                }

                                double meanSq = blockWeightedSq / samples;
                                allSum += meanSq;
                                allCount++;

                                if (Math.Sqrt(meanSq) >= 0.0056) // compuerta > -45 dBFS
                                {
                                    gatedSum += meanSq;
                                    gatedCount++;
                                }
                            }
                        }

                        if (!hasValidRg)
                        {
                            double finalMeanSq = gatedCount > 0
                                ? (gatedSum / gatedCount)
                                : (allCount > 0 ? allSum / allCount : 0.04);

                            double measuredLufsDb = 20.0 * Math.Log10(Math.Max(1e-5, Math.Sqrt(finalMeanSq))) + 3.0103;
                            gainDb = Math.Clamp((float)(-14.0 - measuredLufsDb), -11.0f, 8.5f);
                        }
                        else
                        {
                            // ReplayGain 1.0 usa referencia -18 LUFS; desplazamos +4 dB hacia -14 LUFS
                            gainDb = Math.Clamp((float)(rgGain + 4.0), -11.0f, 8.5f);
                        }
                    }
                    else
                    {
                        gainDb = Math.Clamp((float)(rgGain + 4.0), -11.0f, 8.5f);
                    }
                }
            }
            catch { }
            finally
            {
                if (dec != 0)
                {
                    Bass.StreamFree(dec);
                }
            }

            float linear = MathF.Pow(10f, gainDb / 20f);
            if (maxPeak > 0.01f && maxPeak * linear > 0.96f)
            {
                linear = Math.Clamp(0.96f / maxPeak, 0.2f, 2.8f);
                gainDb = 20f * MathF.Log10(Math.Max(0.1f, linear));
            }
            else
            {
                linear = Math.Clamp(linear, 0.2f, 2.8f);
            }

            var profile = new TrackAudioProfile(linear, gainDb, introStartSec, outroEndSec);
            _trackAudioProfileCache[filePath] = profile;
            return profile;
        }

        private double GetEffectiveCrossfadeDurationSeconds(double totalSec)
        {
            if (!_crossfadeEnabled && !_harmonicAutomixEnabled) return 0.0;
            if (totalSec <= 3.0) return 0.0;

            double baseSec = _crossfadeEnabled ? Math.Clamp(_crossfadeDurationSeconds, 1, 10) : 3.5;

            if (_harmonicAutomixEnabled && _currentTrackBpm >= 60.0 && _currentTrackBpm <= 190.0)
            {
                // Sincronizar la duración al múltiplo de medio compás (2 pulsos de 4/4) más cercano
                double halfBarSec = 120.0 / _currentTrackBpm;
                double snapped = Math.Round(baseSec / halfBarSec) * halfBarSec;
                baseSec = Math.Clamp(snapped, 1.5, 10.0);
            }

            double maxAllowed = Math.Max(1.0, totalSec * 0.25);
            return Math.Min(baseSec, maxAllowed);
        }

        private void RemoveStemDspHandlesLocked()
        {
            if (_oldStreamStemDspHandle != 0)
            {
                if (_fadingOutStream != 0) Bass.ChannelRemoveDSP(_fadingOutStream, _oldStreamStemDspHandle);
                _oldStreamStemDspHandle = 0;
            }
            if (_newStreamStemDspHandle != 0)
            {
                if (_stream != 0) Bass.ChannelRemoveDSP(_stream, _newStreamStemDspHandle);
                _newStreamStemDspHandle = 0;
            }
            _currentCrossfadeProgress = 1.0f;
        }

        private void StopAndFreeFadingOutStreamLocked()
        {
            try
            {
                _crossfadeRampCts?.Cancel();
                _crossfadeRampCts?.Dispose();
            }
            catch { }
            _crossfadeRampCts = null;

            RemoveStemDspHandlesLocked();
            _exclusiveOldGain = 0f;

            if (_fadingOutStream != 0)
            {
                Bass.ChannelStop(_fadingOutStream);
                Bass.StreamFree(_fadingOutStream);
                _fadingOutStream = 0;
            }
        }

        private unsafe void OnOldStreamStemDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (!_stemMorphCrossfadeEnabled || buffer == IntPtr.Zero || length <= 0) return;

            float p = Math.Clamp(_currentCrossfadeProgress, 0f, 1f);
            // Bass (<210 Hz) exits in the first 45% so incoming kick/bass takes over cleanly without mud
            float bassGain = p < 0.45f ? MathF.Cos((p / 0.45f) * (MathF.PI * 0.5f)) : 0f;
            // Stereo instrumental side fades smoothly across the transition
            float sideGain = MathF.Pow(1f - p, 1.75f);
            // Center Mid Vocal (210 Hz - 4.2 kHz) stays upfront during the first 58% (singing over the new beat), then fades out
            float vocalGain = p < 0.58f
                ? (1.0f - (p / 0.58f) * 0.10f)
                : (0.90f * MathF.Cos(((p - 0.58f) / 0.42f) * (MathF.PI * 0.5f)));
            float highGain = MathF.Pow(1f - p, 1.35f);

            ApplyStemMorphInPlace((float*)buffer, length / sizeof(float), _oldStemState.Channels, _oldStemState, bassGain, vocalGain, sideGain, highGain);
        }

        private unsafe void OnNewStreamStemDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (!_stemMorphCrossfadeEnabled || buffer == IntPtr.Zero || length <= 0) return;

            float p = Math.Clamp(_currentCrossfadeProgress, 0f, 1f);
            // Incoming Bass (<210 Hz) locks in during the first 45% to replace outgoing bass
            float bassGain = p < 0.45f ? MathF.Sin((p / 0.45f) * (MathF.PI * 0.5f)) : 1.0f;
            // Incoming Stereo Instrumental rises smoothly from the start
            float sideGain = MathF.Sin(p * (MathF.PI * 0.5f));
            // Incoming Center Mid Vocal stays ducked for the first 48% to avoid vocal clash, then rises to 100%
            float vocalGain = p < 0.48f
                ? (p / 0.48f) * 0.18f
                : (0.18f + 0.82f * MathF.Sin(((p - 0.48f) / 0.52f) * (MathF.PI * 0.5f)));
            float highGain = MathF.Sin(p * (MathF.PI * 0.5f));

            ApplyStemMorphInPlace((float*)buffer, length / sizeof(float), _newStemState.Channels, _newStemState, bassGain, vocalGain, sideGain, highGain);
        }

        private static unsafe void ApplyStemMorphInPlace(
            float* data,
            int totalSamples,
            int channels,
            StemFilterState st,
            float bassGain,
            float vocalGain,
            float sideGain,
            float highGain)
        {
            int ch = Math.Max(1, channels);
            float aLow = st.AlphaLow;
            float aHigh = st.AlphaHigh;

            if (ch == 1)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    float s = data[i];
                    st.LpL += aLow * (s - st.LpL);
                    st.HpStateL += aHigh * (s - st.HpStateL);
                    float low = st.LpL;
                    float band = st.HpStateL - low;
                    float high = s - st.HpStateL;
                    data[i] = Math.Clamp(low * bassGain + band * vocalGain + high * highGain, -0.98f, 0.98f);
                }
                return;
            }

            for (int i = 0; i + 1 < totalSamples; i += ch)
            {
                float l = data[i];
                float r = data[i + 1];

                st.LpL += aLow * (l - st.LpL);
                st.LpR += aLow * (r - st.LpR);
                st.HpStateL += aHigh * (l - st.HpStateL);
                st.HpStateR += aHigh * (r - st.HpStateR);

                float lowL = st.LpL;
                float lowR = st.LpR;
                float bandL = st.HpStateL - lowL;
                float bandR = st.HpStateR - lowR;
                float highL = l - st.HpStateL;
                float highR = r - st.HpStateR;

                // Descomposición Mid/Side sobre la banda vocal (210 Hz – 4.2 kHz)
                float midVocal = 0.5f * (bandL + bandR);
                float sideInst = 0.5f * (bandL - bandR);

                float morphMid = midVocal * vocalGain;
                float morphSide = sideInst * sideGain;

                data[i] = Math.Clamp(lowL * bassGain + morphMid + morphSide + highL * highGain, -0.98f, 0.98f);
                data[i + 1] = Math.Clamp(lowR * bassGain + morphMid - morphSide + highR * highGain, -0.98f, 0.98f);
            }
        }

        private void ReconfigureCrossfadeSyncForCurrentStreamLocked()
        {
            if (_stream == 0) return;

            if (_crossfadeSyncHandle != 0)
            {
                Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle);
                _crossfadeSyncHandle = 0;
            }

            if ((_crossfadeEnabled || _harmonicAutomixEnabled) && !_crossfadeTriggeredForCurrentTrack)
            {
                double totalSec = TotalDuration.TotalSeconds;
                double effectiveEndSec = (_currentTrackMusicalEndSec > 5.0 && _currentTrackMusicalEndSec < totalSec)
                    ? _currentTrackMusicalEndSec
                    : totalSec;
                double crossSec = GetEffectiveCrossfadeDurationSeconds(effectiveEndSec);

                if (crossSec >= 1.0 && effectiveEndSec > crossSec + 1.0)
                {
                    double triggerSec = Math.Max(0.5, effectiveEndSec - crossSec - 0.18);
                    if (CurrentPosition.TotalSeconds < triggerSec)
                    {
                        long triggerBytes = Bass.ChannelSeconds2Bytes(_stream, triggerSec);
                        long totalBytes = Bass.ChannelGetLength(_stream, PositionFlags.Bytes);
                        if (triggerBytes > 0 && triggerBytes < totalBytes)
                        {
                            _crossfadeSyncHandle = Bass.ChannelSetSync(_stream, SyncFlags.Position, triggerBytes, _crossfadeSyncProc!, IntPtr.Zero);
                        }
                    }
                }
            }
        }

        private Task RunEqualPowerCrossfadeAsync(int oldStream, int newStream, int crossfadeMs, float startOldVol, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        double progress = Math.Clamp(sw.Elapsed.TotalMilliseconds / Math.Max(100, crossfadeMs), 0.0, 1.0);
                        _currentCrossfadeProgress = (float)progress;

                        lock (_lock)
                        {
                            if (token.IsCancellationRequested || _stream != newStream)
                                break;

                            bool stemMorphActive = _stemMorphCrossfadeEnabled && _oldStreamStemDspHandle != 0 && _newStreamStemDspHandle != 0;
                            float inGain;
                            float outGain;
                            if (stemMorphActive)
                            {
                                inGain = Math.Clamp((float)(progress / 0.10), 0f, 1f);
                                outGain = Math.Clamp((float)((1.0 - progress) / 0.10), 0f, 1f);
                            }
                            else
                            {
                                inGain = (float)Math.Sin(progress * (Math.PI * 0.5));
                                outGain = (float)Math.Cos(progress * (Math.PI * 0.5));
                            }

                            float targetVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                            if (_wasapiExclusiveEnabled && _exclusiveOut != null)
                            {
                                _exclusiveNewGain = targetVol * inGain;
                                if (_fadingOutStream == oldStream)
                                {
                                    _exclusiveOldGain = Math.Max(0f, startOldVol * outGain);
                                }
                            }
                            else
                            {
                                Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, targetVol * inGain);
                                if (_fadingOutStream == oldStream)
                                {
                                    Bass.ChannelSetAttribute(oldStream, ChannelAttribute.Volume, Math.Max(0f, startOldVol * outGain));
                                }
                            }
                        }

                        if (progress >= 1.0)
                            break;

                        await Task.Delay(25, token);
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
                finally
                {
                    lock (_lock)
                    {
                        RemoveStemDspHandlesLocked();

                        if (!token.IsCancellationRequested && _stream == newStream)
                        {
                            float finalVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                            if (_wasapiExclusiveEnabled && _exclusiveOut != null)
                            {
                                _exclusiveNewGain = finalVol;
                            }
                            else
                            {
                                Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, finalVol);
                            }
                        }

                        _exclusiveOldGain = 0f;
                        if (_fadingOutStream == oldStream)
                        {
                            Bass.ChannelStop(oldStream);
                            Bass.StreamFree(oldStream);
                            _fadingOutStream = 0;
                        }
                    }
                }
            });
        }

        private int CreateBassAudioStream(string filePath, bool decodeOnly)
        {
            BassFlags flags = BassFlags.Float | (decodeOnly ? BassFlags.Decode : BassFlags.Default);
            int s = Bass.CreateStream(filePath, 0, 0, flags);
            if (s == 0)
            {
                BassFlags fallback = decodeOnly ? BassFlags.Decode : BassFlags.Default;
                s = Bass.CreateStream(filePath, 0, 0, fallback);
            }
            return s;
        }

        public void PrewarmTrackAudioProfile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) ||
                _trackAudioProfileCache.ContainsKey(filePath) ||
                !_prewarmingPaths.TryAdd(filePath, 1))
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        GetOrComputeTrackAudioProfile(filePath);
                    }
                }
                catch { }
                finally
                {
                    _prewarmingPaths.TryRemove(filePath, out _);
                }
            });
        }

        private void ApplyLateTrackAudioProfileAsync(string filePath, int targetStream)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var profile = GetOrComputeTrackAudioProfile(filePath);
                    lock (_lock)
                    {
                        if (_stream != targetStream || _currentLoadedFilePath != filePath)
                            return;

                        _currentNormalizationLinear = profile.LinearGain;
                        _currentNormalizationGainDb = profile.GainDb;
                        _currentTrackMusicalEndSec = profile.OutroEndSec;
                        ReconfigureCrossfadeSyncForCurrentStreamLocked();

                        if (_volumeNormalizationEnabled)
                        {
                            _multiOutput.SetNormalizationMultiplier(profile.LinearGain, applyPrimaryVolume: _fadingOutStream == 0 && !_wasapiExclusiveEnabled);
                            if (_fadingOutStream == 0)
                            {
                                if (_wasapiExclusiveEnabled)
                                {
                                    _exclusiveNewGain = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                                }
                                else
                                {
                                    _multiOutput.SlidePrimaryDeviceVolume(180);
                                }
                            }
                        }
                    }
                }
                catch { }
            });
        }

        private void DisposeExclusiveOutLocked()
        {
            if (_exclusiveOut != null)
            {
                try { _exclusiveOut.Stop(); } catch { }
                try { _exclusiveOut.Dispose(); } catch { }
                _exclusiveOut = null;
            }
            _exclusiveProvider = null;
            _exclusiveOldGain = 0f;
        }

        private bool EnsureExclusiveOutputReadyLocked(int streamHandle, bool autoPlay)
        {
            int srcRate = 48000;
            int srcCh = 2;
            if (streamHandle != 0 && Bass.ChannelGetInfo(streamHandle, out var info))
            {
                srcRate = Math.Max(8000, info.Frequency);
                srcCh = Math.Clamp(info.Channels, 1, 2);
            }

            bool formatChanged = _exclusiveOut == null || _exclusiveProvider == null ||
                                 _exclusiveSrcRate != srcRate || _exclusiveSrcChannels != srcCh;

            if (formatChanged)
            {
                DisposeExclusiveOutLocked();
                _exclusiveSrcRate = srcRate;
                _exclusiveSrcChannels = srcCh;

                NAudio.CoreAudioApi.MMDevice? endpoint = null;
                try
                {
                    using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                    var def = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                    if (def != null && !IsVirtualCableDeviceName(def.FriendlyName))
                    {
                        endpoint = def;
                    }
                    else
                    {
                        endpoint = enumerator
                            .EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active)
                            .FirstOrDefault(d => !IsVirtualCableDeviceName(d.FriendlyName));
                    }
                }
                catch { }

                if (endpoint == null) return false;

                var mixFmt = endpoint.AudioClient.MixFormat;
                var candidates = new (NAudio.CoreAudioApi.AudioClientShareMode share, NAudio.Wave.WaveFormat fmt)[]
                {
                    (NAudio.CoreAudioApi.AudioClientShareMode.Exclusive, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(srcRate, srcCh)),
                    (NAudio.CoreAudioApi.AudioClientShareMode.Exclusive, new NAudio.Wave.WaveFormat(srcRate, 24, srcCh)),
                    (NAudio.CoreAudioApi.AudioClientShareMode.Exclusive, new NAudio.Wave.WaveFormat(srcRate, 16, srcCh)),
                    (NAudio.CoreAudioApi.AudioClientShareMode.Exclusive, mixFmt),
                    (NAudio.CoreAudioApi.AudioClientShareMode.Shared, mixFmt)
                };

                foreach (var (share, fmt) in candidates)
                {
                    NAudio.Wave.WasapiOut? testOut = null;
                    try
                    {
                        var provider = new BassExclusiveWaveProvider(this, fmt);
                        testOut = new NAudio.Wave.WasapiOut(endpoint, share, true, 20);
                        testOut.Init(provider);
                        _exclusiveOut = testOut;
                        _exclusiveProvider = provider;
                        break;
                    }
                    catch
                    {
                        try { testOut?.Dispose(); } catch { }
                    }
                }
            }

            if (_exclusiveOut == null) return false;

            if (autoPlay && _exclusiveOut.PlaybackState != NAudio.Wave.PlaybackState.Playing)
            {
                _exclusiveOut.Play();
            }
            return true;
        }

        private async Task ReloadCurrentStreamForOutputModeChangeAsync()
        {
            string file;
            TimeSpan savedPos;
            bool wasPlaying;

            lock (_lock)
            {
                file = _currentLoadedFilePath;
                if (string.IsNullOrWhiteSpace(file) || !File.Exists(file) || _stream == 0)
                {
                    if (!_wasapiExclusiveEnabled)
                    {
                        DisposeExclusiveOutLocked();
                    }
                    return;
                }

                // Evaluar IsPlaying previo sin depender aún del nuevo flag
                wasPlaying = _exclusiveOut != null
                    ? _exclusiveOut.PlaybackState == NAudio.Wave.PlaybackState.Playing
                    : Bass.ChannelIsActive(_stream) == ManagedBass.PlaybackState.Playing;
                savedPos = CurrentPosition;

                StopAndFreeFadingOutStreamLocked();
                DisposeExclusiveOutLocked();

                if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _endSyncHandle); _endSyncHandle = 0; }
                if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            await LoadFileAsync(file, isCrossfade: false);
            if (savedPos > TimeSpan.Zero)
            {
                SetPosition(savedPos);
            }
            if (wasPlaying)
            {
                await PlayAsync();
            }
        }

        public Task LoadFileAsync(string filePath)
        {
            return LoadFileAsync(filePath, isCrossfade: false);
        }

        public Task LoadFileAsync(string filePath, bool isCrossfade)
        {
            return Task.Run(() =>
            {
                bool hasCachedProfile = _trackAudioProfileCache.TryGetValue(filePath, out var profile);
                if (!hasCachedProfile)
                {
                    profile = isCrossfade
                        ? new TrackAudioProfile(1.0f, 0f, 0.0, 0.0)
                        : GetOrComputeTrackAudioProfile(filePath);
                }

                lock (_lock)
                {
                    if (!_wasapiExclusiveEnabled)
                    {
                        EnsurePrimaryPhysicalDeviceActive();
                        DisposeExclusiveOutLocked();
                    }

                    _currentLoadedFilePath = filePath;
                    _crossfadeTriggeredForCurrentTrack = false;
                    _currentMemoryTrackData = null;

                    _currentNormalizationLinear = profile.LinearGain;
                    _currentNormalizationGainDb = profile.GainDb;

                    bool wasPlayingNow = IsPlaying;
                    bool shouldCrossfade = isCrossfade
                        && (_crossfadeEnabled || _harmonicAutomixEnabled)
                        && _stream != 0
                        && wasPlayingNow;

                    // Limpiar cualquier fade-out residual previo antes de iniciar una nueva transición
                    StopAndFreeFadingOutStreamLocked();

                    // STUDIO EQUAL-POWER (+ OPTIONAL STEM-MORPHING) CROSSFADE
                    if (shouldCrossfade)
                    {
                        int oldStream = _stream;
                        double oldTotalSec = TotalDuration.TotalSeconds;
                        double oldRemainingSec = Math.Max(1.2, oldTotalSec - CurrentPosition.TotalSeconds);
                        double effectiveCrossSec = Math.Min(GetEffectiveCrossfadeDurationSeconds(oldTotalSec), oldRemainingSec);
                        int crossfadeMs = (int)Math.Round(Math.Clamp(effectiveCrossSec, 1.0, 10.0) * 1000.0);

                        float currentOldVol;
                        if (_wasapiExclusiveEnabled && _exclusiveOut != null)
                        {
                            currentOldVol = _exclusiveNewGain > 0 ? _exclusiveNewGain : _multiOutput.GetPrimaryDeviceEffectiveVolume();
                        }
                        else if (!Bass.ChannelGetAttribute(oldStream, ChannelAttribute.Volume, out currentOldVol))
                        {
                            currentOldVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                        }

                        if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _endSyncHandle); _endSyncHandle = 0; }
                        if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }

                        int newStream = CreateBassAudioStream(filePath, decodeOnly: _wasapiExclusiveEnabled);
                        if (newStream != 0)
                        {
                            long newLen = Bass.ChannelGetLength(newStream, PositionFlags.Bytes);
                            double newTotalSec = newLen > 0 ? Bass.ChannelBytes2Seconds(newStream, newLen) : 0.0;

                            if (profile.IntroStartSec >= 0.12 && newTotalSec > 10.0 && profile.IntroStartSec < newTotalSec * 0.15)
                            {
                                long introByte = Bass.ChannelSeconds2Bytes(newStream, profile.IntroStartSec);
                                if (introByte > 0) Bass.ChannelSetPosition(newStream, introByte, PositionFlags.Bytes);
                            }

                            _multiOutput.SetNormalizationMultiplier(_volumeNormalizationEnabled ? profile.LinearGain : 1.0f, applyPrimaryVolume: false);
                            _equalizer.AttachToStream(newStream);

                            if (_wasapiExclusiveEnabled)
                            {
                                _exclusiveNewGain = 0f;
                                _exclusiveOldGain = currentOldVol;
                                _fadingOutStream = oldStream;
                                _stream = newStream;
                                _currentTrackMusicalEndSec = profile.OutroEndSec;
                                SetupStreamSyncs(newStream);
                                AttachStemMorphDspsIfEnabledLocked(oldStream, newStream);

                                if (!EnsureExclusiveOutputReadyLocked(newStream, autoPlay: true))
                                {
                                    // Fallback si no hubiera endpoint WASAPI válido
                                    DisposeExclusiveOutLocked();
                                }
                            }
                            else
                            {
                                Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, 0f);
                                _multiOutput.AttachToStream(newStream, updatePrimaryVolume: false);
                                Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, 0f);

                                _fadingOutStream = oldStream;
                                _stream = newStream;
                                _currentTrackMusicalEndSec = profile.OutroEndSec;
                                SetupStreamSyncs(newStream);
                                AttachStemMorphDspsIfEnabledLocked(oldStream, newStream);

                                Bass.ChannelPlay(newStream);
                            }

                            _crossfadeRampCts = new CancellationTokenSource();
                            var rampToken = _crossfadeRampCts.Token;
                            _ = RunEqualPowerCrossfadeAsync(oldStream, newStream, crossfadeMs, currentOldVol, rampToken);

                            if (!hasCachedProfile)
                            {
                                ApplyLateTrackAudioProfileAsync(filePath, newStream);
                            }

                            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                            return;
                        }
                    }

                    // NORMAL TRANSITION
                    if (_stream != 0)
                    {
                        if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _endSyncHandle); _endSyncHandle = 0; }
                        if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }
                        Bass.ChannelStop(_stream);
                        Bass.StreamFree(_stream);
                        _stream = 0;
                    }

                    _multiOutput.SetNormalizationMultiplier(_volumeNormalizationEnabled ? profile.LinearGain : 1.0f, applyPrimaryVolume: false);
                    _stream = CreateBassAudioStream(filePath, decodeOnly: _wasapiExclusiveEnabled);

                    if (_stream != 0)
                    {
                        _currentTrackMusicalEndSec = profile.OutroEndSec;
                        float targetVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                        _equalizer.AttachToStream(_stream);

                        if (_wasapiExclusiveEnabled)
                        {
                            _exclusiveNewGain = targetVol;
                            _exclusiveOldGain = 0f;
                            SetupStreamSyncs(_stream);
                            EnsureExclusiveOutputReadyLocked(_stream, autoPlay: isCrossfade);
                        }
                        else
                        {
                            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, targetVol);
                            _multiOutput.AttachToStream(_stream);
                            SetupStreamSyncs(_stream);
                            if (isCrossfade)
                            {
                                Bass.ChannelPlay(_stream);
                                _multiOutput.NotifyPlaybackState(true);
                            }
                        }

                        if (!hasCachedProfile)
                        {
                            ApplyLateTrackAudioProfileAsync(filePath, _stream);
                        }
                    }

                    PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        private void AttachStemMorphDspsIfEnabledLocked(int oldStream, int newStream)
        {
            if (!_stemMorphCrossfadeEnabled || oldStream == 0 || newStream == 0) return;

            int srOld = 44100;
            int chOld = 2;
            if (Bass.ChannelGetInfo(oldStream, out var infoOld))
            {
                srOld = Math.Max(8000, infoOld.Frequency);
                chOld = Math.Max(1, infoOld.Channels);
            }

            int srNew = 44100;
            int chNew = 2;
            if (Bass.ChannelGetInfo(newStream, out var infoNew))
            {
                srNew = Math.Max(8000, infoNew.Frequency);
                chNew = Math.Max(1, infoNew.Channels);
            }

            _oldStemState.Configure(srOld, chOld);
            _newStemState.Configure(srNew, chNew);
            _currentCrossfadeProgress = 0.0f;

            _oldStreamStemDspHandle = Bass.ChannelSetDSP(oldStream, _oldStemDspProc!, IntPtr.Zero, 50);
            _newStreamStemDspHandle = Bass.ChannelSetDSP(newStream, _newStemDspProc!, IntPtr.Zero, 50);
        }

        public Task<bool> SwitchStreamFromMemoryAsync(byte[] audioData)
        {
            return Task.Run(() =>
            {
                lock (_lock)
                {
                    if (audioData == null || audioData.Length == 0) return false;

                    StopAndFreeFadingOutStreamLocked();
                    TimeSpan currentPos = CurrentPosition;
                    bool wasPlaying = IsPlaying;

                    BassFlags flags = BassFlags.Float | BassFlags.Prescan | (_wasapiExclusiveEnabled ? BassFlags.Decode : BassFlags.Default);
                    int newStream = Bass.CreateStream(audioData, 0, audioData.Length, flags);
                    if (newStream == 0)
                    {
                        newStream = Bass.CreateStream(audioData, 0, audioData.Length, BassFlags.Prescan | (_wasapiExclusiveEnabled ? BassFlags.Decode : BassFlags.Default));
                    }
                    if (newStream == 0) return false;

                    int oldStream = _stream;
                    if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _endSyncHandle); _endSyncHandle = 0; }
                    if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }

                    long bytes = Bass.ChannelSeconds2Bytes(newStream, currentPos.TotalSeconds);
                    Bass.ChannelSetPosition(newStream, bytes, PositionFlags.Bytes);

                    float targetMemVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                    _equalizer.AttachToStream(newStream);

                    if (_wasapiExclusiveEnabled)
                    {
                        _exclusiveNewGain = targetMemVol;
                        SetupStreamSyncs(newStream);
                        _stream = newStream;
                        _currentMemoryTrackData = audioData;
                        EnsureExclusiveOutputReadyLocked(newStream, autoPlay: wasPlaying);
                    }
                    else
                    {
                        Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, targetMemVol);
                        _multiOutput.AttachToStream(newStream);
                        SetupStreamSyncs(newStream);
                        _stream = newStream;
                        _currentMemoryTrackData = audioData;
                        if (wasPlaying) Bass.ChannelPlay(newStream);
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

                    StopAndFreeFadingOutStreamLocked();
                    TimeSpan currentPos = CurrentPosition;
                    bool wasPlaying = IsPlaying;

                    int newStream = CreateBassAudioStream(filePath, decodeOnly: _wasapiExclusiveEnabled);
                    if (newStream == 0) return false;

                    int oldStream = _stream;
                    if (_endSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _endSyncHandle); _endSyncHandle = 0; }
                    if (_crossfadeSyncHandle != 0) { Bass.ChannelRemoveSync(oldStream, _crossfadeSyncHandle); _crossfadeSyncHandle = 0; }

                    long bytes = Bass.ChannelSeconds2Bytes(newStream, currentPos.TotalSeconds);
                    Bass.ChannelSetPosition(newStream, bytes, PositionFlags.Bytes);

                    float targetMemVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                    _equalizer.AttachToStream(newStream);

                    if (_wasapiExclusiveEnabled)
                    {
                        _exclusiveNewGain = targetMemVol;
                        SetupStreamSyncs(newStream);
                        _stream = newStream;
                        _currentMemoryTrackData = null;
                        EnsureExclusiveOutputReadyLocked(newStream, autoPlay: wasPlaying);
                    }
                    else
                    {
                        Bass.ChannelSetAttribute(newStream, ChannelAttribute.Volume, targetMemVol);
                        _multiOutput.AttachToStream(newStream);
                        SetupStreamSyncs(newStream);
                        _stream = newStream;
                        _currentMemoryTrackData = null;
                        if (wasPlaying) Bass.ChannelPlay(newStream);
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

        private static bool IsVirtualCableDeviceName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Kaki EQ", StringComparison.OrdinalIgnoreCase);
        }

        private void SetupStreamSyncs(int stream)
        {
            if (stream == 0) return;

            _exclusiveEndSignaled = false;
            _endSyncHandle = Bass.ChannelSetSync(stream, SyncFlags.End, 0, _endSyncProc!, IntPtr.Zero);

            if (_crossfadeEnabled || _harmonicAutomixEnabled)
            {
                long totalBytes = Bass.ChannelGetLength(stream, PositionFlags.Bytes);
                if (totalBytes > 0)
                {
                    double totalSec = Bass.ChannelBytes2Seconds(stream, totalBytes);
                    double effectiveEndSec = (_currentTrackMusicalEndSec > 5.0 && _currentTrackMusicalEndSec < totalSec)
                        ? _currentTrackMusicalEndSec
                        : totalSec;
                    double crossSec = GetEffectiveCrossfadeDurationSeconds(effectiveEndSec);

                    if (crossSec >= 1.0 && effectiveEndSec > crossSec + 1.0)
                    {
                        double triggerSec = Math.Max(0.5, effectiveEndSec - crossSec - 0.18);
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
            if ((!_crossfadeEnabled && !_harmonicAutomixEnabled) || _crossfadeTriggeredForCurrentTrack || _stream == 0) return;
            if (!IsPlaying) return;

            double totalSec = TotalDuration.TotalSeconds;
            double curPosSec = CurrentPosition.TotalSeconds;
            double effectiveEndSec = (_currentTrackMusicalEndSec > 5.0 && _currentTrackMusicalEndSec < totalSec && curPosSec < _currentTrackMusicalEndSec - 1.2)
                ? _currentTrackMusicalEndSec
                : totalSec;
            double crossSec = GetEffectiveCrossfadeDurationSeconds(effectiveEndSec);

            if (crossSec < 1.0 || effectiveEndSec <= crossSec + 1.0) return;

            double remaining = effectiveEndSec - curPosSec;
            if (remaining <= crossSec + 0.25 && (totalSec - curPosSec) >= 0.7)
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
                        if (_wasapiExclusiveEnabled)
                        {
                            if (EnsureExclusiveOutputReadyLocked(_stream, autoPlay: true))
                            {
                                _exclusiveOut?.Play();
                            }
                        }
                        else
                        {
                            Bass.ChannelPlay(_stream);
                            _multiOutput.NotifyPlaybackState(true);
                        }
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
                    StopAndFreeFadingOutStreamLocked();
                    if (_stream != 0)
                    {
                        if (_wasapiExclusiveEnabled)
                        {
                            _exclusiveOut?.Pause();
                        }
                        else
                        {
                            Bass.ChannelPause(_stream);
                            _multiOutput.NotifyPlaybackState(false);
                        }
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
                    StopAndFreeFadingOutStreamLocked();
                    if (_stream != 0)
                    {
                        if (_wasapiExclusiveEnabled)
                        {
                            _exclusiveOut?.Stop();
                            Bass.ChannelSetPosition(_stream, 0, PositionFlags.Bytes);
                        }
                        else
                        {
                            Bass.ChannelStop(_stream);
                            _multiOutput.NotifyPlaybackState(false);
                            _multiOutput.FlushSecondaryBuffers();
                        }
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
                    if (_fadingOutStream != 0)
                    {
                        StopAndFreeFadingOutStreamLocked();
                        float restoreVol = _multiOutput.GetPrimaryDeviceEffectiveVolume();
                        if (_wasapiExclusiveEnabled && _exclusiveOut != null)
                        {
                            _exclusiveNewGain = restoreVol;
                        }
                        else
                        {
                            _multiOutput.UpdatePrimaryDeviceVolume();
                        }
                    }

                    _multiOutput.FlushSecondaryBuffers();
                    long bytes = Bass.ChannelSeconds2Bytes(_stream, position.TotalSeconds);
                    Bass.ChannelSetPosition(_stream, bytes, PositionFlags.Bytes);
                    _multiOutput.FlushSecondaryBuffers();

                    if (_crossfadeEnabled || _harmonicAutomixEnabled)
                    {
                        long totalBytes = Bass.ChannelGetLength(_stream, PositionFlags.Bytes);
                        if (totalBytes > 0)
                        {
                            double totalSec = Bass.ChannelBytes2Seconds(_stream, totalBytes);
                            double effectiveEndSec = (_currentTrackMusicalEndSec > 5.0 && _currentTrackMusicalEndSec < totalSec && position.TotalSeconds < _currentTrackMusicalEndSec - 1.2)
                                ? _currentTrackMusicalEndSec
                                : totalSec;
                            double crossSec = GetEffectiveCrossfadeDurationSeconds(effectiveEndSec);
                            double triggerSec = Math.Max(0.5, effectiveEndSec - crossSec - 0.18);

                            if (totalSec - position.TotalSeconds > 0.8)
                            {
                                _crossfadeTriggeredForCurrentTrack = false;
                                if (_crossfadeSyncHandle != 0)
                                {
                                    Bass.ChannelRemoveSync(_stream, _crossfadeSyncHandle);
                                    _crossfadeSyncHandle = 0;
                                }

                                if (crossSec >= 1.0 && position.TotalSeconds < triggerSec && effectiveEndSec > crossSec + 1.0)
                                {
                                    long triggerBytes = Bass.ChannelSeconds2Bytes(_stream, triggerSec);
                                    if (triggerBytes > 0 && triggerBytes < totalBytes)
                                    {
                                        _crossfadeSyncHandle = Bass.ChannelSetSync(_stream, SyncFlags.Position, triggerBytes, _crossfadeSyncProc!, IntPtr.Zero);
                                    }
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
            if (_wasapiExclusiveEnabled && _fadingOutStream == 0)
            {
                _exclusiveNewGain = _multiOutput.GetPrimaryDeviceEffectiveVolume();
            }
        }

        public bool GetSpectrumBands(float[] bandsOut)
        {
            if (bandsOut == null || bandsOut.Length == 0) return false;
            if (!IsPlaying || _stream == 0)
            {
                Array.Clear(bandsOut, 0, bandsOut.Length);
                return false;
            }

            double sampleRate = 48000.0;
            if (Bass.ChannelGetInfo(_stream, out var info) && info.Frequency > 0)
            {
                sampleRate = info.Frequency;
            }

            int fftSize;
            int numBins;
            float[] magnitudes;

            if (_wasapiExclusiveEnabled && _exclusiveOut != null)
            {
                fftSize = 1024;
                numBins = 512;
                magnitudes = ComputeExclusiveFftMagnitudes();
            }
            else
            {
                int read = Bass.ChannelGetData(_stream, _fftBuffer, (int)DataFlags.FFT2048);
                if (read <= 0)
                {
                    Array.Clear(bandsOut, 0, bandsOut.Length);
                    return false;
                }
                fftSize = 2048;
                numBins = 1024;
                magnitudes = _fftBuffer;
            }

            double binHz = sampleRate / fftSize;
            int nBands = bandsOut.Length;
            const double minFreq = 28.0;
            const double maxFreq = 16500.0;
            double ratio = Math.Pow(maxFreq / minFreq, 1.0 / nBands);

            for (int i = 0; i < nBands; i++)
            {
                double fLo = minFreq * Math.Pow(ratio, i);
                double fHi = fLo * ratio;
                int bLo = Math.Clamp((int)Math.Floor(fLo / binHz), 1, numBins - 1);
                int bHi = Math.Clamp((int)Math.Floor(fHi / binHz), bLo, numBins - 1);

                float peak = 0f;
                for (int b = bLo; b <= bHi; b++)
                {
                    float m = magnitudes[b];
                    if (m > peak) peak = m;
                }

                // Compensación perceptual suave hacia agudos para balance visual musical
                float tilt = 0.65f + 1.8f * MathF.Pow((i + 1f) / nBands, 0.55f);
                double db = 20.0 * Math.Log10(Math.Max(1e-6, peak * tilt));
                bandsOut[i] = (float)Math.Clamp((db + 62.0) / 56.0, 0.0, 1.0);
            }

            return true;
        }

        private float[] ComputeExclusiveFftMagnitudes()
        {
            const int n = 1024;
            float[] real = new float[n];
            float[] imag = new float[n];

            lock (_ringLock)
            {
                int writeIdx = _exclusiveRingWritePos;
                for (int i = 0; i < n; i++)
                {
                    int srcIdx = (writeIdx - n + i + _exclusivePcmRing.Length) & (_exclusivePcmRing.Length - 1);
                    real[i] = _exclusivePcmRing[srcIdx] * FftHann1024[i];
                }
            }

            for (int i = 0; i < n; i++)
            {
                int j = FftBitRev1024[i];
                if (i < j) (real[i], real[j]) = (real[j], real[i]);
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    int tbl = 0;
                    for (int k = 0; k < half; k++)
                    {
                        float wRe = FftCos1024[tbl];
                        float wIm = FftSin1024[tbl];
                        tbl += step;

                        int even = i + k;
                        int odd = even + half;
                        float tRe = wRe * real[odd] - wIm * imag[odd];
                        float tIm = wRe * imag[odd] + wIm * real[odd];
                        real[odd] = real[even] - tRe;
                        imag[odd] = imag[even] - tIm;
                        real[even] += tRe;
                        imag[even] += tIm;
                    }
                }
            }

            float[] mags = new float[512];
            for (int k = 0; k < 512; k++)
            {
                mags[k] = MathF.Sqrt(real[k] * real[k] + imag[k] * imag[k]) / 512f;
            }
            return mags;
        }

        private static int[] BuildBitRev(int n)
        {
            int[] rev = new int[n];
            int bits = (int)Math.Log2(n);
            for (int i = 0; i < n; i++)
            {
                int r = 0, x = i;
                for (int b = 0; b < bits; b++) { r = (r << 1) | (x & 1); x >>= 1; }
                rev[i] = r;
            }
            return rev;
        }

        private static float[] BuildHann(int n)
        {
            float[] w = new float[n];
            for (int i = 0; i < n; i++) w[i] = 0.5f * (1f - MathF.Cos((2f * MathF.PI * i) / (n - 1)));
            return w;
        }

        private static float[] BuildTrigCos(int n)
        {
            float[] c = new float[n / 2];
            for (int k = 0; k < c.Length; k++) c[k] = MathF.Cos(-2f * MathF.PI * k / n);
            return c;
        }

        private static float[] BuildTrigSin(int n)
        {
            float[] s = new float[n / 2];
            for (int k = 0; k < s.Length; k++) s[k] = MathF.Sin(-2f * MathF.PI * k / n);
            return s;
        }

        private sealed class BassExclusiveWaveProvider : NAudio.Wave.IWaveProvider
        {
            private readonly BassAudioEngine _engine;
            private readonly NAudio.Wave.WaveFormat _format;
            private float[] _mixBuf = Array.Empty<float>();
            private float[] _oldBuf = Array.Empty<float>();
            private float[] _resampOut = Array.Empty<float>();

            public BassExclusiveWaveProvider(BassAudioEngine engine, NAudio.Wave.WaveFormat format)
            {
                _engine = engine;
                _format = format;
            }

            public NAudio.Wave.WaveFormat WaveFormat => _format;

            public int Read(byte[] buffer, int offset, int count)
            {
                int outCh = Math.Max(1, _format.Channels);
                int bytesPerSample = Math.Max(2, _format.BitsPerSample / 8);
                int outFrameBytes = outCh * bytesPerSample;
                int outFrames = count / outFrameBytes;
                if (outFrames <= 0)
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                int srcRate = _engine._exclusiveSrcRate;
                int srcCh = _engine._exclusiveSrcChannels;
                int outRate = _format.SampleRate;
                int srcFrames = outRate == srcRate
                    ? outFrames
                    : Math.Max(1, (int)Math.Ceiling(outFrames * ((double)srcRate / outRate)) + 1);

                int srcSampleCount = srcFrames * srcCh;
                if (_mixBuf.Length < srcSampleCount) _mixBuf = new float[srcSampleCount];
                if (_oldBuf.Length < srcSampleCount) _oldBuf = new float[srcSampleCount];

                Array.Clear(_mixBuf, 0, srcSampleCount);

                bool hitEnd = false;
                lock (_engine._lock)
                {
                    int mainStream = _engine._stream;
                    int fadingStream = _engine._fadingOutStream;
                    float mainGain = fadingStream != 0 ? _engine._exclusiveNewGain : _engine._multiOutput.GetPrimaryDeviceEffectiveVolume();
                    float oldGain = _engine._exclusiveOldGain;

                    if (mainStream != 0)
                    {
                        int bytesWanted = srcSampleCount * sizeof(float);
                        int read = Bass.ChannelGetData(mainStream, _mixBuf, bytesWanted);
                        if (read > 0)
                        {
                            int samplesGot = read / sizeof(float);
                            for (int i = 0; i < samplesGot; i++)
                            {
                                _mixBuf[i] *= mainGain;
                            }
                        }
                        else if (fadingStream == 0)
                        {
                            hitEnd = true;
                        }
                    }

                    if (fadingStream != 0 && oldGain > 0.0001f)
                    {
                        int bytesWanted = srcSampleCount * sizeof(float);
                        int readOld = Bass.ChannelGetData(fadingStream, _oldBuf, bytesWanted);
                        if (readOld > 0)
                        {
                            int samplesOld = Math.Min(srcSampleCount, readOld / sizeof(float));
                            for (int i = 0; i < samplesOld; i++)
                            {
                                _mixBuf[i] += _oldBuf[i] * oldGain;
                            }
                        }
                    }
                }

                if (hitEnd && !_engine._exclusiveEndSignaled && !_engine._crossfadeTriggeredForCurrentTrack)
                {
                    var endCb = _engine._endSyncProc;
                    if (endCb != null)
                    {
                        _ = Task.Run(() => _engine.OnTrackEndSync(0, _engine._stream, 0, IntPtr.Zero));
                    }
                }

                // Guardar mezcla mono en el buffer circular para el analizador de espectro FFT en tiempo real
                lock (_engine._ringLock)
                {
                    int ringMask = _engine._exclusivePcmRing.Length - 1;
                    for (int f = 0; f < srcFrames; f++)
                    {
                        int idx = f * srcCh;
                        float mono = srcCh >= 2 ? 0.5f * (_mixBuf[idx] + _mixBuf[idx + 1]) : _mixBuf[idx];
                        _engine._exclusivePcmRing[_engine._exclusiveRingWritePos & ringMask] = mono;
                        _engine._exclusiveRingWritePos = (_engine._exclusiveRingWritePos + 1) & ringMask;
                    }
                }

                float[] finalSamples;
                if (outRate == srcRate && outCh == srcCh)
                {
                    finalSamples = _mixBuf;
                }
                else
                {
                    int outSampleCount = outFrames * outCh;
                    if (_resampOut.Length < outSampleCount) _resampOut = new float[outSampleCount];
                    double step = (double)srcRate / outRate;
                    for (int f = 0; f < outFrames; f++)
                    {
                        double pos = f * step;
                        int i0 = Math.Min(srcFrames - 1, (int)pos);
                        int i1 = Math.Min(srcFrames - 1, i0 + 1);
                        float frac = (float)(pos - i0);
                        for (int c = 0; c < outCh; c++)
                        {
                            int sCh = Math.Min(c, srcCh - 1);
                            float a = _mixBuf[i0 * srcCh + sCh];
                            float b = _mixBuf[i1 * srcCh + sCh];
                            _resampOut[f * outCh + c] = a + (b - a) * frac;
                        }
                    }
                    finalSamples = _resampOut;
                }

                int totalOutSamples = outFrames * outCh;
                int dst = offset;
                if (_format.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat)
                {
                    Buffer.BlockCopy(finalSamples, 0, buffer, offset, totalOutSamples * sizeof(float));
                }
                else if (_format.BitsPerSample == 24)
                {
                    for (int i = 0; i < totalOutSamples; i++)
                    {
                        float s = Math.Clamp(finalSamples[i], -1f, 1f);
                        int v = (int)(s * 8388607f);
                        buffer[dst++] = (byte)(v & 0xFF);
                        buffer[dst++] = (byte)((v >> 8) & 0xFF);
                        buffer[dst++] = (byte)((v >> 16) & 0xFF);
                    }
                }
                else
                {
                    for (int i = 0; i < totalOutSamples; i++)
                    {
                        float s = Math.Clamp(finalSamples[i], -1f, 1f);
                        short v = (short)(s * 32767f);
                        buffer[dst++] = (byte)(v & 0xFF);
                        buffer[dst++] = (byte)((v >> 8) & 0xFF);
                    }
                }

                int bytesWritten = totalOutSamples * bytesPerSample;
                if (bytesWritten < count)
                {
                    Array.Clear(buffer, offset + bytesWritten, count - bytesWritten);
                }
                return count;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                StopAndFreeFadingOutStreamLocked();
                DisposeExclusiveOutLocked();
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
