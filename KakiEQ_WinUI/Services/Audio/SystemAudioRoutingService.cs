using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace KakiEQ_WinUI.Services.Audio
{
    public class SystemAudioRoutingService : IDisposable
    {
        private readonly MultiOutputDeviceService _multiOutputService;
        private WasapiLoopbackCapture? _capture;
        private bool _isRouting = false;

        public event EventHandler? RoutingStateChanged;
        public event EventHandler? DriverStatusChanged;

        public bool IsRouting => _isRouting;

        public bool IsDriverInstalled
        {
            get
            {
                try
                {
                    // 1. Check if the WHQL kernel service is registered
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\VBAudioVACMME");
                    if (key != null) return true;

                    // 2. Check if the audio endpoint exists in Windows MMDevices
                    using var renderKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render");
                    if (renderKey != null)
                    {
                        foreach (var subKeyName in renderKey.GetSubKeyNames())
                        {
                            using var propKey = renderKey.OpenSubKey($@"{subKeyName}\Properties");
                            if (propKey != null)
                            {
                                var name = propKey.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
                                if (!string.IsNullOrEmpty(name) && (name.Contains("CABLE") || name.Contains("Kaki EQ")))
                                {
                                    return true;
                                }
                            }
                        }
                    }
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
                        ? "Dispositivo 'Kaki EQ' transmitiendo audio en tiempo real a todas tus salidas." 
                        : "Dispositivo 'Kaki EQ' detectado en Windows. Activa el interruptor para comenzar la transmisión.";
                }
                return "Controlador WHQL no instalado. Haz clic en 'Instalar Dispositivo Virtual Kaki EQ' para registrarlo en Windows 11.";
            }
        }

        public SystemAudioRoutingService(MultiOutputDeviceService multiOutputService)
        {
            _multiOutputService = multiOutputService ?? throw new ArgumentNullException(nameof(multiOutputService));
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
                        // Fallback to Desktop folder if present
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
                        Verb = "runas" // Elevates with Windows UAC
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        
                        // Automatically customize device endpoint name to "Kaki EQ (Audio Virtual)"
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
                using var renderKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render", true);
                if (renderKey != null)
                {
                    foreach (var subKeyName in renderKey.GetSubKeyNames())
                    {
                        using var propKey = renderKey.OpenSubKey($@"{subKeyName}\Properties", true);
                        if (propKey != null)
                        {
                            var name = propKey.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
                            if (!string.IsNullOrEmpty(name) && name.Contains("CABLE Input"))
                            {
                                propKey.SetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6", "Kaki EQ (Audio Virtual)");
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
            if (_isRouting) return true;

            try
            {
                // Capture system-wide audio directly using native Windows CoreAudio WASAPI loopback
                // Prefer the virtual cable endpoint explicitly if available
                MMDevice? captureDevice = null;
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    captureDevice = endpoints.FirstOrDefault(d => 
                        d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) || 
                        d.FriendlyName.Contains("Kaki EQ", StringComparison.OrdinalIgnoreCase));
                }
                catch { }

                _capture = captureDevice != null 
                    ? new WasapiLoopbackCapture(captureDevice) 
                    : new WasapiLoopbackCapture();

                int sampleRate = _capture.WaveFormat.SampleRate;
                int channels = _capture.WaveFormat.Channels;
                bool isFloat = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat;

                // Configure multi-output push streams with identical audio format (48kHz/44.1kHz Float PCM)
                _multiOutputService.ConfigureStreamFormat(sampleRate, channels, isFloat);
                _multiOutputService.IsSystemRoutingActive = true;

                _capture.DataAvailable += OnWasapiDataAvailable;
                _capture.RecordingStopped += (s, e) =>
                {
                    _isRouting = false;
                    _multiOutputService.IsSystemRoutingActive = false;
                    RoutingStateChanged?.Invoke(this, EventArgs.Empty);
                };

                _capture.StartRecording();
                _isRouting = true;
                RoutingStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemAudioRouting] StartRouting error: {ex.Message}");
                _isRouting = false;
                _multiOutputService.IsSystemRoutingActive = false;
                RoutingStateChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }

        public void StopRouting()
        {
            if (!_isRouting) return;

            try
            {
                _isRouting = false;
                if (_capture != null)
                {
                    _capture.StopRecording();
                    _capture.Dispose();
                    _capture = null;
                }

                _multiOutputService.IsSystemRoutingActive = false;
                RoutingStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemAudioRouting] StopRouting error: {ex.Message}");
            }
        }

        private void OnWasapiDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRouting || e.BytesRecorded <= 0) return;

            unsafe
            {
                fixed (byte* pBuffer = e.Buffer)
                {
                    // ZERO-ALLOCATION loop: distribute audio block directly to all active outputs
                    _multiOutputService.DistributeSystemAudioBlock((IntPtr)pBuffer, e.BytesRecorded);
                }
            }
        }

        public void Dispose()
        {
            StopRouting();
        }
    }
}
