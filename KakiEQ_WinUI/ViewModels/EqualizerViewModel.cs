using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using KakiEQ_WinUI.Helpers;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Audio;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.ViewModels
{
    public class DspTargetItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = "master";
        public string DisplayName { get; set; } = "🌐 Todas las Salidas (Master)";
        public string Glyph { get; set; } = "\uE767";
        public bool IsMaster => Id == "master";
        public AudioOutputDevice? Device { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class EqualizerViewModel : ViewModelBase
    {
        private readonly BassAudioEngine _audioEngine;
        private readonly SettingsService _settingsService;

        public ObservableCollection<EqualizerBand> Bands { get; } = new();
        public ObservableCollection<EqualizerPreset> Presets { get; } = new();
        public ObservableCollection<DspTargetItem> DspTargets { get; } = new();

        private DspTargetItem? _selectedTarget;
        public DspTargetItem? SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                if (_selectedTarget != value)
                {
                    OnTargetChanging(_selectedTarget);
                    _selectedTarget = value;
                    OnPropertyChanged();
                    OnTargetChanged(value);
                }
            }
        }

        // Master state cache
        private bool _masterIsEnabled = true;
        private double _masterPreampGain = 0.0;
        private readonly float[] _masterGains = new float[10];
        private string _masterPresetName = "Plano";

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();

                    if (_isBatchUpdating) return;

                    if (SelectedTarget == null || SelectedTarget.IsMaster)
                    {
                        _masterIsEnabled = value;
                        _audioEngine.Equalizer.IsEnabled = value;
                        SaveSettings();
                    }
                    else if (SelectedTarget.Device is { } dev)
                    {
                        dev.EqIsEnabled = value;
                        if (_audioEngine.Equalizer is BassEqualizer bassEq)
                        {
                            bassEq.ApplyAllDeviceBands(dev);
                        }
                        _audioEngine.MultiOutput.SetDeviceVolume(dev, dev.Volume);
                        SaveDeviceProfile(dev);
                    }
                }
            }
        }

        private double _preampGain = 0.0;
        public double PreampGain
        {
            get => _preampGain;
            set
            {
                if (Math.Abs(_preampGain - value) > 0.05)
                {
                    _preampGain = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreampGainFormatted));

                    if (_isBatchUpdating) return;

                    if (SelectedTarget == null || SelectedTarget.IsMaster)
                    {
                        _masterPreampGain = value;
                        if (_audioEngine.Equalizer is BassEqualizer bassEq)
                        {
                            bassEq.MasterPreampGain = (float)value;
                        }
                        _audioEngine.MultiOutput.UpdatePrimaryDeviceVolume();
                        SaveSettings();
                    }
                    else if (SelectedTarget.Device is { } dev)
                    {
                        dev.EqPreampGain = (float)value;
                        _audioEngine.MultiOutput.SetDeviceVolume(dev, dev.Volume);
                        SaveDeviceProfile(dev);
                    }
                }
            }
        }

        public string PreampGainFormatted => PreampGain > 0 ? $"+{PreampGain:0.0} dB" : $"{PreampGain:0.0} dB";

        private EqualizerPreset? _selectedPreset;
        public EqualizerPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                _selectedPreset = value;
                OnPropertyChanged();
                if (value != null && !_isBatchUpdating)
                {
                    ApplyPreset(value);
                }
            }
        }

        private bool _isBatchUpdating = false;
        public ICommand ResetCommand { get; }
        public event EventHandler? BandsChanged;

        public EqualizerViewModel(BassAudioEngine audioEngine, SettingsService settingsService)
        {
            _audioEngine = audioEngine;
            _settingsService = settingsService;

            var bands = _audioEngine.Equalizer.Bands;
            string[] labels = { "32Hz", "64Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" };

            var savedSettings = _settingsService.LoadSettings();
            _masterIsEnabled = savedSettings.EqualizerEnabled;
            _masterPreampGain = savedSettings.PreampGain;
            _masterPresetName = savedSettings.ActivePresetName ?? "Plano";

            _isEnabled = _masterIsEnabled;
            _preampGain = _masterPreampGain;
            _audioEngine.Equalizer.IsEnabled = _isEnabled;
            if (_audioEngine.Equalizer is BassEqualizer be)
            {
                be.MasterPreampGain = (float)_preampGain;
            }

            for (int i = 0; i < bands.Length; i++)
            {
                float savedGain = (savedSettings.EqualizerGains != null && savedSettings.EqualizerGains.Length > i)
                    ? savedSettings.EqualizerGains[i]
                    : 0f;

                _masterGains[i] = savedGain;
                _audioEngine.Equalizer.SetBandGain(i, savedGain);

                var band = new EqualizerBand
                {
                    Index = i,
                    Frequency = bands[i],
                    Label = i < labels.Length ? labels[i] : $"{bands[i]}Hz",
                    Gain = savedGain
                };

                band.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(EqualizerBand.Gain) && s is EqualizerBand b)
                    {
                        if (_isBatchUpdating) return;

                        if (SelectedTarget == null || SelectedTarget.IsMaster)
                        {
                            _masterGains[b.Index] = (float)b.Gain;
                            _audioEngine.Equalizer.SetBandGain(b.Index, (float)b.Gain);
                            SaveSettings();
                        }
                        else if (SelectedTarget.Device is { } dev)
                        {
                            if (dev.EqGains == null || dev.EqGains.Length < 10) dev.EqGains = new float[10];
                            dev.EqGains[b.Index] = (float)b.Gain;
                            dev.EqProfileName = "Personalizado";
                            dev.EqIsCustom = true;
                            if (_audioEngine.Equalizer is BassEqualizer bassEq)
                            {
                                bassEq.SetDeviceBandGain(dev, b.Index, (float)b.Gain);
                            }
                            SaveDeviceProfile(dev);
                        }

                        BandsChanged?.Invoke(this, EventArgs.Empty);
                    }
                };

                Bands.Add(band);
            }

            InitPresets(savedSettings);
            ResetCommand = new RelayCommand(Reset);

            // Populate DSP targets and subscribe to device changes
            RefreshDspTargets();
            _audioEngine.MultiOutput.DevicesUpdated += (s, e) =>
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(RefreshDspTargets);
            };
        }

        public void RefreshDspTargets()
        {
            var prevSelectedId = SelectedTarget?.Id ?? "master";
            DspTargets.Clear();

            // 1. Master target
            DspTargets.Add(new DspTargetItem
            {
                Id = "master",
                DisplayName = "🌐 Todas las Salidas (Master)",
                Glyph = "\uE767"
            });

            // 2. Active dynamic devices
            foreach (var dev in _audioEngine.MultiOutput.Devices)
            {
                DspTargets.Add(new DspTargetItem
                {
                    Id = dev.Id,
                    DisplayName = $"{dev.Name}",
                    Glyph = dev.IconGlyph,
                    Device = dev
                });
            }

            var match = DspTargets.FirstOrDefault(t => t.Id.Equals(prevSelectedId, StringComparison.OrdinalIgnoreCase));
            _selectedTarget = match ?? DspTargets.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedTarget));
        }

        public void SelectTargetDevice(string? deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                SelectedTarget = DspTargets.FirstOrDefault(t => t.IsMaster);
                return;
            }

            var target = DspTargets.FirstOrDefault(t => t.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                SelectedTarget = target;
            }
            else
            {
                RefreshDspTargets();
                var retry = DspTargets.FirstOrDefault(t => t.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
                if (retry != null)
                {
                    SelectedTarget = retry;
                }
            }
        }

        private void OnTargetChanging(DspTargetItem? oldTarget)
        {
            if (oldTarget == null) return;

            // Persist current in-memory edits to the outgoing target before switching
            if (oldTarget.IsMaster)
            {
                _masterIsEnabled = IsEnabled;
                _masterPreampGain = PreampGain;
                for (int i = 0; i < Bands.Count && i < _masterGains.Length; i++)
                {
                    _masterGains[i] = (float)Bands[i].Gain;
                }
                _masterPresetName = SelectedPreset?.Name ?? "Plano";
            }
            else if (oldTarget.Device is { } dev)
            {
                dev.EqIsEnabled = IsEnabled;
                dev.EqPreampGain = (float)PreampGain;
                if (dev.EqGains == null || dev.EqGains.Length < 10) dev.EqGains = new float[10];
                for (int i = 0; i < Bands.Count && i < dev.EqGains.Length; i++)
                {
                    dev.EqGains[i] = (float)Bands[i].Gain;
                }
                SaveDeviceProfile(dev);
            }
        }

        private void OnTargetChanged(DspTargetItem? newTarget)
        {
            _isBatchUpdating = true;
            try
            {
                if (newTarget == null || newTarget.IsMaster)
                {
                    _isEnabled = _masterIsEnabled;
                    _preampGain = _masterPreampGain;
                    for (int i = 0; i < Bands.Count && i < _masterGains.Length; i++)
                    {
                        Bands[i].Gain = _masterGains[i];
                    }
                    _selectedPreset = Presets.FirstOrDefault(p => p.Name.Equals(_masterPresetName, StringComparison.OrdinalIgnoreCase));
                }
                else if (newTarget.Device is { } dev)
                {
                    _isEnabled = dev.EqIsEnabled;
                    _preampGain = dev.EqPreampGain;
                    if (dev.EqGains != null)
                    {
                        for (int i = 0; i < Bands.Count && i < dev.EqGains.Length; i++)
                        {
                            Bands[i].Gain = dev.EqGains[i];
                        }
                    }
                    _selectedPreset = Presets.FirstOrDefault(p => p.Name.Equals(dev.EqProfileName, StringComparison.OrdinalIgnoreCase));
                }
            }
            finally
            {
                _isBatchUpdating = false;
            }

            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(PreampGain));
            OnPropertyChanged(nameof(PreampGainFormatted));
            OnPropertyChanged(nameof(SelectedPreset));
            BandsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void InitPresets(AppSettings savedSettings)
        {
            Presets.Clear();
            // Default presets
            Presets.Add(new EqualizerPreset { Name = "Plano", Gains = new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, IsCustom = false });
            Presets.Add(new EqualizerPreset { Name = "Bass Boost", Gains = new float[] { 7f, 6f, 4f, 2f, 0, 0, 0, 0, 0, 0 }, IsCustom = false });
            Presets.Add(new EqualizerPreset { Name = "Rock", Gains = new float[] { 4.5f, 3.0f, -1.5f, -2.5f, -0.5f, 2.5f, 4.0f, 5.5f, 6.0f, 6.0f }, IsCustom = false });
            Presets.Add(new EqualizerPreset { Name = "Pop", Gains = new float[] { -1.5f, -1.0f, 2.0f, 3.5f, 4.5f, 3.0f, 1.0f, -1.0f, -1.5f, -1.5f }, IsCustom = false });
            Presets.Add(new EqualizerPreset { Name = "Jazz", Gains = new float[] { 3.5f, 2.5f, 1.0f, 1.5f, -1.5f, -1.5f, 0, 1.5f, 3.0f, 3.5f }, IsCustom = false });
            Presets.Add(new EqualizerPreset { Name = "Voz / Podcast", Gains = new float[] { -2.0f, -3.5f, -2.0f, 1.5f, 4.0f, 4.5f, 3.5f, 1.5f, 0, -1.0f }, IsCustom = false });
            Presets.Add(new EqualizerPreset { Name = "Electrónica", Gains = new float[] { 5.0f, 4.5f, 2.0f, 0, -2.0f, 1.5f, 3.0f, 4.0f, 5.0f, 5.5f }, IsCustom = false });

            // User custom presets
            if (savedSettings.CustomPresets != null)
            {
                foreach (var custom in savedSettings.CustomPresets)
                {
                    custom.IsCustom = true;
                    Presets.Add(custom);
                }
            }

            // Restore active preset
            var active = Presets.FirstOrDefault(p => p.Name.Equals(savedSettings.ActivePresetName, StringComparison.OrdinalIgnoreCase));
            if (active != null)
            {
                _selectedPreset = active;
                OnPropertyChanged(nameof(SelectedPreset));
            }
        }

        public void SaveCurrentAsPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string trimmedName = name.Trim();

            var currentGains = Bands.Select(b => (float)b.Gain).ToArray();
            var existing = Presets.FirstOrDefault(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));

            if (existing != null && existing.IsCustom)
            {
                existing.Gains = currentGains;
                SelectedPreset = existing;
            }
            else
            {
                var newPreset = new EqualizerPreset
                {
                    Name = trimmedName,
                    Gains = currentGains,
                    IsCustom = true
                };
                Presets.Add(newPreset);
                SelectedPreset = newPreset;
            }

            if (SelectedTarget != null && !SelectedTarget.IsMaster && SelectedTarget.Device is { } dev)
            {
                dev.EqProfileName = trimmedName;
                dev.EqIsCustom = true;
                SaveDeviceProfile(dev);
            }
            else
            {
                SaveSettings();
            }

            MainWindow.Instance?.ShowToast($"Perfil de audio \"{trimmedName}\" guardado");
        }

        public void DeletePreset(EqualizerPreset preset)
        {
            if (preset == null || !preset.IsCustom) return;

            Presets.Remove(preset);
            SaveSettings();

            if (SelectedPreset == preset)
            {
                SelectedPreset = Presets.FirstOrDefault();
            }

            MainWindow.Instance?.ShowToast($"Perfil \"{preset.Name}\" eliminado");
        }

        public void ApplyPreset(EqualizerPreset preset)
        {
            _isBatchUpdating = true;
            try
            {
                for (int i = 0; i < Bands.Count && i < preset.Gains.Length; i++)
                {
                    Bands[i].Gain = preset.Gains[i];
                }
            }
            finally
            {
                _isBatchUpdating = false;
            }

            if (SelectedTarget == null || SelectedTarget.IsMaster)
            {
                for (int i = 0; i < Bands.Count && i < preset.Gains.Length; i++)
                {
                    _masterGains[i] = preset.Gains[i];
                    _audioEngine.Equalizer.SetBandGain(i, preset.Gains[i]);
                }
                _masterPresetName = preset.Name;
                SaveSettings();
            }
            else if (SelectedTarget.Device is { } dev)
            {
                dev.EqProfileName = preset.Name;
                dev.EqIsCustom = preset.IsCustom;
                if (dev.EqGains == null || dev.EqGains.Length < 10) dev.EqGains = new float[10];
                Array.Copy(preset.Gains, dev.EqGains, Math.Min(preset.Gains.Length, 10));
                if (_audioEngine.Equalizer is BassEqualizer bassEq)
                {
                    bassEq.ApplyAllDeviceBands(dev);
                }
                SaveDeviceProfile(dev);
            }

            BandsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            _isBatchUpdating = true;
            try
            {
                foreach (var b in Bands)
                {
                    b.Gain = 0f;
                }
            }
            finally
            {
                _isBatchUpdating = false;
            }
            PreampGain = 0f;

            if (SelectedTarget == null || SelectedTarget.IsMaster)
            {
                Array.Clear(_masterGains, 0, _masterGains.Length);
                _masterPreampGain = 0f;
                _audioEngine.Equalizer.Reset();
                if (_audioEngine.Equalizer is BassEqualizer be) be.MasterPreampGain = 0f;
                _audioEngine.MultiOutput.UpdatePrimaryDeviceVolume();
                SaveSettings();
                SelectedPreset = Presets.FirstOrDefault(p => p.Name == "Plano");
            }
            else if (SelectedTarget.Device is { } dev)
            {
                dev.EqProfileName = "Plano";
                dev.EqIsCustom = false;
                dev.EqPreampGain = 0f;
                if (dev.EqGains != null) Array.Clear(dev.EqGains, 0, dev.EqGains.Length);
                if (_audioEngine.Equalizer is BassEqualizer bassEq)
                {
                    bassEq.ApplyAllDeviceBands(dev);
                }
                _audioEngine.MultiOutput.SetDeviceVolume(dev, dev.Volume);
                SaveDeviceProfile(dev);
                SelectedPreset = Presets.FirstOrDefault(p => p.Name == "Plano");
            }

            BandsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SaveSettings()
        {
            bool enabled = _masterIsEnabled;
            float preamp = (float)_masterPreampGain;
            float[] gains = (float[])_masterGains.Clone();
            string? activePreset = _masterPresetName;
            var customPresets = Presets.Where(p => p.IsCustom).ToList();

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var settings = _settingsService.LoadSettings();
                    settings.EqualizerEnabled = enabled;
                    settings.PreampGain = preamp;
                    settings.EqualizerGains = gains;
                    if (activePreset != null)
                    {
                        settings.ActivePresetName = activePreset;
                    }
                    settings.CustomPresets = customPresets;
                    _settingsService.SaveSettings(settings);
                }
                catch { }
            });
        }

        private void SaveDeviceProfile(AudioOutputDevice dev)
        {
            var profile = new DeviceEqProfile
            {
                DeviceId = dev.Id,
                DeviceName = dev.Name,
                ProfileName = dev.EqProfileName,
                IsEnabled = dev.EqIsEnabled,
                PreampGain = dev.EqPreampGain,
                Gains = (float[])dev.EqGains.Clone(),
                LastModified = DateTime.UtcNow
            };

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    if (_audioEngine.MultiOutput.DatabaseService != null)
                    {
                        await _audioEngine.MultiOutput.DatabaseService.SaveDeviceEqProfileAsync(profile);
                    }
                }
                catch { }
            });
        }
    }
}
