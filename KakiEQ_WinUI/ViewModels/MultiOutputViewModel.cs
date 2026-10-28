using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ManagedBass;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Audio;

namespace KakiEQ_WinUI.ViewModels
{
    public class MultiOutputViewModel : ViewModelBase
    {
        private readonly MultiOutputDeviceService _multiOutputService;
        private readonly SystemAudioRoutingService _routingService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        public ReadOnlyObservableCollection<AudioOutputDevice> Devices => _multiOutputService.Devices;

        public int ActiveDevicesCount => Devices.Count(d => d.IsEnabled);
        public int TotalDevicesCount => Devices.Count;
        public string ActiveDevicesSummary => $"{ActiveDevicesCount} de {TotalDevicesCount} salidas activas";
        public bool HasDevices => Devices.Count > 0;
        public bool HasMultipleActiveDevices => ActiveDevicesCount > 1;

        public bool IsSystemRoutingActive
        {
            get => _routingService.IsRouting;
            set
            {
                if (_routingService.IsRouting != value)
                {
                    if (value) _routingService.StartRouting();
                    else _routingService.StopRouting();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RoutingStatusSummary));
                    OnPropertyChanged(nameof(RoutingBadgeText));
                    OnPropertyChanged(nameof(RoutingBadgeColor));
                }
            }
        }

        public string RoutingStatusSummary => _routingService.RoutingStatusSummary;
        public string RoutingBadgeText => IsSystemRoutingActive ? "Transmitiendo Audio de Windows" : "Modo Nativo (Sin Driver)";
        public string RoutingBadgeColor => IsSystemRoutingActive ? "#4CAF50" : "#BB86FC";
        public bool HasLegacyVirtualDriverDevice => _routingService.HasLegacyVirtualDriverDevice;

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public float MasterVolume
        {
            get => _multiOutputService.MasterVolume;
            set
            {
                if (Math.Abs(_multiOutputService.MasterVolume - value) > 0.005f)
                {
                    _multiOutputService.MasterVolume = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MasterVolumePercent));
                    OnPropertyChanged(nameof(MasterVolumePercentFormatted));
                }
            }
        }

        public int MasterVolumePercent
        {
            get => (int)Math.Round(MasterVolume * 100);
            set => MasterVolume = value / 100f;
        }

        public string MasterVolumePercentFormatted => $"{MasterVolumePercent}%";

        public ICommand ToggleDeviceCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand MuteDeviceCommand { get; }
        public ICommand TestDeviceCommand { get; }
        public ICommand ToggleSystemRoutingCommand { get; }
        public ICommand RemoveLegacyDriverCommand { get; }
        public ICommand OpenWindowsSoundSettingsCommand { get; }

        public MultiOutputViewModel(MultiOutputDeviceService multiOutputService, SystemAudioRoutingService routingService)
        {
            _multiOutputService = multiOutputService ?? throw new ArgumentNullException(nameof(multiOutputService));
            _routingService = routingService ?? throw new ArgumentNullException(nameof(routingService));
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            _multiOutputService.DevicesUpdated += (s, e) =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    OnPropertyChanged(nameof(Devices));
                    NotifyCountsChanged();
                });
            };

            _routingService.RoutingStateChanged += (s, e) =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    OnPropertyChanged(nameof(IsSystemRoutingActive));
                    OnPropertyChanged(nameof(RoutingStatusSummary));
                    OnPropertyChanged(nameof(RoutingBadgeText));
                    OnPropertyChanged(nameof(RoutingBadgeColor));
                });
            };

            ToggleDeviceCommand = new RelayCommand<AudioOutputDevice>(device =>
            {
                if (device == null) return;
                bool newState = !device.IsEnabled;
                _multiOutputService.SetDeviceEnabled(device, newState);
                NotifyCountsChanged();
            });

            MuteDeviceCommand = new RelayCommand<AudioOutputDevice>(device =>
            {
                if (device == null) return;
                bool newMute = !device.IsMuted;
                _multiOutputService.SetDeviceMuted(device, newMute);
                OnPropertyChanged(nameof(Devices));
            });

            RefreshDevicesCommand = new RelayCommand(() =>
            {
                _multiOutputService.RefreshDevices();
                NotifyCountsChanged();
                NotifyDriverChanged();
            });

            TestDeviceCommand = new RelayCommand<AudioOutputDevice>(device =>
            {
                if (device == null) return;
                PlayTestToneOnDevice(device.DeviceIndex);
            });

            RemoveLegacyDriverCommand = new RelayCommand(async () =>
            {
                IsBusy = true;
                await _routingService.RemoveLegacyVirtualDriverDeviceAsync();
                NotifyDriverChanged();
                IsBusy = false;
            });

            ToggleSystemRoutingCommand = new RelayCommand(() =>
            {
                if (_routingService.IsRouting)
                {
                    _routingService.StopRouting();
                }
                else
                {
                    _routingService.StartRouting();
                }
                OnPropertyChanged(nameof(IsSystemRoutingActive));
                OnPropertyChanged(nameof(RoutingStatusSummary));
                OnPropertyChanged(nameof(RoutingBadgeText));
                OnPropertyChanged(nameof(RoutingBadgeColor));
            });

            OpenWindowsSoundSettingsCommand = new RelayCommand(() =>
            {
                SystemAudioRoutingService.OpenWindowsSoundSettings();
            });
        }

        private void NotifyDriverChanged()
        {
            OnPropertyChanged(nameof(HasLegacyVirtualDriverDevice));
            OnPropertyChanged(nameof(IsSystemRoutingActive));
            OnPropertyChanged(nameof(RoutingStatusSummary));
            OnPropertyChanged(nameof(RoutingBadgeText));
            OnPropertyChanged(nameof(RoutingBadgeColor));
        }

        public void UpdateDeviceVolume(AudioOutputDevice device, float volume)
        {
            _multiOutputService.SetDeviceVolume(device, volume);
        }

        public void SetDeviceEnabled(AudioOutputDevice device, bool enabled)
        {
            if (device == null) return;
            _multiOutputService.SetDeviceEnabled(device, enabled);
            NotifyCountsChanged();
        }

        public void SetDeviceMuted(AudioOutputDevice device, bool muted)
        {
            if (device == null) return;
            _multiOutputService.SetDeviceMuted(device, muted);
            NotifyCountsChanged();
        }

        private void NotifyCountsChanged()
        {
            OnPropertyChanged(nameof(ActiveDevicesCount));
            OnPropertyChanged(nameof(TotalDevicesCount));
            OnPropertyChanged(nameof(ActiveDevicesSummary));
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(HasMultipleActiveDevices));
        }

        private static void PlayTestToneOnDevice(int deviceIndex)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                int prevDevice = Bass.CurrentDevice;
                try
                {
                    // Ensure initialized
                    Bass.Init(deviceIndex, 44100, DeviceInitFlags.Default, IntPtr.Zero);
                    Bass.CurrentDevice = deviceIndex;

                    // Generate a gentle 528 Hz pleasant test chime
                    int sampleRate = 44100;
                    int durationSeconds = 1;
                    int numSamples = sampleRate * durationSeconds;
                    short[] pcm = new short[numSamples * 2]; // Stereo 16-bit

                    double freq = 528.0; // Harmonic tone
                    for (int i = 0; i < numSamples; i++)
                    {
                        double t = (double)i / sampleRate;
                        // Envelope: quick attack, smooth exponential decay
                        double envelope = Math.Exp(-3.5 * t);
                        short sample = (short)(Math.Sin(2.0 * Math.PI * freq * t) * envelope * 16000.0);
                        pcm[i * 2] = sample;     // Left
                        pcm[i * 2 + 1] = sample; // Right
                    }

                    byte[] pcmBytes = new byte[pcm.Length * sizeof(short)];
                    Buffer.BlockCopy(pcm, 0, pcmBytes, 0, pcmBytes.Length);

                    int stream = Bass.CreateStream(sampleRate, 2, BassFlags.Default, StreamProcedureType.Push);
                    if (stream != 0)
                    {
                        Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, 0.8f);
                        Bass.StreamPutData(stream, pcmBytes, pcmBytes.Length);
                        Bass.ChannelPlay(stream);
                        System.Threading.Thread.Sleep(1200);
                        Bass.ChannelStop(stream);
                        Bass.StreamFree(stream);
                    }
                }
                catch { }
                finally
                {
                    Bass.CurrentDevice = prevDevice;
                }
            });
        }
    }
}
