using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ManagedBass;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Audio
{
    public class MultiOutputDeviceService : IDisposable
    {
        private readonly ObservableCollection<AudioOutputDevice> _devices = new();
        public ReadOnlyObservableCollection<AudioOutputDevice> Devices { get; }

        private int _currentStreamHandle = 0;
        private int _dspHandle = 0;
        private readonly DSPProcedure _dspProc;
        private AudioOutputDevice[] _activeSecondaryDevices = Array.Empty<AudioOutputDevice>();
        private AudioOutputDevice? _primaryDevice;

        // Local Kaki EQ player stream format
        private int _localSampleRate = 48000;
        private int _localChannels = 2;
        private bool _localIsFloat = true;

        // System-wide WASAPI capture stream format
        private int _routingSampleRate = 48000;
        private int _routingChannels = 2;
        private bool _routingIsFloat = true;
        private bool _isCapturingVirtualCable = true;

        private bool _isPlaying = false;
        private float _masterVolume = 1.0f;

        private AudioOutputDevice[] _activeRoutingDevices = Array.Empty<AudioOutputDevice>();
        private bool _isSystemRoutingActive = false;

        private readonly object _lock = new();

        public event EventHandler? DevicesUpdated;

        public MultiOutputDeviceService()
        {
            Devices = new ReadOnlyObservableCollection<AudioOutputDevice>(_devices);
            _dspProc = OnDspAudioBlock;
            RefreshDevices();
        }

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Math.Clamp(value, 0f, 1f);
                UpdateAllDeviceVolumes();
            }
        }

        public int GetPrimaryPhysicalDeviceIndex()
        {
            lock (_lock)
            {
                var primary = _devices.FirstOrDefault(d => d.IsPrimary);
                return primary?.DeviceIndex ?? -1;
            }
        }

        public void RefreshDevices()
        {
            lock (_lock)
            {
                int deviceCount = Bass.DeviceCount;
                var currentDevicesMap = _devices.ToDictionary(d => d.DeviceIndex);
                var newDeviceList = new List<AudioOutputDevice>();

                // In BASS:
                // Device 0 = No Sound
                // Device 1 = Windows Default alias
                // Device 2..N = Physical audio endpoints
                int startIndex = (deviceCount > 2) ? 2 : 1;

                // Identify the Windows default physical device (excluding virtual loopback devices)
                int defaultPhysicalIndex = -1;
                for (int i = startIndex; i < deviceCount; i++)
                {
                    if (Bass.GetDeviceInfo(i, out var dInfo) && dInfo.IsEnabled && !IsVirtualCableDevice(dInfo))
                    {
                        if (dInfo.IsDefault)
                        {
                            defaultPhysicalIndex = i;
                            break;
                        }
                        else if (defaultPhysicalIndex == -1)
                        {
                            defaultPhysicalIndex = i;
                        }
                    }
                }
                if (defaultPhysicalIndex == -1 && startIndex < deviceCount)
                {
                    defaultPhysicalIndex = startIndex;
                }

                for (int i = startIndex; i < deviceCount; i++)
                {
                    var info = new DeviceInfo();
                    if (!Bass.GetDeviceInfo(i, out info)) continue;

                    // Skip disabled devices
                    if (!info.IsEnabled) continue;

                    // Skip virtual loopback device: it acts as input to Kaki EQ, not an output destination
                    if (IsVirtualCableDevice(info)) continue;

                    bool isPrimary = (i == defaultPhysicalIndex);

                    if (currentDevicesMap.TryGetValue(i, out var existing))
                    {
                        existing.Name = info.Name;
                        existing.IsDefault = info.IsDefault;
                        existing.IsPrimary = isPrimary;
                        newDeviceList.Add(existing);
                    }
                    else
                    {
                        var dev = new AudioOutputDevice
                        {
                            DeviceIndex = i,
                            Id = $"BASS_DEV_{i}_{info.Driver}",
                            Name = info.Name,
                            Driver = info.Driver ?? string.Empty,
                            IsDefault = info.IsDefault,
                            IsPrimary = isPrimary,
                            Volume = 1.0f,
                            IsEnabled = isPrimary // Primary default device is enabled by default
                        };

                        CategorizeDevice(dev);
                        ConfigureDeviceDelayBuffers(dev);
                        newDeviceList.Add(dev);
                    }
                }

                _devices.Clear();
                foreach (var d in newDeviceList)
                {
                    _devices.Add(d);
                }

                _primaryDevice = _devices.FirstOrDefault(d => d.IsPrimary);
                UpdateActiveSecondaryStreamsArray();
                if (_isSystemRoutingActive)
                {
                    UpdateRoutingPushStreamsArray();
                }
            }

            DevicesUpdated?.Invoke(this, EventArgs.Empty);
        }

        public static bool IsVirtualCableDevice(DeviceInfo info)
        {
            string name = info.Name ?? string.Empty;
            string driver = info.Driver ?? string.Empty;
            return name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Kaki EQ", StringComparison.OrdinalIgnoreCase) ||
                   driver.Contains("vbaudio", StringComparison.OrdinalIgnoreCase);
        }

        private static void CategorizeDevice(AudioOutputDevice dev)
        {
            string lower = dev.Name.ToLowerInvariant();

            if (lower.Contains("bluetooth") || lower.Contains("hands-free") || lower.Contains("a2dp") ||
                lower.Contains("buds") || lower.Contains("wireless") || lower.Contains("airpod") ||
                lower.Contains("qcy") || lower.Contains("jbl") || lower.Contains("sony") ||
                lower.Contains("p9") || lower.Contains("a520") || lower.Contains("bt-") || lower.Contains("car-kit"))
            {
                dev.DeviceTypeDescription = "Bluetooth Inalámbrico";
                dev.IconGlyph = "\uE702"; // Bluetooth
            }
            else if (lower.Contains("headphone") || lower.Contains("audífon") || lower.Contains("auricular") || lower.Contains("headset"))
            {
                dev.DeviceTypeDescription = "Audífonos";
                dev.IconGlyph = "\uE7F6"; // Headset
            }
            else if (lower.Contains("hdmi") || lower.Contains("pantalla") || lower.Contains("display") || lower.Contains("tv"))
            {
                dev.DeviceTypeDescription = "Pantalla / HDMI";
                dev.IconGlyph = "\uE7F4"; // Display
            }
            else if (lower.Contains("usb") || lower.Contains("dac") || lower.Contains("focusrite") || lower.Contains("audiobox"))
            {
                dev.DeviceTypeDescription = "Interfaz USB / DAC";
                dev.IconGlyph = "\uECAD"; // USB
            }
            else
            {
                dev.DeviceTypeDescription = "Altavoces del Sistema";
                dev.IconGlyph = "\uE767"; // Speaker
            }
        }

        private void ConfigureDeviceDelayBuffers(AudioOutputDevice dev)
        {
            int localBytesPerSample = _localIsFloat ? 4 : 2;
            dev.LocalDelayBuffer.Configure(_localSampleRate, _localChannels, localBytesPerSample, dev.LatencyCompensationMs);

            int routingBytesPerSample = _routingIsFloat ? 4 : 2;
            dev.RoutingDelayBuffer.Configure(_routingSampleRate, _routingChannels, routingBytesPerSample, dev.LatencyCompensationMs);
        }

        public void SetDeviceLatency(AudioOutputDevice device, int latencyMs)
        {
            if (device == null) return;
            lock (_lock)
            {
                device.LatencyCompensationMs = Math.Clamp(latencyMs, 0, 300);
                ConfigureDeviceDelayBuffers(device);
            }
        }

        public void AttachToStream(int streamHandle)
        {
            lock (_lock)
            {
                if (_currentStreamHandle != 0 && _dspHandle != 0)
                {
                    Bass.ChannelRemoveDSP(_currentStreamHandle, _dspHandle);
                    _dspHandle = 0;
                }

                _currentStreamHandle = streamHandle;

                if (streamHandle == 0)
                {
                    StopAllSecondaryStreams();
                    return;
                }

                if (Bass.ChannelGetInfo(streamHandle, out var info))
                {
                    _localSampleRate = info.Frequency > 0 ? info.Frequency : 48000;
                    _localChannels = info.Channels > 0 ? info.Channels : 2;
                    _localIsFloat = info.Flags.HasFlag(BassFlags.Float);
                }

                foreach (var d in _devices)
                {
                    int localBytesPerSample = _localIsFloat ? 4 : 2;
                    d.LocalDelayBuffer.Configure(_localSampleRate, _localChannels, localBytesPerSample, d.LatencyCompensationMs);
                }

                // Recreate all active secondary local push streams with matching sample rate
                RecreateSecondaryPushStreams();

                // Attach DSP to stream (priority -10 so it runs AFTER EQ DSP)
                _dspHandle = Bass.ChannelSetDSP(streamHandle, _dspProc, IntPtr.Zero, -10);

                // Update primary volume
                UpdatePrimaryDeviceVolume();
            }
        }

        private void OnDspAudioBlock(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (length <= 0 || buffer == IntPtr.Zero) return;

            // If System Routing is capturing a physical device directly (fallback mode without Virtual Cable),
            // WasapiLoopbackCapture already captures this local stream from the primary speaker and distributes it.
            if (_isSystemRoutingActive && !_isCapturingVirtualCable)
            {
                return;
            }

            int bytesPerSample = _localIsFloat ? 4 : 2;
            int bytesPerMs = Math.Max(4, (_localSampleRate * _localChannels * bytesPerSample) / 1000);

            // 1. Distribute audio buffer to all secondary active local push streams
            var secondaryDevices = _activeSecondaryDevices;
            for (int i = 0; i < secondaryDevices.Length; i++)
            {
                var dev = secondaryDevices[i];
                int pushStream = dev.PushStreamHandle;
                if (pushStream != 0)
                {
                    IntPtr outPtr = dev.LatencyCompensationMs > 0
                        ? dev.LocalDelayBuffer.ProcessOutOfPlace(buffer, length)
                        : buffer;
                    PushDataWithDriftGuard(pushStream, outPtr, length, bytesPerMs);
                }
            }

            // 2. If the primary device itself has latency compensation set (e.g. delaying wired speakers to match Bluetooth),
            // delay the primary stream buffer in-place!
            var primary = _primaryDevice;
            if (primary != null && primary.LatencyCompensationMs > 0)
            {
                primary.LocalDelayBuffer.ProcessInPlace(buffer, length);
            }
        }

        public void SetDeviceEnabled(AudioOutputDevice device, bool enabled)
        {
            if (device == null) return;
            lock (_lock)
            {
                bool stateChanged = (device.IsEnabled != enabled);
                device.IsEnabled = enabled;

                // 1. Update System Routing stream for this device if routing is active
                if (_isSystemRoutingActive)
                {
                    bool shouldRouteToDevice = enabled && (_isCapturingVirtualCable || !device.IsPrimary);
                    if (shouldRouteToDevice)
                    {
                        EnsureRoutingPushStreamForDevice(device);
                    }
                    else
                    {
                        StopRoutingPushStreamForDevice(device);
                    }
                    UpdateRoutingPushStreamsArray();
                }

                // 2. Update Local Player stream for this device
                if (device.IsPrimary)
                {
                    UpdatePrimaryDeviceVolume();
                }
                else
                {
                    if (enabled && _currentStreamHandle != 0)
                    {
                        EnsureLocalPushStreamForDevice(device);
                    }
                    else if (!enabled)
                    {
                        StopLocalPushStreamForDevice(device);
                    }
                    UpdateActiveSecondaryStreamsArray();
                }
            }
        }

        public void SetDeviceVolume(AudioOutputDevice device, float volume)
        {
            if (device == null) return;
            lock (_lock)
            {
                device.Volume = volume;
                float effectiveVol = device.IsMuted ? 0f : (device.Volume * _masterVolume);

                if (device.PushStreamHandle != 0)
                {
                    Bass.ChannelSetAttribute(device.PushStreamHandle, ChannelAttribute.Volume, effectiveVol);
                }

                if (device.RoutingPushStreamHandle != 0)
                {
                    Bass.ChannelSetAttribute(device.RoutingPushStreamHandle, ChannelAttribute.Volume, effectiveVol);
                }

                if (device.IsPrimary)
                {
                    UpdatePrimaryDeviceVolume();
                }
            }
        }

        public void SetDeviceMuted(AudioOutputDevice device, bool muted)
        {
            if (device == null) return;
            lock (_lock)
            {
                device.IsMuted = muted;
                float effectiveVol = muted ? 0f : (device.Volume * _masterVolume);

                if (device.PushStreamHandle != 0)
                {
                    Bass.ChannelSetAttribute(device.PushStreamHandle, ChannelAttribute.Volume, effectiveVol);
                }

                if (device.RoutingPushStreamHandle != 0)
                {
                    Bass.ChannelSetAttribute(device.RoutingPushStreamHandle, ChannelAttribute.Volume, effectiveVol);
                }

                if (device.IsPrimary)
                {
                    UpdatePrimaryDeviceVolume();
                }
            }
        }

        private void UpdateAllDeviceVolumes()
        {
            lock (_lock)
            {
                UpdatePrimaryDeviceVolume();

                foreach (var d in _devices)
                {
                    float vol = d.IsMuted ? 0f : (d.Volume * _masterVolume);
                    if (d.PushStreamHandle != 0)
                    {
                        Bass.ChannelSetAttribute(d.PushStreamHandle, ChannelAttribute.Volume, vol);
                    }
                    if (d.RoutingPushStreamHandle != 0)
                    {
                        Bass.ChannelSetAttribute(d.RoutingPushStreamHandle, ChannelAttribute.Volume, vol);
                    }
                }
            }
        }

        private void UpdatePrimaryDeviceVolume()
        {
            if (_currentStreamHandle == 0) return;

            var primary = _devices.FirstOrDefault(d => d.IsPrimary);
            float vol = 0f;
            if (primary != null && primary.IsEnabled && !primary.IsMuted)
            {
                vol = primary.Volume * _masterVolume;
            }

            Bass.ChannelSetAttribute(_currentStreamHandle, ChannelAttribute.Volume, vol);
        }

        private int CreateAndPrimePushStream(AudioOutputDevice device, int sampleRate, int channels, bool isFloat, bool shouldPlayImmediately)
        {
            int prevDevice = Bass.CurrentDevice;
            try
            {
                if (!Bass.Init(device.DeviceIndex, sampleRate, DeviceInitFlags.Default, IntPtr.Zero))
                {
                    int err = (int)Bass.LastError;
                    if (err != 14 && err != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MultiOutput] Bass.Init failed on device {device.DeviceIndex}: {Bass.LastError}");
                    }
                }

                Bass.CurrentDevice = device.DeviceIndex;
                Bass.Start();

                var flags = isFloat ? BassFlags.Float : BassFlags.Default;
                int pushStream = Bass.CreateStream(sampleRate, channels, flags, StreamProcedureType.Push);
                if (pushStream == 0)
                {
                    pushStream = Bass.CreateStream(sampleRate, channels, BassFlags.Default, StreamProcedureType.Push);
                }

                if (pushStream != 0)
                {
                    // Set compact low-latency buffer (65ms) on the push stream
                    Bass.ChannelSetAttribute(pushStream, ChannelAttribute.Buffer, 0.065f);

                    float targetVol = device.IsMuted ? 0f : (device.Volume * _masterVolume);

                    if (shouldPlayImmediately)
                    {
                        // Pre-fill 16ms of zero-valued silence cushion before starting playback
                        // so the hardware endpoint never starts in an underrun/starved state
                        int bytesPerSample = isFloat ? 4 : 2;
                        int frameAlign = channels * bytesPerSample;
                        int silenceBytes = ((sampleRate * frameAlign * 16 / 1000) / frameAlign) * frameAlign;
                        if (silenceBytes > 0)
                        {
                            byte[] silence = new byte[silenceBytes];
                            Bass.StreamPutData(pushStream, silence, silence.Length);
                        }

                        // Start at 0 volume and smoothly ramp over 45ms to eliminate any startup click/interference
                        Bass.ChannelSetAttribute(pushStream, ChannelAttribute.Volume, 0f);
                        Bass.ChannelPlay(pushStream, false);
                        if (targetVol > 0f)
                        {
                            Bass.ChannelSlideAttribute(pushStream, ChannelAttribute.Volume, targetVol, 45);
                        }
                    }
                    else
                    {
                        Bass.ChannelSetAttribute(pushStream, ChannelAttribute.Volume, targetVol);
                    }
                }

                return pushStream;
            }
            finally
            {
                Bass.CurrentDevice = prevDevice;
            }
        }

        private void EnsureLocalPushStreamForDevice(AudioOutputDevice device)
        {
            if (device.PushStreamHandle != 0)
            {
                // Verify format matches current local stream
                if (device.LocalStreamSampleRate == _localSampleRate &&
                    device.LocalStreamChannels == _localChannels &&
                    device.LocalStreamIsFloat == _localIsFloat)
                {
                    if (_isPlaying && Bass.ChannelIsActive(device.PushStreamHandle) != PlaybackState.Playing)
                    {
                        Bass.ChannelPlay(device.PushStreamHandle, false);
                    }
                    return;
                }

                StopLocalPushStreamForDevice(device);
            }

            device.LocalDelayBuffer.Reset();
            int handle = CreateAndPrimePushStream(device, _localSampleRate, _localChannels, _localIsFloat, _isPlaying);
            if (handle != 0)
            {
                device.PushStreamHandle = handle;
                device.LocalStreamSampleRate = _localSampleRate;
                device.LocalStreamChannels = _localChannels;
                device.LocalStreamIsFloat = _localIsFloat;
            }
        }

        private void StopLocalPushStreamForDevice(AudioOutputDevice device)
        {
            if (device.PushStreamHandle != 0)
            {
                int handle = device.PushStreamHandle;
                device.PushStreamHandle = 0;
                Bass.ChannelStop(handle);
                Bass.StreamFree(handle);
            }
            device.LocalDelayBuffer.Reset();
        }

        private void EnsureRoutingPushStreamForDevice(AudioOutputDevice device)
        {
            if (device.RoutingPushStreamHandle != 0)
            {
                if (device.RoutingStreamSampleRate == _routingSampleRate &&
                    device.RoutingStreamChannels == _routingChannels &&
                    device.RoutingStreamIsFloat == _routingIsFloat)
                {
                    if (Bass.ChannelIsActive(device.RoutingPushStreamHandle) != PlaybackState.Playing)
                    {
                        Bass.ChannelPlay(device.RoutingPushStreamHandle, false);
                    }
                    return;
                }

                StopRoutingPushStreamForDevice(device);
            }

            device.RoutingDelayBuffer.Reset();
            int handle = CreateAndPrimePushStream(device, _routingSampleRate, _routingChannels, _routingIsFloat, true);
            if (handle != 0)
            {
                device.RoutingPushStreamHandle = handle;
                device.RoutingStreamSampleRate = _routingSampleRate;
                device.RoutingStreamChannels = _routingChannels;
                device.RoutingStreamIsFloat = _routingIsFloat;
            }
        }

        private void StopRoutingPushStreamForDevice(AudioOutputDevice device)
        {
            if (device.RoutingPushStreamHandle != 0)
            {
                int handle = device.RoutingPushStreamHandle;
                device.RoutingPushStreamHandle = 0;
                Bass.ChannelStop(handle);
                Bass.StreamFree(handle);
            }
            device.RoutingDelayBuffer.Reset();
        }

        private void RecreateSecondaryPushStreams()
        {
            foreach (var d in _devices)
            {
                if (!d.IsPrimary)
                {
                    StopLocalPushStreamForDevice(d);
                    if (d.IsEnabled)
                    {
                        EnsureLocalPushStreamForDevice(d);
                    }
                }
            }
            UpdateActiveSecondaryStreamsArray();
        }

        private void StopAllSecondaryStreams()
        {
            foreach (var d in _devices)
            {
                if (!d.IsPrimary)
                {
                    StopLocalPushStreamForDevice(d);
                }
            }
            UpdateActiveSecondaryStreamsArray();
        }

        private void UpdateActiveSecondaryStreamsArray()
        {
            _primaryDevice = _devices.FirstOrDefault(d => d.IsPrimary);
            _activeSecondaryDevices = _devices
                .Where(d => !d.IsPrimary && d.IsEnabled && d.PushStreamHandle != 0)
                .ToArray();
        }

        public void ConfigureStreamFormat(int sampleRate, int channels, bool isFloat, bool isCapturingVirtualCable = true)
        {
            lock (_lock)
            {
                _routingSampleRate = sampleRate > 0 ? sampleRate : 48000;
                _routingChannels = channels > 0 ? channels : 2;
                _routingIsFloat = isFloat;
                _isCapturingVirtualCable = isCapturingVirtualCable;

                int routingBytesPerSample = _routingIsFloat ? 4 : 2;
                foreach (var d in _devices)
                {
                    d.RoutingDelayBuffer.Configure(_routingSampleRate, _routingChannels, routingBytesPerSample, d.LatencyCompensationMs);
                }
            }
        }

        public bool IsSystemRoutingActive
        {
            get => _isSystemRoutingActive;
            set
            {
                lock (_lock)
                {
                    if (_isSystemRoutingActive != value)
                    {
                        _isSystemRoutingActive = value;
                        if (_isSystemRoutingActive)
                        {
                            StartSystemRoutingPushStreams();
                        }
                        else
                        {
                            StopSystemRoutingPushStreams();
                        }
                    }
                }
            }
        }

        public void StartSystemRoutingPushStreams()
        {
            lock (_lock)
            {
                foreach (var d in _devices)
                {
                    // When capturing the Virtual Cable, route to ALL enabled physical devices (Primary + Secondary).
                    // When capturing a physical device directly (fallback without Virtual Cable), skip Primary to prevent feedback echo.
                    bool shouldRoute = d.IsEnabled && (_isCapturingVirtualCable || !d.IsPrimary);
                    if (shouldRoute)
                    {
                        StopRoutingPushStreamForDevice(d);
                        EnsureRoutingPushStreamForDevice(d);
                    }
                    else
                    {
                        StopRoutingPushStreamForDevice(d);
                    }
                }
                UpdateRoutingPushStreamsArray();
            }
        }

        public void StopSystemRoutingPushStreams()
        {
            lock (_lock)
            {
                foreach (var d in _devices)
                {
                    StopRoutingPushStreamForDevice(d);
                }
                UpdateRoutingPushStreamsArray();
            }
        }

        private void UpdateRoutingPushStreamsArray()
        {
            _activeRoutingDevices = _devices
                .Where(d => d.IsEnabled && d.RoutingPushStreamHandle != 0 && (_isCapturingVirtualCable || !d.IsPrimary))
                .ToArray();
        }

        public void DistributeSystemAudioBlock(IntPtr buffer, int length)
        {
            if (length <= 0 || buffer == IntPtr.Zero) return;

            int bytesPerSample = _routingIsFloat ? 4 : 2;
            int bytesPerMs = Math.Max(4, (_routingSampleRate * _routingChannels * bytesPerSample) / 1000);

            var routingDevices = _activeRoutingDevices;
            for (int i = 0; i < routingDevices.Length; i++)
            {
                var dev = routingDevices[i];
                int pushStream = dev.RoutingPushStreamHandle;
                if (pushStream != 0)
                {
                    IntPtr outPtr = dev.LatencyCompensationMs > 0
                        ? dev.RoutingDelayBuffer.ProcessOutOfPlace(buffer, length)
                        : buffer;
                    PushDataWithDriftGuard(pushStream, outPtr, length, bytesPerMs);
                }
            }
        }

        /// <summary>
        /// Pushes PCM data to a BASS push stream while enforcing a strict low-latency buffer ceiling (~45ms)
        /// so clock drift or Bluetooth packet jitter never causes progressive audio desync (desfase).
        /// </summary>
        private static void PushDataWithDriftGuard(int pushStream, IntPtr buffer, int length, int bytesPerMs)
        {
            if (pushStream == 0 || length <= 0) return;

            int avail = Bass.ChannelGetData(pushStream, IntPtr.Zero, (int)DataFlags.Available);
            int queued = Bass.StreamPutData(pushStream, IntPtr.Zero, 0);
            int totalBuffered = (avail > 0 ? avail : 0) + (queued > 0 ? queued : 0);

            // Ceiling: 48ms of buffered audio + current block size.
            // If buffered audio exceeds this ceiling, the device has drifted behind; flush stale backlog to snap back in sync.
            int maxAllowedBytes = (48 * bytesPerMs) + length;
            if (totalBuffered > maxAllowedBytes)
            {
                Bass.ChannelSetPosition(pushStream, 0, PositionFlags.Bytes);
            }

            Bass.StreamPutData(pushStream, buffer, length);

            var state = Bass.ChannelIsActive(pushStream);
            if (state != PlaybackState.Playing)
            {
                Bass.ChannelPlay(pushStream, false);
            }
        }

        public void NotifyPlaybackState(bool isPlaying)
        {
            lock (_lock)
            {
                _isPlaying = isPlaying;

                foreach (var dev in _activeSecondaryDevices)
                {
                    int stream = dev.PushStreamHandle;
                    if (stream != 0)
                    {
                        if (isPlaying)
                        {
                            Bass.ChannelPlay(stream, false);
                        }
                        else
                        {
                            Bass.ChannelPause(stream);
                        }
                    }
                }
            }
        }

        public void FlushSecondaryBuffers()
        {
            lock (_lock)
            {
                foreach (var dev in _devices)
                {
                    dev.LocalDelayBuffer.Reset();
                    int stream = dev.PushStreamHandle;
                    if (stream != 0)
                    {
                        Bass.ChannelSetPosition(stream, 0, PositionFlags.Bytes);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_currentStreamHandle != 0 && _dspHandle != 0)
                {
                    Bass.ChannelRemoveDSP(_currentStreamHandle, _dspHandle);
                    _dspHandle = 0;
                }

                StopAllSecondaryStreams();
                StopSystemRoutingPushStreams();
            }
        }
    }
}
