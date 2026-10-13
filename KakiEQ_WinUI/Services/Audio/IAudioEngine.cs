using System;
using System.Threading.Tasks;

namespace KakiEQ_WinUI.Services.Audio
{
    public interface IAudioEngine
    {
        Task PlayAsync();
        Task PauseAsync();
        Task StopAsync();
        Task LoadFileAsync(string filePath);
        void SetPosition(TimeSpan position);
        void SetVolume(float volume); // 0.0 to 1.0

        bool IsPlaying { get; }
        TimeSpan CurrentPosition { get; }
        TimeSpan TotalDuration { get; }
        float Volume { get; }

        event EventHandler PlaybackPositionChanged;
        event EventHandler PlaybackStateChanged;

        EqualizerBase Equalizer { get; }
    }
}

