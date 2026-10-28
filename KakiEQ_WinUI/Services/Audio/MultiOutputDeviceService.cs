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
        private int[] _activeSecondaryStreams = Array.Empty<int>();

        private int _streamSampleRate = 48000;
        private int _streamChannels = 2;
        private bool _streamIsFloat = true;
        private bool _isPlaying = false;
        private float _masterVolume = 1.0f;

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
                // When physical devices exist (deviceCount > 2), we enumerate from 2 up to prevent
                // duplicate playback on the default endpoint. If deviceCount <= 2, we fallback to Device 1.
                int startIndex = (deviceCount > 2) ? 2 : 1;

                // Identify the Windows default physical device
                int defaultPhysicalIndex = -1;
                for (int i = startIndex; i < deviceCount; i++)
                {
                    if (Bass.GetDeviceInfo(i, out var dInfo) && dInfo.IsEnabled && dInfo.IsDefault)
                    {
                        defaultPhysicalIndex = i;
                        break;
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

                        // Classify device type & icon
                        CategorizeDevice(dev);

                        newDeviceList.Add(dev);
                    }
                }

                _devices.Clear();
                foreach (var d in newDeviceList)
                {
                    _devices.Add(d);
                }
            }

            DevicesUpdated?.Invoke(this, EventArgs.Empty);
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

        public void AttachToStream(int streamHandle)
        {
            lock (_lock)
            {
                // Clean up previous DSP
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
                    _streamSampleRate = info.Frequency > 0 ? info.Frequency : 48000;
                    _streamChannels = info.Channels > 0 ? info.Channels : 2;
                    _streamIsFloat = info.Flags.HasFlag(BassFlags.Float);
                }

                // Recreate all active secondary push streams with matching sample rate
                RecreateSecondaryPushStreams();

                // Attach DSP to stream
                _dspHandle = Bass.ChannelSetDSP(streamHandle, _dspProc, IntPtr.Zero, 0);

                // Update primary volume
                UpdatePrimaryDeviceVolume();
            }
        }

        private void OnDspAudioBlock(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            // ZERO-ALLOCATION FAST LOOP
            // Distribute audio buffer directly to all secondary active push streams
            var streams = _activeSecondaryStreams;
            for (int i = 0; i < streams.Length; i++)
            {
                int pushStream = streams[i];
                if (pushStream != 0)
                {
                    Bass.StreamPutData(pushStream, buffer, length);
                }
            }
        }

        public void SetDeviceEnabled(AudioOutputDevice device, bool enabled)
        {
            lock (_lock)
            {
                device.IsEnabled = enabled;

                if (_isSystemRoutingActive)
                {
                    if (enabled) StartPushStreamForDevice(device);
                    else StopPushStreamForDevice(device);
                    UpdateRoutingPushStreamsArray();
                }
                else
                {
                    if (device.IsPrimary)
                    {
                        UpdatePrimaryDeviceVolume();
                    }
                    else
                    {
                        if (enabled)
                        {
                            StartPushStreamForDevice(device);
                        }
                        else
                        {
                            StopPushStreamForDevice(device);
                        }
                        UpdateActiveSecondaryStreamsArray();
                    }
                }
            }
        }

        public void SetDeviceVolume(AudioOutputDevice device, float volume)
        {
            lock (_lock)
            {
                device.Volume = volume;

                if (device.PushStreamHandle != 0)
                {
                    float vol = device.IsMuted ? 0f : (device.Volume * _masterVolume);
                    Bass.ChannelSetAttribute(device.PushStreamHandle, ChannelAttribute.Volume, vol);
                }

                if (device.IsPrimary && !_isSystemRoutingActive)
                {
                    UpdatePrimaryDeviceVolume();
                }
            }
        }

        public void SetDeviceMuted(AudioOutputDevice device, bool muted)
        {
            lock (_lock)
            {
                device.IsMuted = muted;

                if (device.PushStreamHandle != 0)
                {
                    float vol = muted ? 0f : (device.Volume * _masterVolume);
                    Bass.ChannelSetAttribute(device.PushStreamHandle, ChannelAttribute.Volume, vol);
                }

                if (device.IsPrimary && !_isSystemRoutingActive)
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
                    if (!d.IsPrimary && d.PushStreamHandle != 0)
                    {
                        float vol = d.IsMuted ? 0f : (d.Volume * _masterVolume);
                        Bass.ChannelSetAttribute(d.PushStreamHandle, ChannelAttribute.Volume, vol);
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

        private void StartPushStreamForDevice(AudioOutputDevice device)
        {
            if (device.PushStreamHandle != 0) return;

            int prevDevice = Bass.CurrentDevice;
            try
            {
                // Initialize BASS device if not already initialized
                if (!Bass.Init(device.DeviceIndex, _streamSampleRate, DeviceInitFlags.Default, IntPtr.Zero))
                {
                    int err = (int)Bass.LastError;
                    // BASS_ERROR_ALREADY (14) means already initialized, which is fine
                    if (err != 14 && err != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MultiOutput] Bass.Init failed on device {device.DeviceIndex}: {Bass.LastError}");
                    }
                }

                Bass.CurrentDevice = device.DeviceIndex;

                var flags = _streamIsFloat ? BassFlags.Float : BassFlags.Default;
                int pushStream = Bass.CreateStream(_streamSampleRate, _streamChannels, flags, StreamProcedureType.Push);
                if (pushStream == 0)
                {
                    // Fallback to standard 16-bit PCM if float is not supported
                    pushStream = Bass.CreateStream(_streamSampleRate, _streamChannels, BassFlags.Default, StreamProcedureType.Push);
                }

                if (pushStream != 0)
                {
                    float vol = device.IsMuted ? 0f : (device.Volume * _masterVolume);
                    Bass.ChannelSetAttribute(pushStream, ChannelAttribute.Volume, vol);

                    if (_isPlaying)
                    {
                        Bass.ChannelPlay(pushStream, false);
                    }

                    device.PushStreamHandle = pushStream;
                }
            }
            finally
            {
                Bass.CurrentDevice = prevDevice;
            }
        }

        private void StopPushStreamForDevice(AudioOutputDevice device)
        {
            if (device.PushStreamHandle != 0)
            {
                int handle = device.PushStreamHandle;
                device.PushStreamHandle = 0;
                Bass.ChannelStop(handle);
                Bass.StreamFree(handle);
            }
        }

        private void RecreateSecondaryPushStreams()
        {
            foreach (var d in _devices)
            {
                if (!d.IsPrimary)
                {
                    StopPushStreamForDevice(d);
                    if (d.IsEnabled)
                    {
                        StartPushStreamForDevice(d);
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
                    StopPushStreamForDevice(d);
                }
            }
            UpdateActiveSecondaryStreamsArray();
        }

        private void UpdateActiveSecondaryStreamsArray()
        {
            _activeSecondaryStreams = _devices
                .Where(d => !d.IsPrimary && d.IsEnabled && d.PushStreamHandle != 0)
                .Select(d => d.PushStreamHandle)
                .ToArray();
        }

        private int[] _allRoutingPushStreams = Array.Empty<int>();
        private bool _isSystemRoutingActive = false;

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
                // In system routing mode, ALL enabled devices (primary AND secondary) need push streams
                foreach (var d in _devices)
                {
                    if (d.IsEnabled)
                    {
                        StartPushStreamForDevice(d);
                    }
                    else
                    {
                        StopPushStreamForDevice(d);
                    }
                }
                UpdateRoutingPushStreamsArray();

                // Start playback on all active push streams
                foreach (var stream in _allRoutingPushStreams)
                {
                    if (stream != 0)
                    {
                        Bass.ChannelPlay(stream, false);
                    }
                }
            }
        }

        public void StopSystemRoutingPushStreams()
        {
            lock (_lock)
            {
                var primary = _devices.FirstOrDefault(d => d.IsPrimary);
                if (primary != null)
                {
                    StopPushStreamForDevice(primary);
                }
                UpdateActiveSecondaryStreamsArray();
            }
        }

        private void UpdateRoutingPushStreamsArray()
        {
            _allRoutingPushStreams = _devices
                .Where(d => d.IsEnabled && d.PushStreamHandle != 0)
                .Select(d => d.PushStreamHandle)
                .ToArray();
        }

        public void DistributeSystemAudioBlock(IntPtr buffer, int length)
        {
            // ZERO-ALLOCATION FAST LOOP FOR SYSTEM AUDIO
            var streams = _allRoutingPushStreams;
            for (int i = 0; i < streams.Length; i++)
            {
                int pushStream = streams[i];
                if (pushStream != 0)
                {
                    Bass.StreamPutData(pushStream, buffer, length);
                }
            }
        }

        public void NotifyPlaybackState(bool isPlaying)
        {
            lock (_lock)
            {
                _isPlaying = isPlaying;

                foreach (var stream in _activeSecondaryStreams)
                {
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
                // Reset/flush secondary push streams so there is no audio buffer desync
                foreach (var stream in _activeSecondaryStreams)
                {
                    if (stream != 0)
                    {
                        // Passing length 0 with BASS_STREAMPROC_END or restarting position resets buffer
                        Bass.ChannelSetPosition(stream, 0);
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
            }
        }
    }
}
