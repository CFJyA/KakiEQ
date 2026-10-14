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
    public class EqualizerViewModel : ViewModelBase
    {
        private readonly BassAudioEngine _audioEngine;
        private readonly SettingsService _settingsService;

        public ObservableCollection<EqualizerBand> Bands { get; } = new();
        public ObservableCollection<EqualizerPreset> Presets { get; } = new();

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    _audioEngine.Equalizer.IsEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        private float _preampGain = 0f;
        public float PreampGain
        {
            get => _preampGain;
            set
            {
                if (Math.Abs(_preampGain - value) > 0.05f)
                {
                    _preampGain = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreampGainFormatted));
                    SaveSettings();
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
                if (value != null)
                {
                    ApplyPreset(value);
                }
            }
        }

        public ICommand ResetCommand { get; }
        public event EventHandler? BandsChanged;

        public EqualizerViewModel(BassAudioEngine audioEngine, SettingsService settingsService)
        {
            _audioEngine = audioEngine;
            _settingsService = settingsService;

            var bands = _audioEngine.Equalizer.Bands;
            string[] labels = { "32Hz", "64Hz", "125Hz", "250Hz", "500Hz", "1kHz", "2kHz", "4kHz", "8kHz", "16kHz" };

            var savedSettings = _settingsService.LoadSettings();
            _isEnabled = savedSettings.EqualizerEnabled;
            _preampGain = savedSettings.PreampGain;
            _audioEngine.Equalizer.IsEnabled = _isEnabled;

            for (int i = 0; i < bands.Length; i++)
            {
                float savedGain = (savedSettings.EqualizerGains != null && savedSettings.EqualizerGains.Length > i)
                    ? savedSettings.EqualizerGains[i]
                    : 0f;

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
                        _audioEngine.Equalizer.SetBandGain(b.Index, b.Gain);
                        SaveSettings();
                        BandsChanged?.Invoke(this, EventArgs.Empty);
                    }
                };

                Bands.Add(band);
            }

            InitPresets(savedSettings);
            ResetCommand = new RelayCommand(Reset);
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

            var currentGains = Bands.Select(b => b.Gain).ToArray();
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

            SaveSettings();
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
            for (int i = 0; i < Bands.Count && i < preset.Gains.Length; i++)
            {
                Bands[i].Gain = preset.Gains[i];
            }
            SaveSettings();
            BandsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            foreach (var b in Bands)
            {
                b.Gain = 0f;
            }
            PreampGain = 0f;
            _audioEngine.Equalizer.Reset();
            SaveSettings();
            BandsChanged?.Invoke(this, EventArgs.Empty);
            SelectedPreset = Presets.FirstOrDefault(p => p.Name == "Plano");
        }

        private void SaveSettings()
        {
            var settings = _settingsService.LoadSettings();
            settings.EqualizerEnabled = IsEnabled;
            settings.PreampGain = PreampGain;
            if (settings.EqualizerGains == null || settings.EqualizerGains.Length != Bands.Count)
            {
                settings.EqualizerGains = new float[Bands.Count];
            }
            for (int i = 0; i < Bands.Count; i++)
            {
                settings.EqualizerGains[i] = Bands[i].Gain;
            }
            if (SelectedPreset != null)
            {
                settings.ActivePresetName = SelectedPreset.Name;
            }

            settings.CustomPresets = Presets.Where(p => p.IsCustom).ToList();
            _settingsService.SaveSettings(settings);
        }
    }
}
