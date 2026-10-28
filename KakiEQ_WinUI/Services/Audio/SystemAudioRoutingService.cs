using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using ManagedBass;

namespace KakiEQ_WinUI.Services.Audio
{
    public class SystemAudioRoutingService : IDisposable
    {
        private readonly MultiOutputDeviceService _multiOutputService;
        private int _recordChannelHandle = 0;
        private RecordProcedure? _recordProc;
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
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\VirtualAudioDriver");
                    return key != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string DriverStatusSummary
        {
            get
            {
                if (IsDriverInstalled)
                {
                    return _isRouting 
                        ? "Transmitiendo audio del sistema a todas las salidas" 
                        : "Dispositivo virtual listo (Inactivo)";
                }
                return "Dispositivo virtual no instalado";
            }
        }

        public SystemAudioRoutingService(MultiOutputDeviceService multiOutputService)
        {
            _multiOutputService = multiOutputService ?? throw new ArgumentNullException(nameof(multiOutputService));
            _recordProc = OnRecordAudioBlock;
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
                    string infPath = Path.Combine(driverFolder, "VirtualAudioDriver.inf");
                    string exePath = Path.Combine(driverFolder, "nefconc.exe");

                    if (!File.Exists(infPath) || !File.Exists(exePath))
                    {
                        Debug.WriteLine($"[SystemAudioRouting] Driver files not found in {driverFolder}");
                        return false;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"install \"{infPath}\" \"ROOT\\VirtualAudioDriver\" --no-duplicates",
                        WorkingDirectory = driverFolder,
                        UseShellExecute = true,
                        Verb = "runas" // Elevate with UAC
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
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

        public async Task<bool> UninstallDriverAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_isRouting)
                    {
                        StopRouting();
                    }

                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string driverFolder = Path.Combine(baseDir, "Assets", "Driver");
                    string exePath = Path.Combine(driverFolder, "nefconc.exe");

                    if (!File.Exists(exePath)) return false;

                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "remove \"ROOT\\VirtualAudioDriver\"",
                        WorkingDirectory = driverFolder,
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit();
                        NotifyDriverStatus();
                        return !IsDriverInstalled;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemAudioRouting] UninstallDriverAsync error: {ex.Message}");
                }

                NotifyDriverStatus();
                return false;
            });
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
                // Find virtual driver recording device index
                int targetRecordDevice = FindVirtualRecordDeviceIndex();

                // Free previous recording device if needed
                Bass.RecordFree();

                // Initialize recording device (48000 Hz, stereo)
                if (!Bass.RecordInit(targetRecordDevice))
                {
                    int err = (int)Bass.LastError;
                    if (err != 14 && err != 0) // 14 = already initialized
                    {
                        Debug.WriteLine($"[SystemAudioRouting] Bass.RecordInit failed on dev {targetRecordDevice}: {Bass.LastError}");
                    }
                }

                // Prepare MultiOutput push streams for all enabled devices
                _multiOutputService.IsSystemRoutingActive = true;

                // Start recording stream
                _recordChannelHandle = Bass.RecordStart(48000, 2, BassFlags.Default, _recordProc!, IntPtr.Zero);
                if (_recordChannelHandle == 0)
                {
                    Debug.WriteLine($"[SystemAudioRouting] Bass.RecordStart failed: {Bass.LastError}");
                    _multiOutputService.IsSystemRoutingActive = false;
                    return false;
                }

                _isRouting = true;
                RoutingStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemAudioRouting] StartRouting error: {ex.Message}");
                _multiOutputService.IsSystemRoutingActive = false;
                return false;
            }
        }

        public void StopRouting()
        {
            if (!_isRouting) return;

            try
            {
                if (_recordChannelHandle != 0)
                {
                    Bass.ChannelStop(_recordChannelHandle);
                    _recordChannelHandle = 0;
                }

                Bass.RecordFree();
                _multiOutputService.IsSystemRoutingActive = false;
                _isRouting = false;
                RoutingStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemAudioRouting] StopRouting error: {ex.Message}");
            }
        }

        private int FindVirtualRecordDeviceIndex()
        {
            int count = Bass.RecordingDeviceCount;
            // 1. Search for Virtual Audio Driver endpoint
            for (int i = 0; i < count; i++)
            {
                if (Bass.RecordGetDeviceInfo(i, out var info) && info.IsEnabled)
                {
                    string name = info.Name.ToLowerInvariant();
                    if (name.Contains("virtual") || name.Contains("cable") || name.Contains("mtt") || name.Contains("kaki"))
                    {
                        return i;
                    }
                }
            }

            // 2. Fallback to default recording device (-1)
            return -1;
        }

        private bool OnRecordAudioBlock(int handle, IntPtr buffer, int length, IntPtr user)
        {
            if (!_isRouting || buffer == IntPtr.Zero || length <= 0)
            {
                return true; // continue recording
            }

            // ZERO-ALLOCATION loop: distribute audio block directly to all active outputs
            _multiOutputService.DistributeSystemAudioBlock(buffer, length);

            return true; // continue recording
        }

        public void Dispose()
        {
            StopRouting();
        }
    }
}
