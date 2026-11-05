using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Services.Data
{
    public class AppSettings
    {
        public string MusicFolderPath { get; set; } = string.Empty;
        public float Volume { get; set; } = 0.8f;
        public bool EqualizerEnabled { get; set; } = true;
        public float[] EqualizerGains { get; set; } = new float[10];
        public float PreampGain { get; set; } = 0f;
        public string ActivePresetName { get; set; } = "Plano";
        public System.Collections.Generic.List<EqualizerPreset> CustomPresets { get; set; } = new();
        public bool IsDarkMode { get; set; } = true;
        public bool CrossfadeEnabled { get; set; } = false;
        public int CrossfadeDurationSeconds { get; set; } = 3;
        public bool StartWithWindows { get; set; } = false;
        public string RunInBackgroundPreference { get; set; } = "Ask"; // "Ask", "Background", "Exit"
        public bool ShowAudioTechnicalInfo { get; set; } = false;
    }

    public class SettingsService
    {
        private readonly string _settingsPath;
        private readonly object _lock = new();
        private AppSettings? _cachedSettings;
        private CancellationTokenSource? _saveDebounceCts;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public SettingsService()
        {
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ");
            _settingsPath = Path.Combine(dataFolder, "settings.json");
            Directory.CreateDirectory(dataFolder);
        }

        public AppSettings LoadSettings()
        {
            lock (_lock)
            {
                if (_cachedSettings != null)
                {
                    return _cachedSettings;
                }

                if (File.Exists(_settingsPath))
                {
                    try
                    {
                        var json = File.ReadAllText(_settingsPath);
                        _cachedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                        return _cachedSettings;
                    }
                    catch
                    {
                        _cachedSettings = new AppSettings();
                        return _cachedSettings;
                    }
                }

                _cachedSettings = new AppSettings();
                return _cachedSettings;
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            lock (_lock)
            {
                _cachedSettings = settings;
                _saveDebounceCts?.Cancel();
                _saveDebounceCts = new CancellationTokenSource();
                var token = _saveDebounceCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(180, token);
                        if (token.IsCancellationRequested) return;

                        string json;
                        lock (_lock)
                        {
                            json = JsonSerializer.Serialize(_cachedSettings, JsonOptions);
                        }
                        await File.WriteAllTextAsync(_settingsPath, json, token);
                    }
                    catch
                    {
                    }
                }, token);
            }
        }
    }
}
