using System;
using System.IO;
using System.Text.Json;
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
    }

    public class SettingsService
    {
        private readonly string _settingsPath;

        public SettingsService()
        {
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KakiEQ");
            _settingsPath = Path.Combine(dataFolder, "settings.json");
            Directory.CreateDirectory(dataFolder);
        }

        public AppSettings LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    return new AppSettings();
                }
            }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }
    }
}

