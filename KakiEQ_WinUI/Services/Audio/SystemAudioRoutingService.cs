using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace KakiEQ_WinUI.Services.Audio
{
    public class SystemAudioRoutingService : IDisposable
    {
        private readonly MultiOutputDeviceService _multiOutputService;
        private WasapiCapture? _capture;
        private WasapiOut? _silenceKeeper;
        private MMDevice? _activeCaptureEndpoint;
        private string? _savedPhysicalDefaultEndpointId;
        private bool _isRouting = false;

        // Warmup & smooth anti-interference fade-in state
        private int _discardPacketsRemaining = 0;
        private float _fadeInGain = 0f;
        private long _lastPacketTick = 0;
        private bool _captureIsFloat = true;

        private readonly object _routingLock = new();

        public event EventHandler? RoutingStateChanged;
        public event EventHandler? DriverStatusChanged;

        public bool IsRouting => _isRouting;

        public bool IsDriverInstalled
        {
            get
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    if (endpoints.Any(IsVirtualCableEndpoint))
                    {
                        return true;
                    }
                }
                catch { }

                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\VBAudioVACMME");
                    if (key != null) return true;
                }
                catch { }

                return false;
            }
        }

        public string DriverStatusSummary
        {
            get
            {
                if (IsDriverInstalled)
                {
                    return _isRouting
                        ? "Dispositivo 'Kaki EQ | Audio Virtual' activo: transmitiendo todo el audio de Windows sin desfase a tus salidas."
                        : "Dispositivo 'Kaki EQ | Audio Virtual' listo en Windows 11. Activa el interruptor para transmitir todo el audio.";
                }
                return "Controlador WHQL no instalado. Haz clic en 'Instalar Dispositivo (1 Clic)' para registrarlo en Windows 11.";
            }
        }

        public SystemAudioRoutingService(MultiOutputDeviceService multiOutputService)
        {
            _multiOutputService = multiOutputService ?? throw new ArgumentNullException(nameof(multiOutputService));

            // If a previous session exited while the virtual cable was still set as Windows default output,
            // restore the physical speaker as default until the user turns on routing.
            EnsurePhysicalDefaultWhenIdle();
        }

        private static bool IsVirtualCableEndpoint(MMDevice device)
        {
            try
            {
                string name = device.FriendlyName ?? string.Empty;
                return name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Kaki EQ", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void EnsurePhysicalDefaultWhenIdle()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var currentDefault = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (currentDefault != null && IsVirtualCableEndpoint(currentDefault))
                {
                    var physical = enumerator
                        .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                        .FirstOrDefault(d => !IsVirtualCableEndpoint(d));

                    if (physical != null)
                    {
                        WindowsAudioPolicyConfig.SetDefaultEndpoint(physical.ID);
                        _multiOutputService.RefreshDevices();
                    }
                }
            }
            catch { }
        }

        public void NotifyDriverStatus()
        {
            DriverStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<bool> InstallDriverAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string driverFolder = Path.Combine(baseDir, "Assets", "Driver");
                    string exePath = Path.Combine(driverFolder, "VBCABLE_Setup_x64.exe");

                    if (!File.Exists(exePath))
                    {
                        string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        string altPath = Path.Combine(desktopFolder, "Driver_Audio_Virtual_KakiEQ", "VBCABLE_Setup_x64.exe");
                        if (File.Exists(altPath))
                        {
                            exePath = altPath;
                            driverFolder = Path.GetDirectoryName(altPath)!;
                        }
                    }

                    if (!File.Exists(exePath))
                    {
                        Debug.WriteLine($"[SystemAudioRouting] VBCABLE_Setup_x64.exe not found at {exePath}");
                        return false;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = driverFolder,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        RenameVirtualEndpointToKakiEQ();
                        NotifyDriverStatus();
                        return IsDriverInstalled;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemAudioRouting] InstallDriverAsync error: {ex.Message}");
                }

                NotifyDriverStatus();
                return false;
            });
        }

        public static void RenameVirtualEndpointToKakiEQ()
        {
            try
            {
                using var mediaCatKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\MediaCategories\{B961F7FD-8C2D-4378-9DA3-5A3A89511B74}", true);
                mediaCatKey?.SetValue("Name", "Kaki EQ | Audio Virtual");
            }
            catch { }

            try
            {
                using var renderKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render", true);
                if (renderKey != null)
                {
                    foreach (var subKeyName in renderKey.GetSubKeyNames())
                    {
                        using var propKey = renderKey.OpenSubKey($@"{subKeyName}\Properties", true);
                        if (propKey != null)
                        {
                            var adapterName = propKey.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string ?? string.Empty;
                            var endpointDesc = propKey.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string ?? string.Empty;

                            if ((adapterName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
                                 endpointDesc.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) &&
                                !endpointDesc.Contains("16 Ch", StringComparison.OrdinalIgnoreCase))
                            {
                                propKey.SetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2", "Kaki EQ | Audio Virtual");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static void OpenWindowsSoundSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            }
            catch { }
        }

        public bool StartRouting()
        {
            lock (_routingLock)
            {
                if (_isRouting) return true;

                try
                {
                    MMDevice? virtualCableDevice = null;
                    MMDevice? currentDefaultDevice = null;

                    using (var enumerator = new MMDeviceEnumerator())
                    {
                        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                        // Prefer main 2-channel Kaki EQ / CABLE Input virtual endpoint (avoid 16-ch variant)
                        virtualCableDevice = endpoints.FirstOrDefault(d =>
                            IsVirtualCableEndpoint(d) &&
                            !d.FriendlyName.Contains("16 Ch", StringComparison.OrdinalIgnoreCase))
                            ?? endpoints.FirstOrDefault(IsVirtualCableEndpoint);

                        try
                        {
                            currentDefaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        }
                        catch { }

                        if (currentDefaultDevice != null && !IsVirtualCableEndpoint(currentDefaultDevice))
                        {
                            _savedPhysicalDefaultEndpointId = currentDefaultDevice.ID;
                        }
                        else if (string.IsNullOrEmpty(_savedPhysicalDefaultEndpointId))
                        {
                            var firstPhysical = endpoints.FirstOrDefault(d => !IsVirtualCableEndpoint(d));
                            _savedPhysicalDefaultEndpointId = firstPhysical?.ID;
                        }
                    }

                    bool isUsingVirtualCable = virtualCableDevice != null;
                    if (isUsingVirtualCable && virtualCableDevice != null)
                    {
                        // Automatically switch Windows 11 Default Render Endpoint to 'Kaki EQ | Audio Virtual'
                        // so all system applications immediately route their audio through Kaki EQ.
                        if (currentDefaultDevice == null || currentDefaultDevice.ID != virtualCableDevice.ID)
                        {
                            WindowsAudioPolicyConfig.SetDefaultEndpoint(virtualCableDevice.ID);
                        }
                        _activeCaptureEndpoint = virtualCableDevice;
                    }
                    else
                    {
                        _activeCaptureEndpoint = currentDefaultDevice;
                    }

                    if (_activeCaptureEndpoint == null)
                    {
                        return false;
                    }

                    // 1. Start a silent shared-mode WasapiOut stream on the capture endpoint.
                    // This keeps the Windows CoreAudio clock continuously running even during silence,
                    // preventing WASAPI loopback stalls and startup interference when audio starts/stops.
                    try
                    {
                        _silenceKeeper = new WasapiOut(_activeCaptureEndpoint, AudioClientShareMode.Shared, false, 40);
                        _silenceKeeper.Init(new ContinuousSilenceProvider(_activeCaptureEndpoint.AudioClient.MixFormat));
                        _silenceKeeper.Play();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SystemAudioRouting] SilenceKeeper warning: {ex.Message}");
                        _silenceKeeper?.Dispose();
                        _silenceKeeper = null;
                    }

                    // 2. Open low-latency WASAPI loopback capture (18ms buffer -> ~9ms polling interval instead of 50ms)
                    _capture = new LowLatencyLoopbackCapture(_activeCaptureEndpoint, bufferMs: 18);

                    int sampleRate = _capture.WaveFormat.SampleRate;
                    int channels = _capture.WaveFormat.Channels;
                    _captureIsFloat = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                                      (_capture.WaveFormat is WaveFormatExtensible ext &&
                                       ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));

                    // 3. Configure warmup & anti-click micro fade-in
                    _discardPacketsRemaining = 2; // Discard first 2 packets (~18ms) of driver ring-buffer residual data
                    _fadeInGain = 0f;
                    _lastPacketTick = Environment.TickCount64;

                    // 4. Configure multi-output push streams with exact capture format
                    _multiOutputService.ConfigureStreamFormat(sampleRate, channels, _captureIsFloat, isUsingVirtualCable);
                    _multiOutputService.IsSystemRoutingActive = true;

                    _capture.DataAvailable += OnWasapiDataAvailable;
                    _capture.RecordingStopped += OnCaptureRecordingStopped;

                    _capture.StartRecording();
                    _isRouting = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemAudioRouting] StartRouting error: {ex.Message}");
                    CleanupRoutingResources();
                    RestorePhysicalDefaultEndpoint();
                    RoutingStateChanged?.Invoke(this, EventArgs.Empty);
                    return false;
                }
            }

            RoutingStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void OnCaptureRecordingStopped(object? sender, StoppedEventArgs e)
        {
            bool wasRouting;
            lock (_routingLock)
            {
                wasRouting = _isRouting;
                _isRouting = false;
                _multiOutputService.IsSystemRoutingActive = false;
            }

            if (wasRouting)
            {
                RoutingStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void StopRouting()
        {
            lock (_routingLock)
            {
                if (!_isRouting && _capture == null) return;
                _isRouting = false;
                CleanupRoutingResources();
                RestorePhysicalDefaultEndpoint();
            }

            RoutingStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CleanupRoutingResources()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= OnWasapiDataAvailable;
                    _capture.RecordingStopped -= OnCaptureRecordingStopped;
                    try { _capture.StopRecording(); } catch { }
                    try { _capture.Dispose(); } catch { }
                    _capture = null;
                }
            }
            catch { }

            try
            {
                if (_silenceKeeper != null)
                {
                    try { _silenceKeeper.Stop(); } catch { }
                    try { _silenceKeeper.Dispose(); } catch { }
                    _silenceKeeper = null;
                }
            }
            catch { }

            _activeCaptureEndpoint = null;
            _multiOutputService.IsSystemRoutingActive = false;
        }

        private void RestorePhysicalDefaultEndpoint()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                string? targetId = _savedPhysicalDefaultEndpointId;

                if (string.IsNullOrEmpty(targetId))
                {
                    var firstPhysical = enumerator
                        .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                        .FirstOrDefault(d => !IsVirtualCableEndpoint(d));
                    targetId = firstPhysical?.ID;
                }

                if (!string.IsNullOrEmpty(targetId))
                {
                    WindowsAudioPolicyConfig.SetDefaultEndpoint(targetId);
                }
            }
            catch { }
        }

        private unsafe void OnWasapiDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRouting || e.BytesRecorded <= 0) return;

            long now = Environment.TickCount64;
            long gapMs = now - _lastPacketTick;
            _lastPacketTick = now;

            // If there was a pause/gap in audio delivery (> 220ms), trigger a quick smooth fade-in
            if (gapMs > 220 && _fadeInGain >= 1f)
            {
                _fadeInGain = 0f;
            }

            fixed (byte* pBuffer = e.Buffer)
            {
                if (_discardPacketsRemaining > 0)
                {
                    _discardPacketsRemaining--;
                    // Zero out initial driver warmup packet so no residual buffer click is heard
                    new Span<byte>(pBuffer, e.BytesRecorded).Clear();
                    _multiOutputService.DistributeSystemAudioBlock((IntPtr)pBuffer, e.BytesRecorded);
                    return;
                }

                // Sample-accurate micro fade-in (~30ms) on startup or after stream wake-up
                if (_fadeInGain < 1.0f)
                {
                    if (_captureIsFloat)
                    {
                        float* samples = (float*)pBuffer;
                        int count = e.BytesRecorded / sizeof(float);
                        float step = 1.0f / Math.Max(1, _capture!.WaveFormat.SampleRate * _capture.WaveFormat.Channels * 30 / 1000);
                        for (int i = 0; i < count; i++)
                        {
                            samples[i] *= _fadeInGain;
                            if (_fadeInGain < 1.0f)
                            {
                                _fadeInGain = Math.Min(1.0f, _fadeInGain + step);
                            }
                        }
                    }
                    else
                    {
                        short* samples = (short*)pBuffer;
                        int count = e.BytesRecorded / sizeof(short);
                        float step = 1.0f / Math.Max(1, _capture!.WaveFormat.SampleRate * _capture.WaveFormat.Channels * 30 / 1000);
                        for (int i = 0; i < count; i++)
                        {
                            samples[i] = (short)(samples[i] * _fadeInGain);
                            if (_fadeInGain < 1.0f)
                            {
                                _fadeInGain = Math.Min(1.0f, _fadeInGain + step);
                            }
                        }
                    }
                }

                _multiOutputService.DistributeSystemAudioBlock((IntPtr)pBuffer, e.BytesRecorded);
            }
        }

        public void Dispose()
        {
            StopRouting();
        }

        /// <summary>
        /// Low-latency WASAPI loopback capture using an 18ms buffer (~9ms interval) instead of NAudio's 100ms default.
        /// </summary>
        private sealed class LowLatencyLoopbackCapture : WasapiCapture
        {
            public LowLatencyLoopbackCapture(MMDevice captureDevice, int bufferMs = 18)
                : base(captureDevice, false, bufferMs)
            {
            }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
            {
                return AudioClientStreamFlags.Loopback;
            }
        }

        /// <summary>
        /// Provides continuous zero-amplitude silence to keep the WASAPI endpoint clock active without idle dropouts.
        /// </summary>
        private sealed class ContinuousSilenceProvider : IWaveProvider
        {
            public WaveFormat WaveFormat { get; }

            public ContinuousSilenceProvider(WaveFormat waveFormat)
            {
                WaveFormat = waveFormat;
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
        }
    }

    /// <summary>
    /// Interop helper for Windows CoreAudio IPolicyConfig to seamlessly switch the default audio output endpoint.
    /// </summary>
    internal static class WindowsAudioPolicyConfig
    {
        [ComImport]
        [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class PolicyConfigClientComObject
        {
        }

        [ComImport]
        [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bDefault, IntPtr ppFormat);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, bool bVisible);
        }

        public static bool SetDefaultEndpoint(string endpointId)
        {
            if (string.IsNullOrWhiteSpace(endpointId)) return false;

            try
            {
                var client = new PolicyConfigClientComObject();
                if (client is IPolicyConfig policyConfig)
                {
                    policyConfig.SetDefaultEndpoint(endpointId, 0); // eConsole
                    policyConfig.SetDefaultEndpoint(endpointId, 1); // eMultimedia
                    policyConfig.SetDefaultEndpoint(endpointId, 2); // eCommunications
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsAudioPolicyConfig] SetDefaultEndpoint failed: {ex.Message}");
            }
            return false;
        }
    }
}
