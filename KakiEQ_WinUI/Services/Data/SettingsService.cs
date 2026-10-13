using System;
using System.IO;
using System.Text.Json;

namespace KakiEQ_WinUI.Services.Data
{
    public class AppSettings
    {
        public string MusicFolderPath { get; set; } = string.Empty;
        public float Volume { get; set; } = 0.8f;
        public bool EqualizerEnabled { get; set; } = true;
        public float[] EqualizerGains { get; set; } = new float[10];
        public string ActivePresetName { get; set; } = "Flat";
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

