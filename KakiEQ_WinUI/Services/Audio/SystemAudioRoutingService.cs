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

        public bool HasLegacyVirtualDriverDevice
        {
            get
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\MEDIA\0001");
                    return key != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string RoutingStatusSummary
        {
            get
            {
                if (_isRouting)
                {
                    return "Transmitiendo todo el audio de Windows a tus dispositivos activos en tiempo real.";
                }
                return "Transmisión en pausa. Haz clic en 'Iniciar Transmisión' para clonar el sonido de Windows a todas tus salidas.";
            }
        }

        public SystemAudioRoutingService(MultiOutputDeviceService multiOutputService)
        {
            _multiOutputService = multiOutputService ?? throw new ArgumentNullException(nameof(multiOutputService));
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
                _capture = new WasapiLoopbackCapture();

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

        public async Task RemoveLegacyVirtualDriverDeviceAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string driverFolder = Path.Combine(baseDir, "Assets", "Driver");
                    string exePath = Path.Combine(driverFolder, "nefconc.exe");

                    if (!File.Exists(exePath)) return;

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "remove \"ROOT\\VirtualAudioDriver\"",
                        WorkingDirectory = driverFolder,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();

                    // Also try remove ROOT\MEDIA\0001
                    var psi2 = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "remove \"ROOT\\MEDIA\\0001\"",
                        WorkingDirectory = driverFolder,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var proc2 = Process.Start(psi2);
                    proc2?.WaitForExit();
                }
                catch { }

                DriverStatusChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        public void Dispose()
        {
            StopRouting();
        }
    }
}
